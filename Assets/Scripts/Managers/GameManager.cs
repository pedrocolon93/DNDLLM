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

        private async void Start()
        {
            await InitializeSystemsAsync();
            // Yield one frame so all MonoBehaviour Awake/Start calls complete
            // before ChatUI.Instance is accessed
            await System.Threading.Tasks.Task.Yield();
            // Fallback if singleton wasn't set in time
            if (ChatUI.Instance == null)
            {
                var found = FindFirstObjectByType<ChatUI>();
                if (found != null)
                    ChatUI.Instance = found;
            }
            ChangeState(GameState.MainMenu);
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
            commandParser = FindObjectOfType<CommandParser>();
            if (commandParser == null)
            {
                GameObject parserObj = new GameObject("CommandParser");
                commandParser = parserObj.AddComponent<CommandParser>();
                DontDestroyOnLoad(parserObj);
            }
            commandParser.Initialize(llmProvider);

            // Subscribe to events
            if (ChatUI.Instance != null)
            {
                ChatUI.Instance.OnPlayerInput += HandlePlayerInput;
            }

            if (dungeonMaster != null)
            {
                dungeonMaster.OnDMResponse += OnDMResponse;
            }

            if (CombatManager.Instance != null)
            {
                CombatManager.Instance.OnCombatMessage += OnCombatMessage;
            }

            Debug.Log("[GameManager] All systems initialized!");
        }

        public void ChangeState(GameState newState)
        {
            Debug.Log($"[GameManager] State: {currentState} -> {newState}");
            currentState = newState;

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
            if (ChatUI.Instance == null) return;
            ChatUI.Instance.AddSystemMessage("✦  WELCOME TO D&D LLM  ✦");
            ChatUI.Instance.AddSystemMessage("An adventure powered by AI.");
            ChatUI.Instance.AddSystemMessage("Describe the adventure you want to embark on...");
        }

        private void StartCharacterCreation()
        {
            if (ChatUI.Instance == null) return;
            ChatUI.Instance.AddSystemMessage("✦  CHARACTER CREATION  ✦");
            ChatUI.Instance.AddSystemMessage("Describe your hero — class, background, personality.");
        }

        private void StartExploration()
        {
            // Generate the map
            if (MapGenerator.Instance != null)
                MapGenerator.Instance.GenerateMap("dungeon");

            if (ChatUI.Instance == null) return;
            ChatUI.Instance.AddSystemMessage("✦  YOUR ADVENTURE BEGINS  ✦");
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
