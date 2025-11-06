using System;
using System.Threading;
using System.Threading.Tasks;

namespace DnD.AI
{
    /// <summary>
    /// Interface for LLM providers (OpenAI, Claude, Local models, etc.)
    /// Implements Strategy pattern for runtime LLM switching
    /// </summary>
    public interface ILLMProvider
    {
        /// <summary>
        /// Provider name (e.g., "OpenAI", "LLMUnity", "Claude")
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Check if provider is ready to use
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Send a prompt and get a response
        /// </summary>
        Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send a prompt with system instructions
        /// </summary>
        Task<string> GenerateResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stream response for real-time display
        /// </summary>
        Task StreamResponseAsync(string prompt, Action<string> onTokenReceived, CancellationToken cancellationToken = default);

        /// <summary>
        /// Initialize the provider (load models, set API keys, etc.)
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Clear conversation history
        /// </summary>
        void ClearHistory();
    }
}
