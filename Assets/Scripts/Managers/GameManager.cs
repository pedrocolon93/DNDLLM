using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DnD.Core;
using DnD.Character;
using DnD.AI;
using DnD.UI;
using DnD.Combat;
using DNDLLM.Map;
using DnD.Data;
using DNDLLM.Services;
using DNDLLM.Utils;
using DNDLLM.World;

namespace DnD.Managers
{
    /// <summary>
    /// Main game manager coordinating all systems
    /// Implements state machine pattern for game flow
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Game State")]
        [SerializeField] private GameState currentState = GameState.MainMenu;

        [Header("Player Configuration")]
        [SerializeField] private CharacterStats playerCharacter;
        [SerializeField] private List<CharacterStats> partyMembers = new List<CharacterStats>();

        [Header("AI Configuration")]
        [SerializeField] private bool useMockLLM = true; // Set to false when using real LLM

        [Header("UI — set by UISceneBuilder")]
        [SerializeField] private DnD.UI.TitleScreen             titleScreen;
        [SerializeField] private DnD.UI.CharacterCreationPopup  characterPopup;
        [SerializeField] private UnityEngine.UI.Button          menuButton;
        [SerializeField] private DnD.UI.InGameMenuPanel         inGameMenuPanel;
        [SerializeField] private UnityEngine.UI.Button          editMapButton;
        [SerializeField] private DnD.UI.EditMapPanel            editMapPanel;
        [SerializeField] private UnityEngine.UI.Button          characterButton;
        [SerializeField] private DnD.UI.CharacterScreenPanel    characterScreenPanel;

        private int _currentSlotIndex = 0;
        private string _campaignSeed = "";
        private string _appearanceDescription = "";
        private string _backstory = "";
        private Texture2D _characterPortrait;
        private List<DnD.Data.TileDescriptionEntry> _pendingTileDescriptions;
        private List<DnD.Data.TileGridEntry>        _pendingTileGrid;
        private readonly MapGraph _mapGraph = new MapGraph();

        private DungeonMaster dungeonMaster;
        private CommandParser commandParser;
        private ILLMProvider llmProvider;
        private StoryTimeline currentCampaign;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private string _initStatus = "starting...";

        private async void Start()
        {
            try
            {
                _initStatus = "initializing systems";
                await InitializeSystemsAsync();
                _initStatus = "yielding frame";
                await System.Threading.Tasks.Task.Yield();

                var chatSearch = FindAnyObjectByType<ChatUI>(FindObjectsInactive.Include);
                if (ChatUI.Instance == null && chatSearch != null)
                    ChatUI.Instance = chatSearch;

                _initStatus = ChatUI.Instance != null ? "ChatUI found" : "ChatUI MISSING";

                if (ChatUI.Instance != null)
                    ChatUI.Instance.OnPlayerInput += HandlePlayerInput;

                if (menuButton != null)
                    menuButton.onClick.AddListener(OnMenuButtonPressed);

                if (inGameMenuPanel != null)
                {
                    inGameMenuPanel.OnSave            = OnSaveFromMenu;
                    inGameMenuPanel.OnLoad            = OnLoadFromMenu;
                    inGameMenuPanel.OnRegenerateTile  = OnRegenerateTileFromMenu;
                    inGameMenuPanel.OnTTSAutoPlayChanged = v =>
                    {
                        if (DNDLLM.Services.TTSService.Instance != null)
                            DNDLLM.Services.TTSService.Instance.AutoPlay = v;
                        SaveCurrentSlot();  // persist immediately
                    };
                    inGameMenuPanel.OnTTSEnabledChanged = v =>
                    {
                        if (DNDLLM.Services.TTSService.Instance != null)
                            DNDLLM.Services.TTSService.Instance.Enabled = v;
                    };
                }

                if (editMapButton != null)
                    editMapButton.onClick.AddListener(OnEditMapButtonPressed);

                if (editMapPanel != null)
                    editMapPanel.OnSaveRequested = () => { SaveCurrentSlot(); ChatUI.Instance?.AddSystemMessage("Map changes saved."); };

                if (characterButton != null)
                    characterButton.onClick.AddListener(OnCharacterButtonPressed);

                ChangeState(GameState.MainMenu);
                _initStatus = "ready - state: " + currentState;
            }
            catch (System.Exception e)
            {
                _initStatus = "EXCEPTION: " + e.Message;
                Debug.LogError("[GameManager] Start exception: " + e);
            }
        }

        private void OnGUI()
        {
            GUI.color = Color.yellow;
            GUI.Label(new Rect(10, 10, 600, 40), $"[GM] {_initStatus} | state={currentState} | ChatUI={(ChatUI.Instance != null ? "OK" : "NULL")}");
            GUI.color = Color.white;
        }

        private async Task InitializeSystemsAsync()
        {
            Debug.Log("[GameManager] Initializing systems...");

            // Initialize LLM Provider
            if (useMockLLM)
            {
                llmProvider = new MockLLMProvider();
            }
            else
            {
                // TODO: Initialize real LLM provider (LLMUnity, OpenAI, etc.)
                // Example: llmProvider = new LLMUnityProvider();
                llmProvider = new MockLLMProvider();
            }

            await llmProvider.InitializeAsync();

            // Initialize Dungeon Master
            dungeonMaster = DungeonMaster.Instance;
            if (dungeonMaster == null)
            {
                GameObject dmObj = new GameObject("DungeonMaster");
                dungeonMaster = dmObj.AddComponent<DungeonMaster>();
                DontDestroyOnLoad(dmObj);
            }
            await dungeonMaster.InitializeAsync(llmProvider);

            // Initialize Command Parser
            commandParser = FindAnyObjectByType<CommandParser>();
            if (commandParser == null)
            {
                GameObject parserObj = new GameObject("CommandParser");
                commandParser = parserObj.AddComponent<CommandParser>();
                DontDestroyOnLoad(parserObj);
            }
            commandParser.Initialize(llmProvider);

            // Ensure player character exists (not wired in scene = create at runtime)
            if (playerCharacter == null)
            {
                GameObject playerObj = new GameObject("Player");
                playerCharacter = playerObj.AddComponent<CharacterStats>();
                DontDestroyOnLoad(playerObj);
            }

            // Subscribe to non-UI events now
            if (dungeonMaster != null)
                dungeonMaster.OnDMResponse += OnDMResponse;

            if (CombatManager.Instance != null)
                CombatManager.Instance.OnCombatMessage += OnCombatMessage;

            Debug.Log("[GameManager] All systems initialized!");
        }

