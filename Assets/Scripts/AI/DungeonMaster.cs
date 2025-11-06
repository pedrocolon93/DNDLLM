using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DnD.AI
{
    /// <summary>
    /// AI Dungeon Master that guides the story and generates content
    /// Uses LLM to create dynamic narratives, NPCs, and encounters
    /// </summary>
    public class DungeonMaster : MonoBehaviour
    {
        public static DungeonMaster Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private bool useStreamingResponses = true;
        [SerializeField] private int maxContextMessages = 20;

        private ILLMProvider llmProvider;
        private List<string> conversationHistory = new List<string>();
        private StoryTimeline currentTimeline;
        private bool isInitialized = false;

        public event Action<string> OnDMResponse;
        public event Action<string> OnDMResponseToken; // For streaming

        private const string DM_SYSTEM_PROMPT = @"You are an expert Dungeon Master for a D&D 5e adventure game.

Your role:
- Create engaging, immersive narratives based on player actions
- Describe environments, NPCs, and encounters vividly
- Maintain game balance and pacing
- Follow D&D 5e rules
- Respond to player questions about rules and mechanics
- Generate appropriate challenges based on party level
- Keep responses concise but descriptive (2-4 sentences)

When describing combat outcomes, reference dice rolls and mechanics.
When players explore, describe what they see, hear, and sense.
When players interact with NPCs, roleplay the characters authentically.

Always maintain the tone and setting established in the campaign.";

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(gameObject);
        }

        public async Task InitializeAsync(ILLMProvider provider)
        {
            llmProvider = provider;

            if (!llmProvider.IsReady)
            {
                await llmProvider.InitializeAsync();
            }

            isInitialized = true;
            Debug.Log("[DungeonMaster] Initialized and ready!");
        }

        public void SetProvider(ILLMProvider provider)
        {
            llmProvider = provider;
        }

        /// <summary>
        /// Generate initial story timeline from player's campaign prompt
        /// </summary>
        public async Task<StoryTimeline> GenerateCampaignAsync(string campaignPrompt, int partyLevel)
        {
            if (!isInitialized || llmProvider == null)
            {
                Debug.LogError("DungeonMaster not initialized!");
                return null;
            }

            string timelinePrompt = $@"Create a D&D campaign timeline based on this premise:
'{campaignPrompt}'

Party Level: {partyLevel}

Generate a structured timeline with:
1. Campaign Hook (initial situation)
2. 3-5 Major Story Beats (key events/encounters)
3. Climactic Encounter (final challenge)
4. Resolution

Format each section clearly. Keep it concise but exciting.";

            try
            {
                CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                string timelineText = await llmProvider.GenerateResponseAsync(DM_SYSTEM_PROMPT, timelinePrompt, cts.Token);

                currentTimeline = new StoryTimeline
                {
                    campaignPrompt = campaignPrompt,
                    timelineText = timelineText,
                    partyLevel = partyLevel
                };

                conversationHistory.Add($"Campaign: {campaignPrompt}");
                conversationHistory.Add($"Timeline: {timelineText}");

                Debug.Log($"[DungeonMaster] Campaign created:\n{timelineText}");
                return currentTimeline;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to generate campaign: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Respond to player action with narrative description
        /// </summary>
        public async Task<string> NarrateActionAsync(string playerAction, string context = "")
        {
            if (!isInitialized || llmProvider == null)
            {
                return "The Dungeon Master is currently unavailable.";
            }

            string contextInfo = string.IsNullOrEmpty(context) ? "" : $"\nContext: {context}";
            string historyContext = GetRecentHistory();

            string prompt = $@"{historyContext}{contextInfo}

Player Action: {playerAction}

Describe what happens. Include sensory details and consequences.";

            try
            {
                string response;
                CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                if (useStreamingResponses)
                {
                    response = "";
                    await llmProvider.StreamResponseAsync(prompt, (token) =>
                    {
                        response += token;
                        OnDMResponseToken?.Invoke(token);
                    }, cts.Token);
                }
                else
                {
                    response = await llmProvider.GenerateResponseAsync(DM_SYSTEM_PROMPT, prompt, cts.Token);
                }

                AddToHistory($"Player: {playerAction}");
                AddToHistory($"DM: {response}");

                OnDMResponse?.Invoke(response);
                return response;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to narrate action: {e.Message}");
                return "The Dungeon Master needs a moment to think...";
            }
        }

        /// <summary>
        /// Generate enemy/creature stats and description
        /// </summary>
        public async Task<CreatureData> GenerateCreatureAsync(string creatureDescription, int challengeRating)
        {
            if (!isInitialized || llmProvider == null)
            {
                return CreateDefaultCreature(creatureDescription, challengeRating);
            }

            string prompt = $@"Generate D&D 5e stats for this creature:
Description: {creatureDescription}
Challenge Rating: {challengeRating}

Provide:
- Name
- Hit Points (based on CR)
- Armor Class
- Attack bonus
- Damage (XdY format)
- Special abilities (if any)
- Brief description

Format as:
NAME: [name]
HP: [number]
AC: [number]
ATTACK: +[bonus]
DAMAGE: [dice]
ABILITIES: [list]
DESC: [description]";

            try
            {
                CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                string response = await llmProvider.GenerateResponseAsync(DM_SYSTEM_PROMPT, prompt, cts.Token);

                return ParseCreatureData(response, challengeRating);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to generate creature: {e.Message}");
                return CreateDefaultCreature(creatureDescription, challengeRating);
            }
        }

        private CreatureData ParseCreatureData(string llmResponse, int cr)
        {
            // Simple parsing - in production, use structured output
            CreatureData creature = new CreatureData
            {
                name = ExtractValue(llmResponse, "NAME", "Unknown Creature"),
                hitPoints = ParseInt(ExtractValue(llmResponse, "HP", "10"), 10),
                armorClass = ParseInt(ExtractValue(llmResponse, "AC", "12"), 12),
                attackBonus = ParseInt(ExtractValue(llmResponse, "ATTACK", "3").Replace("+", ""), 3),
                damageFormula = ExtractValue(llmResponse, "DAMAGE", "1d6"),
                description = ExtractValue(llmResponse, "DESC", "A mysterious creature."),
                challengeRating = cr
            };

            return creature;
        }

        private CreatureData CreateDefaultCreature(string description, int cr)
        {
            return new CreatureData
            {
                name = description,
                hitPoints = 10 + (cr * 5),
                armorClass = 10 + cr,
                attackBonus = 2 + cr,
                damageFormula = "1d6",
                description = $"A {description}",
                challengeRating = cr
            };
        }

        private string ExtractValue(string text, string key, string defaultValue = "")
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, $@"{key}:\s*(.+?)(?:\n|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : defaultValue;
        }

        private int ParseInt(string value, int defaultValue)
        {
            return int.TryParse(value, out int result) ? result : defaultValue;
        }

        private void AddToHistory(string message)
        {
            conversationHistory.Add(message);

            // Trim history to max context
            if (conversationHistory.Count > maxContextMessages)
            {
                conversationHistory.RemoveAt(0);
            }
        }

        private string GetRecentHistory()
        {
            if (conversationHistory.Count == 0)
                return "";

            int startIndex = Mathf.Max(0, conversationHistory.Count - 5);
            return "Recent events:\n" + string.Join("\n", conversationHistory.GetRange(startIndex, conversationHistory.Count - startIndex));
        }

        public void ClearHistory()
        {
            conversationHistory.Clear();
            llmProvider?.ClearHistory();
        }
    }

    [Serializable]
    public class StoryTimeline
    {
        public string campaignPrompt;
        public string timelineText;
        public int partyLevel;
        public int currentBeat = 0;
    }

    [Serializable]
    public class CreatureData
    {
        public string name;
        public string description;
        public int hitPoints;
        public int armorClass;
        public int attackBonus;
        public string damageFormula;
        public int challengeRating;
        public string specialAbilities;
    }
}
