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

MAP VISION:
- When a battlemap image is attached to the user message, treat it as your view of the scene.
  The image is the painted overhead map for the player's current area, with the same grid
  coordinates the textual ""Environment"" block describes. Use it to ground your narration
  in what is actually depicted (terrain, structures, features, sightlines, distances).
- Never claim something appears on the map that isn't visible in the image.

TOOL USE — IMPORTANT:
- When the player's action requires a change to game state (movement, damage, healing, conditions,
  spawning enemies, awarding XP, etc.), call the appropriate tool.
- Call ONE tool per response. Wait for its result, then decide the next step.
- After all needed tool calls have been made and resolved, send a final message containing only
  the in-character narration of what happened — and NO tool call.
- If the player's input is purely descriptive or conversational and changes nothing, skip tool
  calls entirely and respond with narration directly.

INTERACTIVE OPTIONS — IMPORTANT:
- Whenever the moment naturally invites a choice (exploration crossroads, NPC interaction,
  reactions to a threat), end your final narration with 2-4 suggested actions.
- Format each option on its own line, prefixed with ""> "" (greater-than + space). Example:
    > Investigate the ruins
    > Talk to the hooded stranger
    > Climb the ridge for a better view
- Each option must be a short imperative phrase (2-8 words), written as if the player were
  typing it. Do not number them. Do not put narration text after the option block.
