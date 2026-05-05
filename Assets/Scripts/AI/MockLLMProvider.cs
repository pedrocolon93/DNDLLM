using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DnD.AI
{
    /// <summary>
    /// Mock LLM provider for testing without actual LLM integration
    /// Replace with real provider (OpenAI, LLMUnity, etc.) in production
    /// </summary>
    public class MockLLMProvider : ILLMProvider
    {
        private bool isInitialized = false;

        public string ProviderName => "Mock LLM";
        public bool IsReady => isInitialized;

        public async Task InitializeAsync()
        {
            Debug.Log("[MockLLM] Initializing...");
            await Task.Delay(500); // Simulate initialization
            isInitialized = true;
            Debug.Log("[MockLLM] Ready!");
        }

        public async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default)
        {
            await Task.Delay(500, cancellationToken); // Simulate API delay

            // Simple keyword-based responses for testing
            string lowerPrompt = prompt.ToLower();

            if (lowerPrompt.Contains("attack") || lowerPrompt.Contains("fight"))
                return "You swing your weapon at the goblin! Roll for attack.";

            if (lowerPrompt.Contains("move") || lowerPrompt.Contains("go") || lowerPrompt.Contains("walk"))
                return "You move forward cautiously, keeping an eye out for danger.";

            if (lowerPrompt.Contains("look") || lowerPrompt.Contains("examine"))
                return "You carefully examine your surroundings. The dungeon corridor stretches ahead, dimly lit by flickering torches.";

            if (lowerPrompt.Contains("inventory") || lowerPrompt.Contains("items"))
                return "You check your inventory and count your belongings.";

            if (lowerPrompt.Contains("rest") || lowerPrompt.Contains("sleep"))
                return "You take a short rest to recover your strength.";

            if (lowerPrompt.Contains("talk") || lowerPrompt.Contains("speak"))
                return "You attempt to communicate with the creature before you.";

            if (lowerPrompt.Contains("class") || lowerPrompt.Contains("fighter") || lowerPrompt.Contains("wizard"))
                return "Based on your description, you seem suited for the Fighter class - brave and strong in combat!";

            return "The Dungeon Master considers your actions... What would you like to do?";
        }

        public async Task<string> GenerateResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            // For mock, just use the user prompt
            return await GenerateResponseAsync(userPrompt, cancellationToken);
        }

        public async Task StreamResponseAsync(string prompt, Action<string> onTokenReceived, CancellationToken cancellationToken = default)
        {
            string response = await GenerateResponseAsync(prompt, cancellationToken);

            // Simulate streaming by sending word by word
            string[] words = response.Split(' ');
            foreach (string word in words)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                onTokenReceived?.Invoke(word + " ");
                await Task.Delay(50, cancellationToken);
            }
        }

        public void ClearHistory()
        {
            Debug.Log("[MockLLM] History cleared");
        }

        public async Task<LLMChatResult> ChatWithToolsAsync(
            IList<LLMChatMessage> messages,
            IList<LLMTool> tools,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(150, cancellationToken);

            // Find the last user message to seed deterministic behaviour
            string lastUser = "";
            for (int i = messages.Count - 1; i >= 0; i--)
                if (messages[i].Role == "user") { lastUser = messages[i].Content ?? ""; break; }
            string lower = lastUser.ToLowerInvariant();

            // Count prior assistant tool-call rounds in the message list to know which step we're on
            int priorToolRounds = 0;
            foreach (var m in messages)
                if (m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0) priorToolRounds++;

            // Deterministic mock script
            if (lower.Contains("attack") || lower.Contains("fight") || lower.Contains("kill"))
            {
                if (priorToolRounds == 0)
                    return ToolResult("call_mock_1", "SPAWN_ENEMY", "{\"name\":\"Goblin\",\"hp\":10,\"ac\":12}");
                if (priorToolRounds == 1)
                    return ToolResult("call_mock_2", "DAMAGE", "{\"target\":\"player\",\"amount\":2}");
                return TextResult("The Goblin lunges, you parry, and end the exchange breathing hard.");
            }

            if (lower.Contains("move") || lower.Contains("go ") || lower.Contains("walk") || lower.Contains("north") || lower.Contains("south") || lower.Contains("east") || lower.Contains("west"))
            {
                if (priorToolRounds == 0)
                {
                    string dir = "north";
                    if (lower.Contains("south")) dir = "south";
                    else if (lower.Contains("east")) dir = "east";
                    else if (lower.Contains("west")) dir = "west";
                    return ToolResult("call_mock_m", "MOVE", $"{{\"target\":\"player\",\"direction\":\"{dir}\"}}");
                }
                return TextResult("You stride forward, eyes scanning the path ahead.");
            }

            if (lower.Contains("heal") || lower.Contains("rest") || lower.Contains("bandage"))
            {
                if (priorToolRounds == 0)
                    return ToolResult("call_mock_h", "HEAL", "{\"target\":\"player\",\"amount\":5}");
                return TextResult("You catch your breath and tend to your wounds.");
            }

            // No matching keyword → pure narration, no tool calls
            return TextResult("The Dungeon Master watches you, awaiting your move.");
        }

        private static LLMChatResult ToolResult(string id, string name, string argsJson) =>
            new LLMChatResult { ToolCalls = new List<LLMToolCall> { new LLMToolCall { Id = id, Name = name, ArgumentsJson = argsJson } } };

        private static LLMChatResult TextResult(string text) =>
            new LLMChatResult { Text = text };
    }
}