        public void ChangeState(GameState newState)
        {
            Debug.Log($"[GameManager] State: {currentState} -> {newState}");
            currentState = newState;

            // Hide popups that must not linger across state changes
            if (newState != GameState.CharacterCreation && characterPopup != null)
                characterPopup.gameObject.SetActive(false);
            if (newState != GameState.MainMenu && titleScreen != null)
                titleScreen.gameObject.SetActive(false);

            switch (newState)
            {
                case GameState.MainMenu:
                    ShowMainMenu();
                    break;

                case GameState.CharacterCreation:
                    StartCharacterCreation();
                    break;

                case GameState.Exploration:
                    StartExploration();
                    break;

                case GameState.Combat:
                    // Combat system handles its own flow
                    break;

                case GameState.Dialogue:
                    // Handled through chat UI
                    break;
            }
        }

        private void ShowMainMenu()
        {
            if (titleScreen != null)
            {
                titleScreen.gameObject.SetActive(true);
                titleScreen.OnNewGame     = OnNewGameSelected;
                titleScreen.OnSlotSelected = OnSlotSelected;
                titleScreen.OnSlotDelete   = OnSlotDeleted;
                titleScreen.Refresh();
            }
            else
            {
                if (ChatUI.Instance == null) return;
                ChatUI.Instance.AddSystemMessage("=== WELCOME TO D&D LLM ===");
                ChatUI.Instance.AddSystemMessage("An adventure powered by AI.");
                ChatUI.Instance.AddSystemMessage("Describe the adventure you want to embark on...");
            }
        }

        private void StartCharacterCreation()
        {
            if (characterPopup != null)
            {
                characterPopup.OnComplete  = OnCharacterCreationComplete;
                characterPopup.OnCancelled = () => ChangeState(GameState.MainMenu);
                characterPopup.Open(_currentSlotIndex);
            }
            else
            {
                if (ChatUI.Instance == null) return;
                ChatUI.Instance.AddSystemMessage("=== CHARACTER CREATION ===");
                ChatUI.Instance.AddSystemMessage("Describe your hero -- class, background, personality.");
            }
        }

