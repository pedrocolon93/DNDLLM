using System;
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
    }
}
