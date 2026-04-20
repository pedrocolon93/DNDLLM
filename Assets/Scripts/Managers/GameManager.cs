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

        private int _currentSlotIndex = 0;
        private string _campaignSeed = "";

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

                var chatSearch = FindFirstObjectByType<ChatUI>(FindObjectsInactive.Include);
                if (ChatUI.Instance == null && chatSearch != null)
                    ChatUI.Instance = chatSearch;

                _initStatus = ChatUI.Instance != null ? "ChatUI found" : "ChatUI MISSING";

                if (ChatUI.Instance != null)
                    ChatUI.Instance.OnPlayerInput += HandlePlayerInput;

                if (menuButton != null)
                    menuButton.onClick.AddListener(OnMenuButtonPressed);

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
            commandParser = FindFirstObjectByType<CommandParser>();
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

        private void OnMenuButtonPressed()
        {
            // Only save if we're in an active game — not during menus or character creation
            if (currentState == GameState.Exploration ||
                currentState == GameState.Combat      ||
                currentState == GameState.Dialogue)
                SaveCurrentSlot();
            ChangeState(GameState.MainMenu);
        }

        private void OnCharacterCreationComplete(CharacterCreationData data)
        {
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
            playerCharacter.abilities = AbilityScores.GenerateRandom();
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
                                            : new System.Collections.Generic.List<ChatMessageData>()
            };
            saveData.slotLabel = $"{saveData.characterName} · {saveData.className} · Lv{saveData.level}";
            DNDLLM.Services.SaveSystem.Save(_currentSlotIndex, saveData, portrait);
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

            _currentSlotIndex = slotIndex;
            _campaignSeed     = data.campaignSeed ?? "";

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

        private void StartExploration()
        {
            if (MapGenerator.Instance != null)
                MapGenerator.Instance.GenerateMap("dungeon");

            if (ChatUI.Instance == null) return;
            // Don't add "ADVENTURE BEGINS" when we're resuming a loaded save
            if (!_isResumingLoad)
                ChatUI.Instance.AddSystemMessage("=== YOUR ADVENTURE BEGINS ===");
            _isResumingLoad = false;
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
            {
                ChatUI.Instance.AddSystemMessage("Creating your campaign...");
            }

            currentCampaign = await dungeonMaster.GenerateCampaignAsync(campaignPrompt, 1);

            if (currentCampaign != null && ChatUI.Instance != null)
            {
                ChatUI.Instance.AddDMMessage(currentCampaign.timelineText, useTypewriter: true);
                ChangeState(GameState.CharacterCreation);
            }
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
            // Parse command
            IGameCommand command = await commandParser.ParseCommandAsync(input, playerCharacter);

            // Narrate the action
            string narration = await dungeonMaster.NarrateActionAsync(input);

            // Execute command if valid
            if (command != null && command.CanExecute())
            {
                command.Execute();

                if (ChatUI.Instance != null)
                {
                    ChatUI.Instance.AddSystemMessage($"[{command.CommandName}] executed");
                }
            }

            // Check for combat trigger (simplified)
            if (input.ToLower().Contains("attack") || input.ToLower().Contains("fight"))
            {
                await StartRandomEncounter();
            }
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
