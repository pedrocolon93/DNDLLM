using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        // playerCharacter is the *active* turn owner; kept as a [SerializeField] for the
        // ~50 existing call sites. CurrentPlayer (below) is the multi-player-aware accessor.
        [SerializeField] private CharacterStats playerCharacter;
        [SerializeField] private List<CharacterStats> playerCharacters = new List<CharacterStats>();
        [SerializeField] private List<CharacterStats> partyMembers     = new List<CharacterStats>();
        [SerializeField] private int currentPlayerIndex = 0;

        /// <summary>The party member whose turn it is right now. Mirrors playerCharacter.</summary>
        public CharacterStats CurrentPlayer => (playerCharacters != null && currentPlayerIndex >= 0 && currentPlayerIndex < playerCharacters.Count)
            ? playerCharacters[currentPlayerIndex] : playerCharacter;

        /// <summary>Read-only view of the party. Single-player saves yield a 1-element list.</summary>
        public IReadOnlyList<CharacterStats> Party => playerCharacters;

        /// <summary>Drives the turn-order HUD strip and (eventually) gates exploration input. Combat still
        /// uses CombatManager's own initiative for now; the queue is rebuilt at the encounter boundary.</summary>
        public readonly DnD.Core.TurnQueue Turns = new DnD.Core.TurnQueue();

        [Header("AI Configuration")]
        [SerializeField] private bool useMockLLM = true; // Set to false when using real LLM

        [Header("UI — set by UISceneBuilder")]
        [SerializeField] private DnD.UI.TitleScreen             titleScreen;
        [SerializeField] private DnD.UI.AdventurePromptPopup    adventurePromptPopup;
        [SerializeField] private DnD.UI.CharacterCreationPopup  characterPopup;
        [SerializeField] private UnityEngine.UI.Button          menuButton;
        [SerializeField] private DnD.UI.InGameMenuPanel         inGameMenuPanel;
        [SerializeField] private UnityEngine.UI.Button          editMapButton;
        [SerializeField] private DnD.UI.EditMapPanel            editMapPanel;
        [SerializeField] private UnityEngine.UI.Button          characterButton;
        [SerializeField] private DnD.UI.CharacterScreenPanel    characterScreenPanel;
        [SerializeField] private TMPro.TextMeshProUGUI          turnStripText;

        private int _currentSlotIndex = 0;
        private string _campaignSeed = "";
        private string _appearanceDescription = "";
        private string _backstory = "";
        private Texture2D _characterPortrait;
        private Texture2D _characterMapToken; // top-down sprite shown on the map; persisted per save slot
        private List<DnD.Data.TileDescriptionEntry> _pendingTileDescriptions;
        private List<DnD.Data.TileGridEntry>        _pendingTileGrid;
        // Save-state queued by LoadSlot, consumed during StartExploration / OnMapReadyNarrate.
        // When _pendingMapBackground is non-null, the LLM holistic-paint pipeline is skipped.
        private Texture2D                           _pendingMapBackground;
        private int                                 _pendingPlayerX, _pendingPlayerY;
        private List<DnD.Data.EntityEntry>          _pendingEntities;
        private List<Texture2D>                     _pendingEntitySprites;
        private readonly MapGraph _mapGraph = new MapGraph();

        private DungeonMaster dungeonMaster;
        private CommandParser commandParser;
        private ILLMProvider llmProvider;
        private StoryTimeline currentCampaign;
        // Structured plan emitted by DungeonMaster.GenerateCampaignPlanAsync — drives map size + features.
        private CampaignPlan  _currentPlan;
        private CampaignSize  _currentSize = CampaignSize.Medium;

        public CampaignPlan CurrentPlan => _currentPlan;

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

            // UI buttons need an EventSystem to receive clicks. The scene was
            // built procedurally without one, so create it here if missing.
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                DontDestroyOnLoad(es);
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
                    inGameMenuPanel.OnTTSEnabledChanged = v =>
                    {
                        if (DNDLLM.Services.TTSService.Instance != null)
                            DNDLLM.Services.TTSService.Instance.Enabled = v;
                    };
                    inGameMenuPanel.OnTTSAutoPlayChanged = v =>
                    {
                        if (DNDLLM.Services.TTSService.Instance != null)
                            DNDLLM.Services.TTSService.Instance.AutoPlay = v;
                        SaveCurrentSlot();  // persist immediately
                    };
                }

                if (editMapButton != null)
                    editMapButton.onClick.AddListener(OnEditMapButtonPressed);

                if (editMapPanel != null)
                    editMapPanel.OnSaveRequested = () => { SaveCurrentSlot(); ChatUI.Instance?.AddSystemMessage("Map changes saved."); };

                if (characterButton != null)
                    characterButton.onClick.AddListener(OnCharacterButtonPressed);

                // Drive the turn-order HUD strip off the queue so it stays in sync without polling.
                Turns.OnTurnChanged += RefreshTurnStrip;
                RefreshTurnStrip();

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

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            // First Escape while an input field has focus = blur the field. Unity/TMP handle
            // the deactivation themselves; we just bail out so the same press doesn't ALSO
            // open the menu or close a panel. The next Escape (no field focused) goes through.
            if (EventSystem.current != null)
            {
                var sel = EventSystem.current.currentSelectedGameObject;
                if (sel != null && sel.GetComponent<TMP_InputField>() != null) return;
            }

            // Close the topmost open panel/popup. If nothing is open, the in-game menu
            // acts as a pause toggle — same affordance as the MENU button.
            if (TryClose(adventurePromptPopup)  ) return;
            if (TryClose(characterScreenPanel)  ) return;
            if (TryClose(editMapPanel)          ) return;
            if (TryClose(inGameMenuPanel)       ) return;
            if (characterPopup != null && characterPopup.gameObject.activeSelf)
            {
                // Mirror the popup's own Cancel button so the GameManager state machine
                // gets the OnCancelled callback (returns to MainMenu).
                characterPopup.gameObject.SetActive(false);
                characterPopup.OnCancelled?.Invoke();
                return;
            }

            OnMenuButtonPressed();
        }

        // ── Turn-order strip ─────────────────────────────────────────────
        // Reads the TurnQueue and renders it as "▶ Aric → Lyra → Goblin" with the active
        // entry styled gold/bold. Hidden entirely when the queue is empty (e.g. MainMenu).
        private void RefreshTurnStrip()
        {
            if (turnStripText == null) return;
            int count = Turns.Count;
            if (count == 0)
            {
                turnStripText.text = "";
                turnStripText.gameObject.SetActive(false);
                return;
            }
            turnStripText.gameObject.SetActive(true);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                var e = Turns.Order[i];
                if (e == null) continue;
                if (i > 0) sb.Append("  →  ");
                bool active = (i == Turns.CurrentIndex);
                if (active)        sb.Append("<b><color=#C8A050>▶ ").Append(e.DisplayName).Append("</color></b>");
                else if (e.IsPlayer) sb.Append("<color=#A08060>").Append(e.DisplayName).Append("</color>");
                else                 sb.Append("<color=#7A5A40>").Append(e.DisplayName).Append("</color>");
            }
            turnStripText.text = sb.ToString();
        }

        private static bool TryClose(MonoBehaviour panel)
        {
            if (panel == null || !panel.gameObject.activeSelf) return false;
            switch (panel)
            {
                case DnD.UI.InGameMenuPanel m:        m.Close(); return true;
                case DnD.UI.EditMapPanel e:           e.Close(); return true;
                case DnD.UI.CharacterScreenPanel c:   c.Close(); return true;
                // Adventure prompt's OnCancel handler in GameManager closes the popup AND
                // calls ChangeState(MainMenu) (which re-shows the title screen). Skipping
                // OnCancel — as a plain Close() would — leaves the user with a black screen
                // because the title was already hidden when the popup opened.
                case DnD.UI.AdventurePromptPopup a:
                    if (a.OnCancel != null) a.OnCancel.Invoke();
                    else                    a.Close();
                    return true;
                default:                              panel.gameObject.SetActive(false); return true;
            }
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
            {
                dungeonMaster.OnDMResponse += OnDMResponse;
                dungeonMaster.OnToolDispatched += (toolName, _, result) =>
                {
                    if (ChatUI.Instance != null)
                        ChatUI.Instance.AddSystemMessage($"› {toolName} → {result}");
                };
            }

            if (CombatManager.Instance != null)
            {
                CombatManager.Instance.OnCombatMessage += OnCombatMessage;
                CombatManager.Instance.OnPlayerTurnStart += () => ChatUI.Instance?.SetInputEnabled(true);
                CombatManager.Instance.OnPlayerTurnEnd   += () => ChatUI.Instance?.SetInputEnabled(false);
                CombatManager.Instance.OnEnemyTurnStart  += () => ChatUI.Instance?.SetInputEnabled(false);
                CombatManager.Instance.OnCombatEnded     += _  => ChatUI.Instance?.SetInputEnabled(true);
            }

            Debug.Log("[GameManager] All systems initialized!");
        }

        public void ChangeState(GameState newState)
        {
            Debug.Log($"[GameManager] State: {currentState} -> {newState}");
            currentState = newState;

            // Hide popups that must not linger across state changes
            if (adventurePromptPopup != null) adventurePromptPopup.Close();
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
                if (DNDLLM.Services.SaveSystem.Load(i) == null) { slot = i; break; }
            }
            _currentSlotIndex = slot;

            if (titleScreen != null) titleScreen.gameObject.SetActive(false);

            if (adventurePromptPopup != null)
            {
                // Prefer the size-aware callback; OnSubmit is the back-compat fallback
                // (it still fires alongside OnSubmitWithSize, so leave it unset to avoid duplicate work).
                adventurePromptPopup.OnSubmit          = null;
                adventurePromptPopup.OnSubmitWithSize  = OnAdventurePromptSubmittedWithSize;
                adventurePromptPopup.OnCancel          = OnAdventurePromptCancelled;
                adventurePromptPopup.Open();
            }
            else if (ChatUI.Instance != null)
            {
                // Fallback if the popup isn't built yet — keep the old chat flow working.
                Debug.LogWarning("[GameManager] adventurePromptPopup not assigned; falling back to chat prompt. Run DnD/Setup Scene (All Steps) to build it.");
                ChatUI.Instance.ClearChat();
                ChatUI.Instance.AddSystemMessage("=== NEW ADVENTURE ===");
                ChatUI.Instance.AddSystemMessage("Describe the adventure you want to embark on...");
            }
        }

        private async void OnAdventurePromptSubmitted(string prompt)
        {
            // Back-compat path (no size info). Defaults to Medium.
            if (adventurePromptPopup != null) adventurePromptPopup.Close();
            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.ClearChat();
                ChatUI.Instance.AddSystemMessage("=== NEW ADVENTURE ===");
            }
            await StartCampaignAsync(prompt, CampaignSize.Medium);
        }

        private async void OnAdventurePromptSubmittedWithSize(string prompt, CampaignSize size)
        {
            if (adventurePromptPopup != null) adventurePromptPopup.Close();
            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.ClearChat();
                ChatUI.Instance.AddSystemMessage($"=== NEW ADVENTURE — {CampaignSizeInfo.Label(size)} ===");
            }
            await StartCampaignAsync(prompt, size);
        }

        private void OnAdventurePromptCancelled()
        {
            if (adventurePromptPopup != null) adventurePromptPopup.Close();
            ChangeState(GameState.MainMenu);
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
            _characterMapToken       = null; // new character → regenerate the map sprite
            _appearanceDescription   = data.appearanceDescription ?? "";
            _backstory               = data.backstory              ?? "";

            if (playerCharacter == null)
            {
                var go = new GameObject("Player");
                playerCharacter = go.AddComponent<DnD.Character.CharacterStats>();
                DontDestroyOnLoad(go);
            }
            // Register the active character into the party list (1-element today,
            // up to 4 once the multi-player creation flow lands). The TurnQueue is
            // then rebuilt with this party — single entry rotates trivially.
            if (!playerCharacters.Contains(playerCharacter))
                playerCharacters.Add(playerCharacter);
            currentPlayerIndex = playerCharacters.IndexOf(playerCharacter);
            Turns.BeginExploration(playerCharacters);

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
                campaignSizeName      = _currentPlan != null ? _currentPlan.sizeName : _currentSize.ToString(),
                campaignPlanJson      = _currentPlan != null ? UnityEngine.JsonUtility.ToJson(_currentPlan) : "",
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
            UnityEngine.Texture2D savedMapBackground = null;
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
                savedMapBackground = gen.StyleAnchor; // the painted holistic battlemap with grid
            }

            // Capture player grid position so reload doesn't reset to the south entrance
            if (MapCharacterController.Instance != null)
            {
                saveData.playerX = MapCharacterController.Instance.GridX;
                saveData.playerY = MapCharacterController.Instance.GridY;
            }

            // Capture every enemy/NPC currently on the map plus their sprite textures
            saveData.entities = new List<DnD.Data.EntityEntry>();
            var entitySprites = new List<UnityEngine.Texture2D>();
            foreach (var ent in MapEntityController.All)
            {
                if (ent == null) continue;
                saveData.entities.Add(new DnD.Data.EntityEntry
                {
                    name     = ent.EntityName,
                    x        = ent.GridX,
                    y        = ent.GridY,
                    hp       = ent.HP,
                    maxHp    = ent.MaxHP,
                    ac       = ent.AC,
                    isEnemy  = ent.IsEnemy,
                    isHidden = ent.IsHidden,
                });
                var sr  = ent.GetComponent<UnityEngine.SpriteRenderer>();
                entitySprites.Add(sr != null && sr.sprite != null ? sr.sprite.texture as UnityEngine.Texture2D : null);
            }

            // ── Multi-player party state ─────────────────────────────────────
            // Populate players[] alongside the legacy flat fields. New saves are
            // dual-format so they can be loaded by both old and new builds.
            saveData.players = new List<DnD.Data.PlayerSaveEntry>();
            for (int i = 0; i < playerCharacters.Count; i++)
            {
                var p = playerCharacters[i];
                if (p == null) continue;
                bool isCurrent = ReferenceEquals(p, playerCharacter);
                int gx = 0, gy = 0;
                var ctrl = MapCharacterController.For(p);
                if (ctrl != null) { gx = ctrl.GridX; gy = ctrl.GridY; }
                saveData.players.Add(new DnD.Data.PlayerSaveEntry
                {
                    characterName         = p.characterName,
                    raceName              = p.race.ToString(),
                    className             = p.characterClass != null ? p.characterClass.className.ToString() : "",
                    appearanceDescription = isCurrent ? _appearanceDescription : "",
                    backstory             = isCurrent ? _backstory             : "",
                    level                 = p.level,
                    maxHP                 = p.maxHitPoints,
                    currentHP             = p.currentHitPoints,
                    armorClass            = p.armorClass,
                    str                   = p.abilities.GetScore(AbilityScore.Strength),
                    dex                   = p.abilities.GetScore(AbilityScore.Dexterity),
                    con                   = p.abilities.GetScore(AbilityScore.Constitution),
                    intel                 = p.abilities.GetScore(AbilityScore.Intelligence),
                    wis                   = p.abilities.GetScore(AbilityScore.Wisdom),
                    cha                   = p.abilities.GetScore(AbilityScore.Charisma),
                    gridX                 = gx,
                    gridY                 = gy,
                });
            }
            saveData.currentPlayerIndex = currentPlayerIndex;

            UnityEngine.Texture2D savePortrait = portrait ?? _characterPortrait;
            DNDLLM.Services.SaveSystem.Save(
                _currentSlotIndex, saveData,
                savePortrait, _characterMapToken, savedMapBackground, entitySprites);
            // Per-player image files are written for index ≥ 1; index 0 already maps to
            // the legacy slot_{i}_portrait.png + slot_{i}_token.png written by Save above.
            for (int i = 1; i < playerCharacters.Count; i++)
            {
                // Today the only sprites we have per-character are for the lead character;
                // additional party members will get portraits/tokens once multi-character
                // creation lands. The Save call is harmless when textures are null.
                DNDLLM.Services.SaveSystem.SavePlayerImage(_currentSlotIndex, i, DNDLLM.Services.SaveSystem.PlayerImageKind.Portrait, null);
                DNDLLM.Services.SaveSystem.SavePlayerImage(_currentSlotIndex, i, DNDLLM.Services.SaveSystem.PlayerImageKind.MapToken, null);
            }
            Debug.Log($"[GameManager] Saved slot {_currentSlotIndex}: {saveData.slotLabel} " +
                      $"({saveData.entities.Count} entities, {saveData.players.Count} player(s), map={(savedMapBackground != null ? "yes" : "no")})");
        }

        private void LoadSlot(int slotIndex)
        {
            var loaded = DNDLLM.Services.SaveSystem.Load(slotIndex);
            if (loaded == null || loaded.Data == null)
            {
                Debug.LogWarning($"[GameManager] Slot {slotIndex} is empty.");
                ChangeState(GameState.MainMenu);
                return;
            }

            var data = loaded.Data;

            // Clear any entities and world graph from a previous session
            MapEntityController.ClearAll();
            _mapGraph.Reset();

            _currentSlotIndex        = slotIndex;
            _campaignSeed            = data.campaignSeed ?? "";
            _appearanceDescription   = data.appearanceDescription ?? "";
            _backstory               = data.backstory ?? "";
            _characterPortrait       = loaded.Portrait;
            _characterMapToken       = loaded.MapToken;

            // Queue tile descriptions and per-tile grid to be applied once the map finishes generating
            _pendingTileDescriptions = (data.tileDescriptions != null && data.tileDescriptions.Count > 0)
                ? data.tileDescriptions : null;
            _pendingTileGrid = (data.tileGrid != null && data.tileGrid.Count > 0)
                ? data.tileGrid : null;

            // Map background and entity sprites: queued for rehydrate-instead-of-regen on map ready
            _pendingMapBackground = loaded.MapBackground;
            _pendingPlayerX       = data.playerX;
            _pendingPlayerY       = data.playerY;
            _pendingEntities      = (data.entities != null && data.entities.Count > 0) ? data.entities : null;
            _pendingEntitySprites = loaded.EntitySprites;

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

            // Multi-player: rebuild the party list from data.players[] when present.
            // Legacy saves (players.Count == 0) → 1-element list using the playerCharacter
            // we just populated from the flat fields, preserving single-player behaviour.
            playerCharacters.Clear();
            playerCharacters.Add(playerCharacter);
            if (data.players != null && data.players.Count > 1)
            {
                for (int i = 1; i < data.players.Count; i++)
                {
                    var p = data.players[i];
                    var go = new GameObject($"Player_{i}");
                    DontDestroyOnLoad(go);
                    var cs = go.AddComponent<DnD.Character.CharacterStats>();
                    cs.characterName    = p.characterName;
                    cs.level            = p.level;
                    cs.maxHitPoints     = p.maxHP;
                    cs.currentHitPoints = p.currentHP;
                    cs.armorClass       = p.armorClass;
                    cs.abilities        = new AbilityScores(p.str, p.dex, p.con, p.intel, p.wis, p.cha);
                    if (!string.IsNullOrEmpty(p.className) && System.Enum.TryParse<CharacterClassName>(p.className, out var cls))
                    {
                        int hd = cls == CharacterClassName.Fighter ? 10
                               : cls == CharacterClassName.Wizard  ?  6
                               : cls == CharacterClassName.Rogue   ?  8 : 8;
                        cs.characterClass = CreateBasicClass(cls, hd);
                    }
                    if (System.Enum.TryParse<Race>(p.raceName, out var rc)) cs.race = rc;
                    playerCharacters.Add(cs);
                }
            }
            currentPlayerIndex = (data.currentPlayerIndex >= 0 && data.currentPlayerIndex < playerCharacters.Count)
                ? data.currentPlayerIndex : 0;
            playerCharacter = playerCharacters[currentPlayerIndex];
            Turns.BeginExploration(playerCharacters);

            if (!string.IsNullOrEmpty(data.campaignTimeline))
                currentCampaign = new StoryTimeline
                {
                    campaignPrompt = data.campaignSeed ?? "",
                    timelineText   = data.campaignTimeline
                };

            // Restore structured campaign plan if present (new saves). Old saves skip this block.
            _currentPlan = null;
            if (!string.IsNullOrEmpty(data.campaignPlanJson))
            {
                try { _currentPlan = UnityEngine.JsonUtility.FromJson<CampaignPlan>(data.campaignPlanJson); }
                catch (System.Exception e) { Debug.LogWarning($"[GameManager] CampaignPlan parse failed: {e.Message}"); }
            }
            if (_currentPlan != null) _currentSize = _currentPlan.Size;
            else if (System.Enum.TryParse<CampaignSize>(data.campaignSizeName ?? "", true, out var parsedSize))
                _currentSize = parsedSize;

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
            "each on its own line prefixed with '› '. Keep suggestions short (5-8 words each).\n\n" +
            "You have access to game tools. After the › suggestions you MAY append a " +
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
            "  REVEAL_ENTITY <name>           (reveals a hidden entity — used when the player notices it)\n" +
            "  LOCK_DOOR <x> <y>              (bars a passage — player cannot pass)\n" +
            "  UNLOCK_DOOR <x> <y>            (opens a previously locked passage)\n" +
            "  ENTER_SUBREGION <description>  (transport player into a named sub-area, e.g. 'dark armory' or 'flooded cellar')\n\n" +
            "Example response ending:\n" +
            "› Search the body\n" +
            "› Retreat north\n" +
            "› Call for help\n" +
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

            if (MapGenerator.Instance == null)
            {
                Debug.LogError("[GameManager] MapGenerator.Instance is null — run DnD/Setup Game Manager to attach it to GameSystem.");
                return;
            }

            MapGenerator.Instance.OnMapReady -= OnMapReadyNarrate;
            MapGenerator.Instance.OnMapReady += OnMapReadyNarrate;
            // Skip LLM description generation when we have saved descriptions to restore
            MapGenerator.Instance.SkipDescriptionGeneration = _pendingTileDescriptions != null;

            // Apply campaign size + plan features before generation. Only do this for a fresh
            // campaign (no saved map) — reloads must use whatever dims the save expects.
            if (_pendingMapBackground == null && _pendingTileGrid == null && _currentPlan != null)
            {
                int dim = _currentPlan.MapDim;
                MapGenerator.Instance.width  = dim;
                MapGenerator.Instance.height = dim;
                MapGenerator.Instance.KeyFeatures = _currentPlan.keyLocations;
                MapGenerator.Instance.StartingContext = _currentPlan.startingArea ?? "";
            }

            // If LoadSlot left us a saved map background, skip the 30-60s LLM holistic-paint
            // pipeline entirely and rebuild the visible map from the cached PNG + saved tile grid.
            if (_pendingMapBackground != null && _pendingTileGrid != null)
            {
                MapGenerator.Instance.RehydrateFromSavedState(
                    _pendingMapBackground,
                    _campaignSeed.Length > 0 ? _campaignSeed : "dungeon",
                    _pendingTileGrid);
                _pendingMapBackground = null;
                // _pendingTileGrid stays consumed inside Rehydrate; clear so OnMapReadyNarrate's
                // overlay loop doesn't double-apply.
                _pendingTileGrid = null;
                return;
            }

            MapGenerator.Instance.GenerateMap(
                _campaignSeed.Length > 0 ? _campaignSeed : "dungeon");
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

            // After spawn, restore the saved grid position so reload doesn't reset to the
            // south entrance. (0,0) is treated as "no saved position" — character stays at
            // SpawnCharacterOnMapAsync's default start cell.
            if (MapCharacterController.Instance != null && (_pendingPlayerX != 0 || _pendingPlayerY != 0))
            {
                MapCharacterController.Instance.MoveTo(_pendingPlayerX, _pendingPlayerY);
                _pendingPlayerX = 0;
                _pendingPlayerY = 0;
            }

            // ── 3. Spawn enemy / NPC tokens ───────────────────────────────
            // If LoadSlot queued saved entities, rehydrate from the saved sprites + state
            // (preserving HP, names, positions). Otherwise generate fresh entities.
            if (_pendingEntities != null)
            {
                RehydrateEntitiesFromSave(_pendingEntities, _pendingEntitySprites);
                _pendingEntities      = null;
                _pendingEntitySprites = null;
            }
            else
            {
                await SpawnMapEntitiesAsync();
            }

            // ── 4. Save root snapshot into map graph ─────────────────────
            var cc = MapCharacterController.Instance;
            var rootSnap = MapGenerator.Instance?.TakeSnapshot(cc?.GridX ?? 0, cc?.GridY ?? 0);
            if (rootSnap != null) _mapGraph.SaveCurrentSnapshot(rootSnap);

            // ── 5. Auto-save ──────────────────────────────────────────────
            SaveCurrentSlot();
            if (ChatUI.Instance != null)
                ChatUI.Instance.AddSystemMessage("Adventure saved.");
        }

        /// <summary>Recreate entity GameObjects from saved entries + sprite textures (no LLM calls).</summary>
        private static void RehydrateEntitiesFromSave(
            List<DnD.Data.EntityEntry> entries,
            List<UnityEngine.Texture2D> sprites)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var go = new GameObject($"Entity_{e.name}_{e.x}_{e.y}");
                go.AddComponent<UnityEngine.SpriteRenderer>();
                var ec = go.AddComponent<MapEntityController>();
                UnityEngine.Texture2D tex = (sprites != null && i < sprites.Count) ? sprites[i] : null;
                // Initialize sets HP=MaxHP=hp; pass maxHp first then override HP so we keep
                // any "took N damage" state across reloads.
                ec.Initialize(tex, e.name, e.x, e.y, e.maxHp, e.ac, e.isEnemy, e.isHidden);
                ec.HP = e.hp;
            }
        }

        private async Task SpawnCharacterOnMapAsync()
        {
            if (MapGenerator.Instance == null) return;

            Texture2D charTex = _characterMapToken; // reuse persisted token if we have one
            using var _busy = charTex == null
                ? DNDLLM.Services.BusyIndicator.Show("Generating character sprite…")
                : null;

            // Debug-sprite shortcut — skip LLM image generation entirely.
            if (charTex == null && LLMService.Instance != null && LLMService.Instance.useDebugSprites)
                charTex = DNDLLM.Utils.DebugSpriteFactory.MakeCharacterToken(64);

            // Generate a top-down character token styled to match the map's tiles
            if (charTex == null
                && LLMService.Instance != null && MapGenerator.Instance.StyleAnchor != null
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

            // Fall back to the character portrait if token generation failed.
            // NOTE: don't write the map token into _characterPortrait — the portrait is the
            // canonical character image. Cache the generated token in _characterMapToken so the
            // next save persists it and subsequent maps reuse it without another LLM round-trip.
            if (charTex == null) charTex = _characterPortrait;
            if (charTex != null && _characterMapToken == null) _characterMapToken = charTex;

            // Starting cell: just inside the south entrance (bottom door)
            int startX = MapGenerator.Instance.width  / 2;
            int startY = 1; // row above the bottom wall

            if (MapCharacterController.Instance == null)
            {
                Debug.LogError("[GameManager] MapCharacterController.Instance is null — run DnD/Setup Game Manager to attach it.");
                return;
            }

            MapCharacterController.Instance.Initialize(charTex, startX, startY);
            // Tag the controller with the active CharacterStats so multi-player tools can
            // resolve "move/damage <name>" via MapCharacterController.For(stats).
            MapCharacterController.Instance.Stats = playerCharacter;
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

        private Task EnterSubregionViaKey(string graphKey, string childTheme,
                                           int returnX, int returnY,
                                           string announceMsg,
                                           string parentContext = "")
        {
            var gen = MapGenerator.Instance;
            var cc  = MapCharacterController.Instance;
            if (gen == null) return Task.CompletedTask;

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
                return Task.CompletedTask;
            }

            // ── New room: give LLM context about the parent area, then generate ──
            if (!string.IsNullOrEmpty(parentContext))
                gen.StartingContext = parentContext;

            gen.OnMapReady -= OnMapReadyNarrate;
            gen.OnMapReady -= OnSubMapReady;
            gen.OnMapReady += OnSubMapReady;
            gen.SkipDescriptionGeneration = false;
            gen.GenerateMap(childTheme);
            return Task.CompletedTask;
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

            // Ask LLM for both a 2-4 word name AND whether the entity is concealed.
            // Reply format keeps it parseable without JsonUtility.
            string raw = await LLMService.Instance.SendPrompt(
                "You are a D&D world builder. Reply with EXACTLY two lines:\n" +
                "NAME: <2-4 word creature or character name, no punctuation>\n" +
                "HIDDEN: <true|false>\n" +
                "Set HIDDEN to true ONLY when concealment is dramatically appropriate " +
                "(stalking predator, ambush, lurking horror, hidden trap or watcher). " +
                "Default to false otherwise — most NPCs and visible monsters are not hidden.",
                $"In a {gen.LastTheme} setting, describe one {role} that fits the environment.");
            string entityName = fallbackName;
            bool   isHidden   = false;
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (var rawLine in raw.Split('\n'))
                {
                    string line = rawLine.Trim();
                    int colon = line.IndexOf(':');
                    if (colon < 0) continue;
                    string key = line.Substring(0, colon).Trim().ToUpperInvariant();
                    string val = line.Substring(colon + 1).Trim();
                    if (key == "NAME" && !string.IsNullOrEmpty(val)) entityName = val;
                    else if (key == "HIDDEN") isHidden = val.StartsWith("t", System.StringComparison.OrdinalIgnoreCase)
                                                     || val == "1" || val.StartsWith("y", System.StringComparison.OrdinalIgnoreCase);
                }
            }

            // Suppress the public "Generating sprite for X..." message for hidden entities
            // so the player doesn't immediately know what's lurking on the map.
            if (ChatUI.Instance != null && !isHidden && !LLMService.Instance.useDebugSprites)
                ChatUI.Instance.AddSystemMessage($"Generating sprite for {entityName}...");

            Texture2D tex;
            if (LLMService.Instance.useDebugSprites)
            {
                tex = DNDLLM.Utils.DebugSpriteFactory.MakeEntityToken(isEnemy, 64);
            }
            else
            {
                string kind = isEnemy ? "fearsome monster" : "NPC character";
                string prompt =
                    $"Square, 1:1 aspect ratio. Top-down RPG map token: {entityName}, {kind} "
                    + $"in a {gen.LastTheme} setting, viewed directly from above. "
                    + "Centered on a FULLY TRANSPARENT background — no floor, no tile, no ground. "
                    + "The entity is the only visible element. "
                    + "Match the exact art style, color palette of the reference tile. "
                    + "Flat overhead view, no border, no drop shadow.";

                tex = await LLMService.Instance.GenerateStyledTile(prompt, gen.StyleAnchor);
                if (tex != null) tex = SpriteBackgroundRemover.RemoveBackground(tex);
            }

            var go = new GameObject($"Entity_{entityName}_{x}_{y}");
            go.AddComponent<SpriteRenderer>();
            var entity = go.AddComponent<MapEntityController>();
            entity.Initialize(
                tex, entityName, x, y,
                hp: isEnemy ? UnityEngine.Random.Range(8, 20) : 10,
                ac: isEnemy ? UnityEngine.Random.Range(10, 14) : 10,
                isEnemy: isEnemy,
                isHidden: isHidden);
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

        private async Task StartCampaignAsync(string campaignPrompt, CampaignSize size = CampaignSize.Medium)
        {
            _campaignSeed = campaignPrompt;
            _currentSize  = size;
            if (ChatUI.Instance != null)
                ChatUI.Instance.AddSystemMessage($"Designing the {CampaignSizeInfo.Label(size)} campaign...");

            // Structured plan drives map size + features. Fallback handles unreachable LLM.
            _currentPlan = await dungeonMaster.GenerateCampaignPlanAsync(campaignPrompt, size, 1);
            if (_currentPlan == null) _currentPlan = CampaignPlan.Fallback(campaignPrompt, size);

            // Mirror onto the legacy StoryTimeline so older save paths still display something.
            currentCampaign = new StoryTimeline
            {
                campaignPrompt = campaignPrompt,
                timelineText   = _currentPlan.timelineText ?? _currentPlan.ToReadableText(),
                partyLevel     = 1,
            };

            if (ChatUI.Instance != null && !string.IsNullOrEmpty(currentCampaign.timelineText))
                ChatUI.Instance.AddDMMessage(currentCampaign.timelineText, useTypewriter: true);

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
            // ── Fast-path: explicit direction keywords move immediately. ─────
            string movementNote = "";
            if (TryParseMovement(input, out int dx, out int dy))
            {
                bool moved = MapCharacterController.Instance?.TryMove(dx, dy) ?? false;
                if (!moved && ChatUI.Instance != null)
                    ChatUI.Instance.AddSystemMessage("Something blocks your path.");
                movementNote = moved
                    ? "(Player has just moved one tile via direction keyword — do NOT call MOVE again.)"
                    : "(Player tried to move but was blocked.)";

                // Door / exit transitions: handle and stop
                if (moved)
                {
                    var cc  = MapCharacterController.Instance;
                    var gen = MapGenerator.Instance;
                    if (cc != null && gen?.grid != null)
                    {
                        var tileType = gen.grid[cc.GridX, cc.GridY].type;
                        if (tileType == TileType.Door)  { await TryEnterRoom(); return; }
                        else if (tileType == TileType.Exit) { TryExitRoom(); return; }
                    }
                }
            }

            if (dungeonMaster == null) return;

            // ── DM tool loop: one tool per LLM round-trip until final narration. ─
            string envContext = BuildExplorationContext(movementNote);
            string narration  = await dungeonMaster.RunPlayerTurnAsync(input, envContext, playerCharacter);
            if (!string.IsNullOrEmpty(narration) && ChatUI.Instance != null)
                ChatUI.Instance.AddDMMessage(narration, useTypewriter: true);
        }

        private string BuildExplorationContext(string movementNote)
        {
            string pos = MapCharacterController.Instance != null
                ? $"Player at grid ({MapCharacterController.Instance.GridX},{MapCharacterController.Instance.GridY})."
                : "";
            string tileCtx = "";
            if (MapGenerator.Instance != null && MapCharacterController.Instance != null)
                tileCtx = "Environment:\n" + MapGenerator.Instance.GetTileContext(
                    MapCharacterController.Instance.GridX,
                    MapCharacterController.Instance.GridY);
            return $"Campaign: {_campaignSeed}\n{pos} {movementNote}\n{tileCtx}".Trim();
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
            if (CombatManager.Instance == null || !CombatManager.Instance.IsPlayerTurn()) return;

            IGameCommand command = await commandParser.ParseCommandAsync(input, playerCharacter);
            if (command != null && command.CanExecute())
                command.Execute();

            // Wake the combat coroutine so the next turn can begin.
            CombatManager.Instance.NotifyPlayerActed();
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