- Skip the option block during fast-moving combat exchanges or when the next action is obvious.

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
        /// Generate a structured CampaignPlan via the configured LLMService. Asks the model for JSON
        /// matching the plan schema; on parse failure (or no LLMService) returns a sensible fallback.
        /// </summary>
        public async Task<CampaignPlan> GenerateCampaignPlanAsync(string seed, CampaignSize size, int partyLevel)
        {
            using var _busy = DNDLLM.Services.BusyIndicator.Show($"Building {DnD.AI.CampaignSizeInfo.Label(size)} campaign…");

            var svc = DNDLLM.Services.LLMService.Instance;
            if (svc == null)
            {
                Debug.LogWarning("[DungeonMaster] No LLMService — falling back to local plan.");
                return CampaignPlan.Fallback(seed, size);
            }

            int beatCount    = DnD.AI.CampaignSizeInfo.BeatCount(size);
            int featureCount = DnD.AI.CampaignSizeInfo.FeatureCount(size);
            int mapDim       = DnD.AI.CampaignSizeInfo.MapDim(size);

            string sys =
                "You are a D&D 5e campaign architect. Output ONLY valid JSON matching the requested schema. " +
                "No prose, no markdown fences, no commentary.";
            string usr =
                $"Design a {DnD.AI.CampaignSizeInfo.Label(size)} D&D campaign for a party of level {partyLevel} " +
                $"based on this seed:\n\"{seed}\"\n\n" +
                $"The starting area must fit on a {mapDim}x{mapDim} tile map.\n" +
                $"List exactly {beatCount} story beats and exactly {featureCount} keyLocations.\n\n" +
                "JSON shape (use these exact field names):\n" +
                "{\n" +
                "  \"hook\": \"one-sentence opening situation\",\n" +
                "  \"startingArea\": \"name of the first map area\",\n" +
                $"  \"beats\": [ \"beat 1\", \"...\" ],   // exactly {beatCount} items\n" +
                "  \"climax\": \"final confrontation in one sentence\",\n" +
                "  \"resolution\": \"how the story can end\",\n" +
                $"  \"keyLocations\": [ \"feature noun\", \"...\" ],   // {featureCount} short nouns like tavern, monastery, well, armory\n" +
                "  \"keyNPCs\": [ \"name — short role\", \"...\" ]\n" +
                "}";

            string raw = "";
            try { raw = await svc.SendPrompt(sys, usr); }
            catch (Exception e) { Debug.LogWarning($"[DungeonMaster] Plan request failed: {e.Message}"); }

            CampaignPlan plan = TryParsePlanJson(raw);
            if (plan == null)
            {
                Debug.LogWarning($"[DungeonMaster] Plan parse failed — raw response was:\n{raw}");
                plan = CampaignPlan.Fallback(seed, size);
            }

            plan.seed = seed ?? "";
            plan.Size = size;
            if (plan.beats == null) plan.beats = new List<string>();
            if (plan.keyLocations == null) plan.keyLocations = new List<string>();
            if (plan.keyNPCs == null) plan.keyNPCs = new List<string>();
            plan.timelineText = plan.ToReadableText();

            conversationHistory.Add($"Campaign seed: {seed}");
            conversationHistory.Add($"Campaign plan: {plan.timelineText}");

            return plan;
        }

        /// <summary>Strip optional ```json fences and parse via JsonUtility. Returns null on failure.</summary>
        private static CampaignPlan TryParsePlanJson(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string s = raw.Trim();
            if (s.StartsWith("```"))
            {
                int nl = s.IndexOf('\n');
                if (nl >= 0) s = s.Substring(nl + 1);
                int fence = s.LastIndexOf("```");
                if (fence >= 0) s = s.Substring(0, fence);
                s = s.Trim();
            }
            // Locate the outermost { ... } in case the model wrapped extra text.
            int open  = s.IndexOf('{');
            int close = s.LastIndexOf('}');
            if (open < 0 || close <= open) return null;
            s = s.Substring(open, close - open + 1);
            try { return JsonUtility.FromJson<CampaignPlan>(s); }
            catch (Exception e) { Debug.LogWarning($"[DungeonMaster] Plan JSON parse exception: {e.Message}"); return null; }
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

            using var _busy = DNDLLM.Services.BusyIndicator.Show("Building campaign…");

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

            using var _busy = DNDLLM.Services.BusyIndicator.Show("DM narrating…");

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

        public event Action<string, string, string> OnToolDispatched; // toolName, argsJson, resultText

        /// <summary>
        /// Iterative tool-call loop. The LLM emits one tool per round; each result is fed back
        /// until it returns a final narration (or the step cap is hit). Returns the final narration.
        /// </summary>
        public async Task<string> RunPlayerTurnAsync(
            string playerInput,
            string envContext,
            DnD.Character.CharacterStats player,
            int maxToolSteps = 8)
        {
            if (!isInitialized || llmProvider == null)
                return "The Dungeon Master is currently unavailable.";

            using var _busy = DNDLLM.Services.BusyIndicator.Show("DM is thinking…");

            // Use LLMService directly (it implements the tool-call API). The ILLMProvider may also
            // implement it (mock does), but production traffic goes through LLMService.Instance.
            var svc = DNDLLM.Services.LLMService.Instance;
            if (svc == null)
            {
                Debug.LogWarning("[DungeonMaster] LLMService instance missing; falling back to text narration.");
                return await NarrateActionAsync(playerInput, envContext);
            }

            var tools = GMToolExecutor.GetToolDefinitions();
            var msgs = new List<LLMChatMessage>();
            msgs.Add(LLMChatMessage.System(DM_SYSTEM_PROMPT));

            string history = GetRecentHistory();
            if (!string.IsNullOrEmpty(history))
                msgs.Add(LLMChatMessage.System(history));

            string userBlock = string.IsNullOrEmpty(envContext)
                ? $"Player: {playerInput}"
                : $"Context: {envContext}\n\nPlayer: {playerInput}";

            // Attach the painted battlemap to the player turn so multimodal models (Qwen, Gemini,
            // GPT-4o, …) can see what the player is looking at. StyleAnchor holds the final
            // grid-overlaid map texture; on text-only models the image is ignored harmlessly.
            string mapDataUrl = TryEncodeMapDataUrl();
            msgs.Add(string.IsNullOrEmpty(mapDataUrl)
                ? LLMChatMessage.User(userBlock)
                : LLMChatMessage.UserWithImage(userBlock, mapDataUrl));

            string finalNarration = "";
            int steps = 0;

            try
            {
                while (steps < maxToolSteps)
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    LLMChatResult result = await svc.ChatWithToolsAsync(msgs, tools, cts.Token);

                    if (result.HasToolCalls)
                    {
                        msgs.Add(LLMChatMessage.AssistantToolCalls(result.ToolCalls));
                        foreach (var call in result.ToolCalls)
                        {
                            string r = GMToolExecutor.DispatchToolCall(call.Name, call.ArgumentsJson, player);
                            msgs.Add(LLMChatMessage.Tool(call.Id, call.Name, r));
                            OnToolDispatched?.Invoke(call.Name, call.ArgumentsJson, r);
                            steps++;
                        }
                        continue;
                    }

                    finalNarration = result.Text ?? "";
                    break;
                }

                if (steps >= maxToolSteps && string.IsNullOrEmpty(finalNarration))
                    finalNarration = $"(DM stopped after {steps} tool calls.)";
            }
            catch (Exception e)
            {
                Debug.LogError($"[DungeonMaster] Tool loop failed: {e.Message}");
                finalNarration = "The Dungeon Master loses their train of thought...";
            }

            AddToHistory($"Player: {playerInput}");
            if (!string.IsNullOrEmpty(finalNarration)) AddToHistory($"DM: {finalNarration}");
            OnDMResponse?.Invoke(finalNarration);
            return finalNarration;
        }

        // Cached PNG-base64 encoding of the battlemap. Keyed by texture reference so that a
        // new map (different StyleAnchor instance) automatically invalidates the cache, and
        // so repeated player turns on the same map skip ~100–300 ms of PNG+base64 work.
        private static Texture2D _cachedMapTex;
        private static string    _cachedMapDataUrl;

        /// <summary>Returns a data:image/png;base64,... URL for the current battlemap, or "" if none/unreadable.</summary>
        private static string TryEncodeMapDataUrl()
        {
            var gen = DNDLLM.Map.MapGenerator.Instance;
            var tex = gen != null ? gen.StyleAnchor : null;
            if (tex == null) return "";
            if (ReferenceEquals(tex, _cachedMapTex)) return _cachedMapDataUrl ?? "";

            try
            {
                byte[] png = tex.EncodeToPNG();
                if (png == null || png.Length == 0) return "";
                string url = "data:image/png;base64," + System.Convert.ToBase64String(png);
                _cachedMapTex     = tex;
                _cachedMapDataUrl = url;
                return url;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DungeonMaster] Map encode failed: {e.Message}");
                return "";
            }
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