        private void OnNewGameSelected()
        {
            // Pick first empty slot; fall back to slot 0 if all full
            int slot = 0;
            for (int i = 0; i < 3; i++)
            {
                var (data, _) = DNDLLM.Services.SaveSystem.Load(i);
                if (data == null) { slot = i; break; }
            }
            _currentSlotIndex = slot;

            if (titleScreen != null) titleScreen.gameObject.SetActive(false);
            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.ClearChat();
                ChatUI.Instance.AddSystemMessage("=== NEW ADVENTURE ===");
                ChatUI.Instance.AddSystemMessage("Describe the adventure you want to embark on...");
            }
            // State stays MainMenu; player types prompt → StartCampaignAsync transitions to CharacterCreation
        }

        private void OnSlotSelected(int slotIndex)
        {
            if (titleScreen != null) titleScreen.gameObject.SetActive(false);
            LoadSlot(slotIndex);
        }

        private void OnSlotDeleted(int slotIndex)
        {
            DNDLLM.Services.SaveSystem.Delete(slotIndex);
            titleScreen.Refresh();
        }

        private void OnMenuButtonPressed()
        {
            if (currentState == GameState.Exploration ||
                currentState == GameState.Combat      ||
                currentState == GameState.Dialogue)
            {
                if (inGameMenuPanel != null)
                {
                    int px = MapCharacterController.Instance?.GridX ?? 0;
                    int py = MapCharacterController.Instance?.GridY ?? 0;
                    inGameMenuPanel.Open(px, py);
                }
                else
                {
                    SaveCurrentSlot();
                    ChangeState(GameState.MainMenu);
                }
            }
            else
            {
                ChangeState(GameState.MainMenu);
            }
        }

        private void OnEditMapButtonPressed()
        {
            if (currentState == GameState.Exploration ||
                currentState == GameState.Combat      ||
                currentState == GameState.Dialogue)
            {
                inGameMenuPanel?.Close();
                editMapPanel?.Open();
            }
        }

        private void OnCharacterButtonPressed()
        {
            if (currentState == GameState.Exploration ||
                currentState == GameState.Combat      ||
                currentState == GameState.Dialogue)
            {
                characterScreenPanel?.Open(playerCharacter, _characterPortrait,
                    _appearanceDescription, _backstory);
            }
        }

        private void OnSaveFromMenu()
        {
            SaveCurrentSlot();
            ChatUI.Instance?.AddSystemMessage("Game saved.");
        }

        private void OnLoadFromMenu()
        {
            inGameMenuPanel?.Close();
            ChangeState(GameState.MainMenu);
        }

        private async void OnRegenerateTileFromMenu(int x, int y)
        {
            inGameMenuPanel?.Close();
            if (MapGenerator.Instance != null)
                await MapGenerator.Instance.RegenerateTileAsync(x, y);
        }

        private void OnSaveButtonPressed()
        {
            if (currentState == GameState.Exploration ||
                currentState == GameState.Combat      ||
                currentState == GameState.Dialogue)
            {
                SaveCurrentSlot();
                if (ChatUI.Instance != null)
                    ChatUI.Instance.AddSystemMessage("Game saved.");
            }
        }

        private void OnCharacterCreationComplete(CharacterCreationData data)
        {
            _characterPortrait       = data.portrait;
            _appearanceDescription   = data.appearanceDescription ?? "";
            _backstory               = data.backstory              ?? "";

            if (playerCharacter == null)
            {
                var go = new GameObject("Player");
                playerCharacter = go.AddComponent<DnD.Character.CharacterStats>();
                DontDestroyOnLoad(go);
            }
            playerCharacter.characterName = data.characterName;
            playerCharacter.race          = data.race;

            CharacterClass charClass = CreateBasicClass(data.characterClass,
                data.characterClass == CharacterClassName.Fighter  ? 10 :
                data.characterClass == CharacterClassName.Wizard   ?  6 :
                data.characterClass == CharacterClassName.Rogue    ?  8 : 8);

            playerCharacter.characterClass = charClass;
            playerCharacter.abilities = data.abilities ?? AbilityScores.GenerateRandom();
            playerCharacter.Initialize();

            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.AddSystemMessage($"Character created: {data.characterName}");
                ChatUI.Instance.AddSystemMessage($"Class: {data.characterClass} | Race: {data.race}");
                ChatUI.Instance.AddSystemMessage($"HP: {playerCharacter.maxHitPoints} | AC: {playerCharacter.armorClass}");
                if (!string.IsNullOrEmpty(data.backstory))
                    ChatUI.Instance.AddDMMessage(data.backstory);
            }

            SaveCurrentSlot(data.portrait);
            ChangeState(GameState.Exploration);
        }

        private void SaveCurrentSlot(UnityEngine.Texture2D portrait = null)
        {
            if (playerCharacter == null) return;
            if (string.IsNullOrEmpty(playerCharacter.characterName)) return;

            var saveData = new SaveData
            {
                characterName         = playerCharacter.characterName,
                appearanceDescription = _appearanceDescription,
                raceName              = playerCharacter.race.ToString(),
                className             = playerCharacter.characterClass != null
                                            ? playerCharacter.characterClass.className.ToString()
                                            : "",
                level                 = playerCharacter.level,
                maxHP                 = playerCharacter.maxHitPoints,
                currentHP             = playerCharacter.currentHitPoints,
                armorClass            = playerCharacter.armorClass,
                str                   = playerCharacter.abilities.GetScore(AbilityScore.Strength),
                dex                   = playerCharacter.abilities.GetScore(AbilityScore.Dexterity),
                con                   = playerCharacter.abilities.GetScore(AbilityScore.Constitution),
                intel                 = playerCharacter.abilities.GetScore(AbilityScore.Intelligence),
                wis                   = playerCharacter.abilities.GetScore(AbilityScore.Wisdom),
                cha                   = playerCharacter.abilities.GetScore(AbilityScore.Charisma),
                campaignSeed          = _campaignSeed,
                campaignTimeline      = currentCampaign?.timelineText ?? "",
                gameState             = currentState.ToString(),
                messages              = ChatUI.Instance != null
                                            ? ChatUI.Instance.GetMessageHistory()
                                            : new System.Collections.Generic.List<ChatMessageData>(),
                audioAutoplay         = DNDLLM.Services.TTSService.Instance != null
                                            && DNDLLM.Services.TTSService.Instance.AutoPlay,
            };
            saveData.slotLabel  = $"{saveData.characterName} · {saveData.className} · Lv{saveData.level}";
            saveData.backstory  = _backstory;

            // Persist tile descriptions and full per-tile grid state (captures EditMapPanel changes)
            if (MapGenerator.Instance?.grid != null)
            {
                var gen = MapGenerator.Instance;
                saveData.tileDescriptions = gen.GetTileDescriptions();
                saveData.tileGrid = new List<DnD.Data.TileGridEntry>();
                for (int gx = 0; gx < gen.width; gx++)
                for (int gy = 0; gy < gen.height; gy++)
                    saveData.tileGrid.Add(new DnD.Data.TileGridEntry
                    {
                        x           = gx,
                        y           = gy,
                        tileType    = gen.grid[gx, gy].type.ToString(),
                        description = gen.grid[gx, gy].description,
                    });
            }

            // Use the caller-supplied portrait, else the stored one (may be the generated map token)
            UnityEngine.Texture2D savePortrait = portrait ?? _characterPortrait;
            DNDLLM.Services.SaveSystem.Save(_currentSlotIndex, saveData, savePortrait);
            Debug.Log($"[GameManager] Saved slot {_currentSlotIndex}: {saveData.slotLabel}");
        }

        private void LoadSlot(int slotIndex)
        {
            var (data, portrait) = DNDLLM.Services.SaveSystem.Load(slotIndex);
            if (data == null)
            {
                Debug.LogWarning($"[GameManager] Slot {slotIndex} is empty.");
                ChangeState(GameState.MainMenu);
                return;
            }

            // Clear any entities and world graph from a previous session
            MapEntityController.ClearAll();
            _mapGraph.Reset();

            _currentSlotIndex        = slotIndex;
            _campaignSeed            = data.campaignSeed ?? "";
            _appearanceDescription   = data.appearanceDescription ?? "";
            _backstory               = data.backstory ?? "";
            _characterPortrait       = portrait;

            // Queue tile descriptions and per-tile grid to be applied once the map finishes generating
            _pendingTileDescriptions = (data.tileDescriptions != null && data.tileDescriptions.Count > 0)
                ? data.tileDescriptions : null;
            _pendingTileGrid = (data.tileGrid != null && data.tileGrid.Count > 0)
                ? data.tileGrid : null;

            if (DNDLLM.Services.TTSService.Instance != null)
                DNDLLM.Services.TTSService.Instance.AutoPlay = data.audioAutoplay;

            if (playerCharacter == null)
            {
                var go = new GameObject("Player");
                playerCharacter = go.AddComponent<DnD.Character.CharacterStats>();
                DontDestroyOnLoad(go);
            }
            playerCharacter.characterName    = data.characterName;
            playerCharacter.level            = data.level;
            playerCharacter.maxHitPoints     = data.maxHP;
            playerCharacter.currentHitPoints = data.currentHP;
            playerCharacter.armorClass       = data.armorClass;
            playerCharacter.abilities = new AbilityScores(
                data.str, data.dex, data.con, data.intel, data.wis, data.cha);

            if (!string.IsNullOrEmpty(data.className) &&
                System.Enum.TryParse<CharacterClassName>(data.className, out var parsedClass))
            {
                int hitDie = parsedClass == CharacterClassName.Fighter ? 10
                           : parsedClass == CharacterClassName.Wizard  ?  6
                           : parsedClass == CharacterClassName.Rogue   ?  8 : 8;
                playerCharacter.characterClass = CreateBasicClass(parsedClass, hitDie);
            }

            if (System.Enum.TryParse<Race>(data.raceName, out var parsedRace))
                playerCharacter.race = parsedRace;

            if (!string.IsNullOrEmpty(data.campaignTimeline))
                currentCampaign = new StoryTimeline
                {
                    campaignPrompt = data.campaignSeed ?? "",
                    timelineText   = data.campaignTimeline
                };

            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.ClearChat();
                if (data.messages != null)
                {
                    foreach (var msg in data.messages)
                    {
                        switch (msg.type)
                        {
                            case "Player": ChatUI.Instance.AddPlayerMessage(msg.text); break;
                            case "DM":     ChatUI.Instance.AddDMMessage(msg.text);     break;
                            default:       ChatUI.Instance.AddSystemMessage(msg.text); break;
                        }
                    }
                }
                ChatUI.Instance.AddSystemMessage("--- Adventure resumed ---");
            }

            // Always go to Exploration on load; flag so StartExploration skips the "begins" banner
            _isResumingLoad = true;
            ChangeState(GameState.Exploration);
        }

        private bool _isResumingLoad = false;

        private const string DM_SYSTEM_PROMPT =
            "You are an expert Dungeon Master for a D&D 5e adventure. " +
            "Write vivid, immersive descriptions in 2-4 sentences. " +
            "Always end your narrative with exactly 3 suggested actions for the player, " +
            "each on its own line prefixed with '► '. Keep suggestions short (5-8 words each).\n\n" +
            "You have access to game tools. After the ► suggestions you MAY append a " +
            "[GM_ACTIONS] block to drive game state. Only include actions the narrative warrants. " +
            "Omit the block entirely if nothing should happen mechanically.\n\n" +
            "Available tools (one command per line inside the block):\n" +
            "  MOVE player <north|south|east|west>\n" +
            "  DAMAGE player <amount>\n" +
            "  HEAL player <amount>\n" +
            "  ADD_CONDITION player <poisoned|blinded|stunned|frightened>\n" +
            "  REMOVE_CONDITION player <condition>\n" +
            "  SPAWN_ENEMY <name> <hp> <ac>\n" +
            "  AWARD_XP <amount>\n" +
            "  KILL_ENTITY <name>             (removes an enemy/NPC from the map)\n" +
            "  LOCK_DOOR <x> <y>              (bars a passage — player cannot pass)\n" +
            "  UNLOCK_DOOR <x> <y>            (opens a previously locked passage)\n" +
            "  ENTER_SUBREGION <description>  (transport player into a named sub-area, e.g. 'dark armory' or 'flooded cellar')\n\n" +
            "Example response ending:\n" +
            "► Search the body\n" +
            "► Retreat north\n" +
            "► Call for help\n" +
            "[GM_ACTIONS]\n" +
            "DAMAGE player 4\n" +
            "AWARD_XP 25\n" +
            "[/GM_ACTIONS]";


        private void StartExploration()
        {
            if (!_isResumingLoad && ChatUI.Instance != null)
                ChatUI.Instance.AddSystemMessage("=== YOUR ADVENTURE BEGINS ===");
            _isResumingLoad = false;

            // Reset world graph for this campaign
            string rootTheme = _campaignSeed.Length > 0 ? _campaignSeed.Split(',')[0].Trim() : "dungeon";
            _mapGraph.InitRoot(rootTheme);

            if (MapGenerator.Instance != null)
            {
                MapGenerator.Instance.OnMapReady -= OnMapReadyNarrate;
                MapGenerator.Instance.OnMapReady += OnMapReadyNarrate;
                // Skip LLM description generation when we have saved descriptions to restore
                MapGenerator.Instance.SkipDescriptionGeneration = _pendingTileDescriptions != null;
                MapGenerator.Instance.GenerateMap(
                    _campaignSeed.Length > 0 ? _campaignSeed : "dungeon");
            }
        }

        private async void OnMapReadyNarrate()
        {
            MapGenerator.Instance.OnMapReady -= OnMapReadyNarrate;

            var gen = MapGenerator.Instance;

            // Restore saved tile descriptions (skips the LLM description call on reload)
            if (_pendingTileDescriptions != null)
            {
                gen.LoadTileDescriptions(_pendingTileDescriptions);
                _pendingTileDescriptions = null;
            }

            // Restore per-tile type+description overrides saved by EditMapPanel
            if (_pendingTileGrid != null && gen.grid != null)
            {
                foreach (var entry in _pendingTileGrid)
                {
                    if (entry.x < 0 || entry.x >= gen.width || entry.y < 0 || entry.y >= gen.height) continue;
                    if (System.Enum.TryParse<TileType>(entry.tileType, out TileType t))
                    {
                        gen.grid[entry.x, entry.y].type        = t;
                        gen.grid[entry.x, entry.y].walkable    = t == TileType.Floor || t == TileType.Exit
                                                               || t == TileType.Door  || t == TileType.NpcSpawn;
                    }
                    if (!string.IsNullOrEmpty(entry.description))
                        gen.grid[entry.x, entry.y].description = entry.description;
                }
                _pendingTileGrid = null;
            }

            if (LLMService.Instance == null) return;

            // ── 1. DM opening narration ───────────────────────────────────
            if (ChatUI.Instance != null)
            {
                string seed = string.IsNullOrEmpty(_campaignSeed) ? "a mysterious dungeon" : _campaignSeed;

                string tileCtx = "";
                if (MapGenerator.Instance != null)
                    tileCtx = "\n\nStarting area:\n" + MapGenerator.Instance.GetTileContext(
                        MapGenerator.Instance.width / 2, 1);

                string userPrompt =
                    $"The adventurer enters: {seed}. " +
                    "Describe what they see and sense as they arrive, then suggest 3 possible first actions." +
                    tileCtx;

                string rawNarration = await LLMService.Instance.SendPrompt(DM_SYSTEM_PROMPT, userPrompt);
                if (!string.IsNullOrEmpty(rawNarration))
                {
                    string narrative = GMToolExecutor.ExtractNarrative(rawNarration);
                    if (!string.IsNullOrEmpty(narrative))
                        ChatUI.Instance.AddDMMessage(narrative, useTypewriter: true);

                    var actionResults = GMToolExecutor.ExecuteActions(rawNarration, playerCharacter);
                    foreach (string r in actionResults)
                        ChatUI.Instance.AddSystemMessage(r);
                }
            }

            // ── 2. Generate and place character token on the map ─────────
            await SpawnCharacterOnMapAsync();

            // ── 3. Spawn enemy / NPC tokens ───────────────────────────────
            await SpawnMapEntitiesAsync();

            // ── 4. Save root snapshot into map graph ─────────────────────
            var cc = MapCharacterController.Instance;
            var rootSnap = MapGenerator.Instance?.TakeSnapshot(cc?.GridX ?? 0, cc?.GridY ?? 0);
            if (rootSnap != null) _mapGraph.SaveCurrentSnapshot(rootSnap);

            // ── 5. Auto-save ──────────────────────────────────────────────
            SaveCurrentSlot();
            if (ChatUI.Instance != null)
                ChatUI.Instance.AddSystemMessage("Adventure saved.");
        }

        private async Task SpawnCharacterOnMapAsync()
        {
            if (MapGenerator.Instance == null) return;

            Texture2D charTex = null;

            // Generate a top-down character token styled to match the map's tiles
            if (LLMService.Instance != null && MapGenerator.Instance.StyleAnchor != null
                && MapGenerator.Instance.StyleAnchor != Texture2D.whiteTexture)
            {
                string race       = playerCharacter != null ? playerCharacter.race.ToString() : "Human";
                string cls        = playerCharacter?.characterClass != null
                                    ? playerCharacter.characterClass.className.ToString() : "Adventurer";
                string appearance = string.IsNullOrEmpty(_appearanceDescription)
                                    ? "" : $" {_appearanceDescription}.";

                string charPrompt =
                    $"Square, 1:1 aspect ratio. Top-down RPG map token: {race} {cls} adventurer, " +
                    $"viewed directly from above.{appearance} " +
                    "Small heroic figure on a FULLY TRANSPARENT background — no floor, no tile, no ground. " +
                    "The character and equipment are the only visible elements. " +
                    "Match the exact art style, color palette, and brushwork of the reference tile. " +
                    "Flat overhead view, no border, no drop shadow.";

                if (DnD.UI.ChatUI.Instance != null)
                    DnD.UI.ChatUI.Instance.AddSystemMessage("Generating character sprite...");

                charTex = await LLMService.Instance.GenerateStyledTile(
                    charPrompt, MapGenerator.Instance.StyleAnchor);
                if (charTex != null)
                    charTex = SpriteBackgroundRemover.RemoveBackground(charTex);
                Debug.Log($"[GameManager] Character tile generated: {(charTex != null ? "OK" : "failed")}");
            }

            // Fall back to the character portrait if generation failed
            if (charTex == null) charTex = _characterPortrait;

            // Update _characterPortrait so the next save persists the generated token
            if (charTex != null) _characterPortrait = charTex;

            // Starting cell: just inside the south entrance (bottom door)
            int startX = MapGenerator.Instance.width  / 2;
            int startY = 1; // row above the bottom wall

            // Create the controller if it doesn't exist yet
            if (MapCharacterController.Instance == null)
            {
                var go = new GameObject("MapCharacter");
                go.AddComponent<SpriteRenderer>();         // must be added BEFORE MapCharacterController
                go.AddComponent<MapCharacterController>();
            }

            MapCharacterController.Instance?.Initialize(charTex, startX, startY);
        }

        // ── Sub-map traversal (MapGraph-backed) ──────────────────────────────

        /// <summary>Player stepped onto a Door tile — enter or revisit the room behind it.</summary>
        private async Task TryEnterRoom()
        {
            var cc  = MapCharacterController.Instance;
            var gen = MapGenerator.Instance;
            if (cc == null || gen == null) return;

            // Build a rich theme that tells the LLM what environment surrounds this door
            string parentCtx = gen.GetTileContext(cc.GridX, cc.GridY);
            string subTheme  = $"interior room inside {(_campaignSeed.Length > 0 ? _campaignSeed : "dungeon")}";

            await EnterSubregionViaKey(
                $"door_{cc.GridX}_{cc.GridY}",
                subTheme,
                cc.GridX, cc.GridY,
                "You step through the doorway...",
                parentCtx);
        }

        /// <summary>Called by GMToolExecutor ENTER_SUBREGION command.</summary>
        public void RequestSubregionEntry(string description)
        {
            var cc  = MapCharacterController.Instance;
            var gen = MapGenerator.Instance;
            string parentCtx = (cc != null && gen != null)
                ? gen.GetTileContext(cc.GridX, cc.GridY) : "";
            _ = EnterSubregionViaKey(
                $"region_{description.ToLower().Trim().Replace(" ", "_")}",
                description,
                cc?.GridX ?? 0,
                cc?.GridY ?? 0,
                $"Entering: {description}...",
                parentCtx);
        }

        private async Task EnterSubregionViaKey(string graphKey, string childTheme,
                                                 int returnX, int returnY,
                                                 string announceMsg,
                                                 string parentContext = "")
        {
            var gen = MapGenerator.Instance;
            var cc  = MapCharacterController.Instance;
            if (gen == null) return;

            // Snapshot the current map into the graph node before leaving
            var currentSnap = gen.TakeSnapshot(cc?.GridX ?? returnX, cc?.GridY ?? returnY);
            _mapGraph.SaveCurrentSnapshot(currentSnap);

            // Resolve child node — door keys use coordinates, region keys use name
            string childId;
            bool   isNew;
            if (graphKey.StartsWith("region_"))
                (childId, isNew) = _mapGraph.GetOrCreateRegionChild(
                    graphKey.Substring("region_".Length), childTheme);
            else
                (childId, isNew) = _mapGraph.GetOrCreateDoorChild(returnX, returnY, childTheme);

            MapEntityController.ClearAll();
            ChatUI.Instance?.AddSystemMessage(announceMsg);

            _mapGraph.NavigateTo(childId);
            var childNode = _mapGraph.GetNode(childId);

            if (!isNew && childNode?.Snapshot != null)
            {
                // ── Revisit: restore without any LLM calls ──────────────────
                gen.OnMapReady -= OnMapReadyNarrate;
                gen.OnMapReady -= OnSubMapReady;
                gen.RestoreFromSnapshot(childNode.Snapshot);
                int cx = gen.width / 2, cy = gen.height / 2;
                cc?.MoveTo(cx, cy);
                ChatUI.Instance?.AddSystemMessage("You recognise this place from before.");
                return;
            }

            // ── New room: give LLM context about the parent area, then generate ──
            if (!string.IsNullOrEmpty(parentContext))
                gen.StartingContext = parentContext;

            gen.OnMapReady -= OnMapReadyNarrate;
            gen.OnMapReady -= OnSubMapReady;
            gen.OnMapReady += OnSubMapReady;
            gen.SkipDescriptionGeneration = false;
            gen.GenerateMap(childTheme);
        }

        private async void OnSubMapReady()
        {
            MapGenerator.Instance.OnMapReady -= OnSubMapReady;

            int cx = MapGenerator.Instance.width  / 2;
            int cy = MapGenerator.Instance.height / 2;
            MapCharacterController.Instance?.MoveTo(cx, cy);

            if (LLMService.Instance != null && ChatUI.Instance != null)
            {
                string tileCtx = MapGenerator.Instance.GetTileContext(cx, cy);
                string raw = await LLMService.Instance.SendPrompt(DM_SYSTEM_PROMPT,
                    $"The adventurer enters: {MapGenerator.Instance.LastTheme}.\n\n{tileCtx}\n\n"
                    + "Describe what they find, then suggest 3 actions. "
                    + "The Exit tile leads back out.");
                if (!string.IsNullOrEmpty(raw))
                {
                    string narrative = GMToolExecutor.ExtractNarrative(raw);
                    if (!string.IsNullOrEmpty(narrative))
                        ChatUI.Instance.AddDMMessage(narrative, useTypewriter: true);
                    foreach (string r in GMToolExecutor.ExecuteActions(raw, playerCharacter))
                        ChatUI.Instance.AddSystemMessage(r);
                }
            }

            await SpawnMapEntitiesAsync();

            // Save freshly generated sub-map into graph node for future revisits
            var cc   = MapCharacterController.Instance;
            var snap = MapGenerator.Instance.TakeSnapshot(cc?.GridX ?? cx, cc?.GridY ?? cy);
            _mapGraph.SaveCurrentSnapshot(snap);
        }

        private void TryExitRoom()
        {
            if (!_mapGraph.CanGoBack)
            {
                ChatUI.Instance?.AddSystemMessage("There is no way back from here.");
                return;
            }

            // Save current child map state (with player position) before leaving,
            // so re-entering the same door later restores exactly where we were.
            var cc = MapCharacterController.Instance;
            var gen = MapGenerator.Instance;
            if (gen != null)
            {
                var childSnap = gen.TakeSnapshot(cc?.GridX ?? 0, cc?.GridY ?? 0);
                _mapGraph.SaveCurrentSnapshot(childSnap);
            }

            // Restore parent snapshot from the graph
            _mapGraph.NavigateBack();
            var parentNode = _mapGraph.CurrentNode;
            if (parentNode?.Snapshot == null)
            {
                ChatUI.Instance?.AddSystemMessage("The way back has been lost.");
                return;
            }

            MapEntityController.ClearAll();
            MapGenerator.Instance.OnMapReady -= OnMapReadyNarrate;
            MapGenerator.Instance.OnMapReady -= OnSubMapReady;
            MapGenerator.Instance.RestoreFromSnapshot(parentNode.Snapshot);
            MapCharacterController.Instance?.MoveTo(parentNode.Snapshot.playerX, parentNode.Snapshot.playerY);
            ChatUI.Instance?.AddSystemMessage("You step back out.");
        }

        // ── Entity sprite spawning ────────────────────────────────────────────

        private async Task SpawnMapEntitiesAsync()
        {
            var gen = MapGenerator.Instance;
            if (gen == null || gen.grid == null || LLMService.Instance == null) return;

            // Collect all tiles that should get an entity token
            var spawnList = new List<(int x, int y, TileType type)>();
            for (int x = 0; x < gen.width; x++)
            for (int y = 0; y < gen.height; y++)
            {
                var t = gen.grid[x, y].type;
                if (t == TileType.EnemySpawn || t == TileType.NpcSpawn
                    || t == TileType.House   || t == TileType.Inn
                    || t == TileType.Market  || t == TileType.Fountain)
                    spawnList.Add((x, y, t));
            }

            if (spawnList.Count == 0) return;

            // Fire all entity generation tasks in parallel
            var entityTasks = new List<Task>();
            foreach (var (ex, ey, etype) in spawnList)
            {
                int cx = ex, cy = ey; // capture for lambda
                TileType ctype = etype;
                entityTasks.Add(SpawnSingleEntityAsync(gen, cx, cy, ctype));
            }
            await Task.WhenAll(entityTasks);
        }

        private async Task SpawnSingleEntityAsync(MapGenerator gen, int x, int y, TileType tileType)
        {
            bool isEnemy = tileType == TileType.EnemySpawn;

            // Determine role & fallback name based on tile type
            string role, fallbackName;
            switch (tileType)
            {
                case TileType.EnemySpawn: role = "hostile monster";                      fallbackName = "Monster";   break;
                case TileType.Inn:        role = "innkeeper or travelling guest";         fallbackName = "Innkeeper"; break;
                case TileType.Market:     role = "merchant or market vendor";             fallbackName = "Merchant";  break;
                case TileType.Fountain:   role = "street NPC near a fountain";           fallbackName = "Townsfolk"; break;
                case TileType.House:      role = "resident or villager near their home"; fallbackName = "Villager";  break;
                default:                  role = "friendly NPC";                          fallbackName = "Villager";  break;
            }

            // Ask LLM for a 2-4 word name
            string nameRaw = await LLMService.Instance.SendPrompt(
                "You are a D&D world builder. Reply with ONLY a 2-4 word creature or character name, no punctuation.",
                $"In a {gen.LastTheme} setting, name one {role} that fits the environment.");
            string entityName = string.IsNullOrEmpty(nameRaw) ? fallbackName : nameRaw.Trim();

            if (ChatUI.Instance != null)
                ChatUI.Instance.AddSystemMessage($"Generating sprite for {entityName}...");

            string kind = isEnemy ? "fearsome monster" : "NPC character";
            string prompt =
                $"Square, 1:1 aspect ratio. Top-down RPG map token: {entityName}, {kind} "
                + $"in a {gen.LastTheme} setting, viewed directly from above. "
                + "Centered on a FULLY TRANSPARENT background — no floor, no tile, no ground. "
                + "The entity is the only visible element. "
                + "Match the exact art style, color palette of the reference tile. "
                + "Flat overhead view, no border, no drop shadow.";

            Texture2D tex = await LLMService.Instance.GenerateStyledTile(prompt, gen.StyleAnchor);
            if (tex != null)
                tex = SpriteBackgroundRemover.RemoveBackground(tex);

            var go = new GameObject($"Entity_{entityName}_{x}_{y}");
            go.AddComponent<SpriteRenderer>();
            var entity = go.AddComponent<MapEntityController>();
            entity.Initialize(
                tex, entityName, x, y,
                hp: isEnemy ? UnityEngine.Random.Range(8, 20) : 10,
                ac: isEnemy ? UnityEngine.Random.Range(10, 14) : 10,
                isEnemy: isEnemy);
        }

        private async void HandlePlayerInput(string input)
        {
            Debug.Log($"[GameManager] Player input: {input}");

            string lowerInput = input.ToLower().Trim();

            // Handle state-specific commands
            switch (currentState)
            {
                case GameState.MainMenu:
                    await StartCampaignAsync(input);
                    break;

                case GameState.CharacterCreation:
                    await HandleCharacterCreationInput(input);
                    break;

                case GameState.Exploration:
                    await HandleExplorationInput(input);
                    break;

                case GameState.Combat:
                    await HandleCombatInput(input);
                    break;

                case GameState.Dialogue:
                    await HandleDialogueInput(input);
                    break;
            }
        }

        private async Task StartCampaignAsync(string campaignPrompt)
        {
            _campaignSeed = campaignPrompt;
            if (ChatUI.Instance != null)
                ChatUI.Instance.AddSystemMessage("Creating your campaign...");

            currentCampaign = await dungeonMaster.GenerateCampaignAsync(campaignPrompt, 1);

            if (currentCampaign != null && ChatUI.Instance != null)
                ChatUI.Instance.AddDMMessage(currentCampaign.timelineText, useTypewriter: true);
            else
            {
                // LLM failed — create a minimal campaign so the game can still proceed
                currentCampaign = new StoryTimeline
                {
                    campaignPrompt = campaignPrompt,
                    timelineText   = "The Dungeon Master considers your actions..."
                };
                if (ChatUI.Instance != null)
                    ChatUI.Instance.AddDMMessage(currentCampaign.timelineText);
            }

            ChangeState(GameState.CharacterCreation);
        }

        private async Task HandleCharacterCreationInput(string input)
        {
            // Use LLM to determine character class
            string classPrompt = $"Based on this character description, recommend a D&D class (Fighter, Wizard, Rogue, or Cleric): '{input}'";

            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.AddSystemMessage("Creating your character...");
            }

            string response = await dungeonMaster.NarrateActionAsync(input, "Character creation");

            // Simple keyword matching for class selection (in production, parse LLM response)
            CharacterClass selectedClass = DetermineClass(input);

            if (playerCharacter != null && selectedClass != null)
            {
                playerCharacter.characterClass = selectedClass;
                playerCharacter.abilities = AbilityScores.GenerateRandom();
                playerCharacter.Initialize();

                if (ChatUI.Instance != null)
                {
                    ChatUI.Instance.AddSystemMessage($"Character Created!");
                    ChatUI.Instance.AddSystemMessage($"Class: {selectedClass.className}");
                    ChatUI.Instance.AddSystemMessage($"HP: {playerCharacter.maxHitPoints}");
                    ChatUI.Instance.AddSystemMessage($"AC: {playerCharacter.armorClass}");
                }

                ChangeState(GameState.Exploration);
            }
        }

        private async Task HandleExplorationInput(string input)
        {
            // ── Movement ─────────────────────────────────────────────────
            if (TryParseMovement(input, out int dx, out int dy))
            {
                bool moved = MapCharacterController.Instance?.TryMove(dx, dy) ?? false;
                if (!moved && ChatUI.Instance != null)
                    ChatUI.Instance.AddSystemMessage("Something blocks your path.");

                // Check if the player stepped onto a Door or Exit tile
                if (moved)
                {
                    var cc  = MapCharacterController.Instance;
                    var gen = MapGenerator.Instance;
                    if (cc != null && gen?.grid != null)
                    {
                        var tileType = gen.grid[cc.GridX, cc.GridY].type;
                        if (tileType == TileType.Door)
                        {
                            await TryEnterRoom();
                            return; // new map generates; stop processing this input
                        }
                        else if (tileType == TileType.Exit)
                        {
                            TryExitRoom();
                            return;
                        }
                    }
                }
            }

            // ── DM narration ──────────────────────────────────────────────
            IGameCommand command = await commandParser.ParseCommandAsync(input, playerCharacter);

            if (LLMService.Instance != null && ChatUI.Instance != null)
            {
                string pos = MapCharacterController.Instance != null
                    ? $" (at grid {MapCharacterController.Instance.GridX},{MapCharacterController.Instance.GridY})"
                    : "";
                string tileCtx = "";
                if (MapGenerator.Instance != null && MapCharacterController.Instance != null)
                    tileCtx = "\n\nEnvironment:\n" + MapGenerator.Instance.GetTileContext(
                        MapCharacterController.Instance.GridX,
                        MapCharacterController.Instance.GridY);
                string userPrompt =
                    $"Campaign: {_campaignSeed}\nPlayer{pos} does: {input}{tileCtx}\n" +
                    "Describe what happens, then suggest 3 possible next actions.";

                string rawResponse = await LLMService.Instance.SendPrompt(DM_SYSTEM_PROMPT, userPrompt);
                if (!string.IsNullOrEmpty(rawResponse))
                {
                    string narrative = GMToolExecutor.ExtractNarrative(rawResponse);
                    if (!string.IsNullOrEmpty(narrative))
                        ChatUI.Instance.AddDMMessage(narrative, useTypewriter: true);

                    var actionResults = GMToolExecutor.ExecuteActions(rawResponse, playerCharacter);
                    foreach (string r in actionResults)
                        ChatUI.Instance.AddSystemMessage(r);
                }
            }

            if (command != null && command.CanExecute())
                command.Execute();

            if (input.ToLower().Contains("attack") || input.ToLower().Contains("fight"))
                await StartRandomEncounter();
        }

        /// <summary>
        /// Parses a player input string for cardinal/intercardinal movement intent.
        /// Returns true and sets dx/dy if a movement direction was found.
        /// </summary>
        private static bool TryParseMovement(string input, out int dx, out int dy)
        {
            dx = dy = 0;
            string s = input.ToLower().Trim();

            // Pure direction words / abbreviations
            if (s == "n" || s == "north")                    { dy =  1;             return true; }
            if (s == "s" || s == "south")                    { dy = -1;             return true; }
            if (s == "e" || s == "east")                     { dx =  1;             return true; }
            if (s == "w" || s == "west")                     { dx = -1;             return true; }
            if (s == "ne" || s == "northeast")               { dx =  1; dy =  1;    return true; }
            if (s == "nw" || s == "northwest")               { dx = -1; dy =  1;    return true; }
            if (s == "se" || s == "southeast")               { dx =  1; dy = -1;    return true; }
            if (s == "sw" || s == "southwest")               { dx = -1; dy = -1;    return true; }

            // Phrase patterns: "move north", "go west", "walk northeast", etc.
            bool isPhrase = s.StartsWith("move ")  || s.StartsWith("go ")    ||
                            s.StartsWith("walk ")  || s.StartsWith("run ")   ||
                            s.StartsWith("head ")  || s.StartsWith("travel ");
            if (!isPhrase) return false;

            if (s.Contains("north")) dy += 1;
            if (s.Contains("south")) dy -= 1;
            if (s.Contains("east"))  dx += 1;
            if (s.Contains("west"))  dx -= 1;

            return dx != 0 || dy != 0;
        }

        private async Task HandleCombatInput(string input)
        {
            if (CombatManager.Instance != null && CombatManager.Instance.IsPlayerTurn())
            {
                IGameCommand command = await commandParser.ParseCommandAsync(input, playerCharacter);

                if (command != null && command.CanExecute())
                {
                    command.Execute();
                }
            }
        }

        private async Task HandleDialogueInput(string input)
        {
            string response = await dungeonMaster.NarrateActionAsync(input, "Dialogue with NPC");
            // Response automatically displayed via event
        }

        private async Task StartRandomEncounter()
        {
            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.AddSystemMessage("=== COMBAT BEGINS ===");
            }

            // Generate enemy
            CreatureData enemyData = await dungeonMaster.GenerateCreatureAsync("goblin", 1);

            // Create enemy character
            GameObject enemyObj = new GameObject(enemyData.name);
            CharacterStats enemy = enemyObj.AddComponent<CharacterStats>();
            enemy.characterName = enemyData.name;
            enemy.maxHitPoints = enemyData.hitPoints;
            enemy.currentHitPoints = enemyData.hitPoints;
            enemy.armorClass = enemyData.armorClass;

            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.AddDMMessage($"A {enemyData.name} appears!");
                ChatUI.Instance.AddSystemMessage($"HP: {enemyData.hitPoints} | AC: {enemyData.armorClass}");
            }

            // Start combat
            if (CombatManager.Instance != null)
            {
                List<CharacterStats> players = new List<CharacterStats> { playerCharacter };
                List<CharacterStats> enemies = new List<CharacterStats> { enemy };
                CombatManager.Instance.StartCombat(players, enemies);
                ChangeState(GameState.Combat);
            }
        }

        private CharacterClass DetermineClass(string description)
        {
            // Load a default class - in production, load from Resources
            // For now, return a basic configuration
            string lower = description.ToLower();

            if (lower.Contains("fighter") || lower.Contains("warrior") || lower.Contains("strong"))
            {
                return CreateBasicClass(CharacterClassName.Fighter, 10);
            }
            else if (lower.Contains("wizard") || lower.Contains("mage") || lower.Contains("magic"))
            {
                return CreateBasicClass(CharacterClassName.Wizard, 6);
            }
            else if (lower.Contains("rogue") || lower.Contains("thief") || lower.Contains("stealth"))
            {
                return CreateBasicClass(CharacterClassName.Rogue, 8);
            }
            else if (lower.Contains("cleric") || lower.Contains("priest") || lower.Contains("heal"))
            {
                return CreateBasicClass(CharacterClassName.Cleric, 8);
            }
            else
            {
                // Default to Fighter
                return CreateBasicClass(CharacterClassName.Fighter, 10);
            }
        }

        private CharacterClass CreateBasicClass(CharacterClassName className, int hitDie)
        {
            CharacterClass charClass = ScriptableObject.CreateInstance<CharacterClass>();
            charClass.className = className;
            charClass.hitDieSize = hitDie;
            charClass.savingThrowProficiencies = new AbilityScore[] { AbilityScore.Strength, AbilityScore.Constitution };
            return charClass;
        }

        private void OnDMResponse(string response)
        {
            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.AddDMMessage(response);
            }
        }

        private void OnCombatMessage(string message)
        {
            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.AddSystemMessage(message);
            }
        }

        public CharacterStats GetPlayerCharacter()
        {
            return playerCharacter;
        }

        public GameState GetCurrentState()
        {
            return currentState;
        }
    }
}
