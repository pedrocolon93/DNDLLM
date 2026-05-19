// Assets/Scripts/AI/LLMServiceProvider.cs
//
// Bridges the ILLMProvider interface (used by DungeonMaster / CommandParser) to the
// concrete LLMService MonoBehaviour. The Provider abstraction predates LLMService; this
// adapter lets us delete the dead "fall back to Mock when useMockLLM=false" branch in
// GameManager without rewriting every consumer.
//
// Streaming is faked (one chunk = full response) because LLMService doesn't expose token
// streaming. The DM only uses StreamResponseAsync when useStreamingResponses is true; the
// caller still sees the full text either way.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DnD.AI
{
    public sealed class LLMServiceProvider : ILLMProvider
    {
        public string ProviderName => "LLMService";
        public bool IsReady { get; private set; }

        public Task InitializeAsync()
        {
            // LLMService is configured in the Inspector; nothing to await.
            IsReady = DNDLLM.Services.LLMService.Instance != null;
            if (!IsReady)
                Debug.LogWarning("[LLMServiceProvider] LLMService.Instance is null — DungeonMaster narration will fail until the scene has one.");
            return Task.CompletedTask;
        }

        public async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default)
            => await GenerateResponseAsync("", prompt, cancellationToken);

        public async Task<string> GenerateResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            var svc = DNDLLM.Services.LLMService.Instance;
            if (svc == null) return "[LLMServiceProvider] No LLMService.";
            string s = await svc.SendPrompt(systemPrompt ?? "", userPrompt ?? "");
            return (s ?? "").TrimStart();
        }

        public async Task StreamResponseAsync(string prompt, Action<string> onTokenReceived, CancellationToken cancellationToken = default)
        {
            string r = await GenerateResponseAsync(prompt, cancellationToken);
            onTokenReceived?.Invoke(r);
        }

        public void ClearHistory() { /* stateless — DungeonMaster owns the rolling history */ }

        public async Task<LLMChatResult> ChatWithToolsAsync(
            IList<LLMChatMessage> messages,
            IList<LLMTool> tools,
            CancellationToken cancellationToken = default)
        {
            var svc = DNDLLM.Services.LLMService.Instance;
            if (svc == null) return new LLMChatResult { Text = "[LLMServiceProvider] No LLMService." };
            return await svc.ChatWithToolsAsync(messages, tools, cancellationToken);
        }
    }
}
