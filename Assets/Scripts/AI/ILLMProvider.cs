using System;
using System.Collections.Generic;
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
        string ProviderName { get; }
        bool IsReady { get; }

        Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default);
        Task<string> GenerateResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
        Task StreamResponseAsync(string prompt, Action<string> onTokenReceived, CancellationToken cancellationToken = default);
        Task InitializeAsync();
        void ClearHistory();

        /// <summary>
        /// Tool-aware chat: send a message list plus a tool catalogue, get back either a final text reply
        /// or one or more tool calls. The caller dispatches tools, appends tool-result messages, and loops.
        /// </summary>
        Task<LLMChatResult> ChatWithToolsAsync(
            IList<LLMChatMessage> messages,
            IList<LLMTool> tools,
            CancellationToken cancellationToken = default);
    }

    /// <summary>One tool the LLM may invoke. parametersJsonSchema is a literal JSON Schema string.</summary>
    public sealed class LLMTool
    {
        public string Name;
        public string Description;
        public string ParametersJsonSchema;

        public LLMTool(string name, string description, string parametersJsonSchema)
        {
            Name = name;
            Description = description;
            ParametersJsonSchema = parametersJsonSchema;
        }
    }

    /// <summary>One tool invocation emitted by the model. ArgumentsJson is the raw JSON object string.</summary>
    public sealed class LLMToolCall
    {
        public string Id;
        public string Name;
        public string ArgumentsJson;
    }

    /// <summary>A single chat message. Role: "system" | "user" | "assistant" | "tool".</summary>
    public sealed class LLMChatMessage
    {
        public string Role;
        public string Content;
        public List<LLMToolCall> ToolCalls; // assistant turns only
        public string ToolCallId;           // tool turns only - matches the assistant's call id
        public string Name;                 // optional, for "tool" role
        public string ImageDataUrl;         // user turns only - "data:image/png;base64,…" attachment

        public static LLMChatMessage System(string content)    => new LLMChatMessage { Role = "system",    Content = content };
        public static LLMChatMessage User(string content)      => new LLMChatMessage { Role = "user",      Content = content };
        public static LLMChatMessage Assistant(string content) => new LLMChatMessage { Role = "assistant", Content = content };
        public static LLMChatMessage UserWithImage(string content, string imageDataUrl) => new LLMChatMessage
        {
            Role = "user",
            Content = content,
            ImageDataUrl = imageDataUrl,
        };
        public static LLMChatMessage AssistantToolCalls(List<LLMToolCall> calls) => new LLMChatMessage
        {
            Role = "assistant",
            Content = null,
            ToolCalls = calls,
        };
        public static LLMChatMessage Tool(string toolCallId, string name, string content) => new LLMChatMessage
        {
            Role = "tool",
            Content = content,
            ToolCallId = toolCallId,
            Name = name,
        };
    }

    /// <summary>Result of one ChatWithToolsAsync round. Either Text is set OR ToolCalls is non-empty.</summary>
    public sealed class LLMChatResult
    {
        public string Text;
        public List<LLMToolCall> ToolCalls = new List<LLMToolCall>();

        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
    }
}
