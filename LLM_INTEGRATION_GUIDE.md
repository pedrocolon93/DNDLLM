# LLM Integration Guide

This guide explains how to integrate different LLM providers into the D&D game.

## Table of Contents
1. [Mock LLM (Default)](#mock-llm)
2. [LLMUnity (Local Models)](#llmunity-integration)
3. [OpenAI GPT Integration](#openai-integration)
4. [Claude API Integration](#claude-integration)
5. [Custom Provider](#creating-custom-provider)

---

## Mock LLM

**Status**: ✅ Already Integrated (Default)

The Mock LLM is enabled by default for testing without API costs.

**Features:**
- Keyword-based responses
- Simulated API delays
- No external dependencies
- Perfect for development

**Limitations:**
- Not truly intelligent
- Limited response variety
- Cannot generate complex narratives

**Usage:**
```csharp
// In GameManager.cs
[SerializeField] private bool useMockLLM = true; // Already set to true
```

No additional setup required!

---

## LLMUnity Integration

**Status**: 🔧 Ready to Integrate

LLMUnity allows running LLMs locally on your machine without internet.

### Installation

**Option 1: Unity Package Manager (Recommended)**
1. Open Unity
2. Window > Package Manager
3. Click "+" > Add package from git URL
4. Enter: `https://github.com/undreamai/LLMUnity.git`
5. Click "Add"

**Option 2: Asset Store**
1. Download from [Unity Asset Store](https://assetstore.unity.com/)
2. Import into project

### Setup

1. **Create LLMUnity Provider Script**

Create `Assets/Scripts/AI/LLMUnityProvider.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using LLMUnity;

namespace DnD.AI
{
    public class LLMUnityProvider : MonoBehaviour, ILLMProvider
    {
        [SerializeField] private LLMCharacter llmCharacter;

        public string ProviderName => "LLMUnity";
        public bool IsReady { get; private set; }

        public async Task InitializeAsync()
        {
            if (llmCharacter == null)
            {
                Debug.LogError("LLMCharacter not assigned!");
                return;
            }

            // LLMCharacter handles its own initialization
            IsReady = true;
            Debug.Log("[LLMUnity] Ready!");
        }

        public async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (!IsReady || llmCharacter == null)
            {
                throw new InvalidOperationException("LLMUnity not initialized");
            }

            // LLMUnity uses callback pattern, wrap in Task
            var tcs = new TaskCompletionSource<string>();

            llmCharacter.Chat(prompt, (response) =>
            {
                tcs.SetResult(response);
            }, (error) =>
            {
                tcs.SetException(new Exception(error));
            });

            // Register cancellation
            cancellationToken.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }

        public async Task<string> GenerateResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            // LLMUnity sets system prompt separately
            llmCharacter.SetPrompt(systemPrompt);
            return await GenerateResponseAsync(userPrompt, cancellationToken);
        }

        public async Task StreamResponseAsync(string prompt, Action<string> onTokenReceived, CancellationToken cancellationToken = default)
        {
            if (!IsReady || llmCharacter == null)
            {
                throw new InvalidOperationException("LLMUnity not initialized");
            }

            var tcs = new TaskCompletionSource<bool>();

            llmCharacter.Chat(prompt,
                (response) =>
                {
                    onTokenReceived?.Invoke(response);
                    tcs.SetResult(true);
                },
                (error) =>
                {
                    tcs.SetException(new Exception(error));
                });

            cancellationToken.Register(() => tcs.TrySetCanceled());
            await tcs.Task;
        }

        public void ClearHistory()
        {
            if (llmCharacter != null)
            {
                llmCharacter.Clear();
            }
        }
    }
}
```

2. **Configure in Scene**

1. Create GameObject: "LLMUnitySystem"
2. Add Component: LLM Character (from LLMUnity)
3. Configure LLM Character:
   - Model: Download a model (7B recommended for PC)
   - Prompt: "You are a D&D Dungeon Master..."
   - Temperature: 0.7
   - Max Tokens: 256
4. Add Component: LLMUnityProvider
5. Assign LLM Character reference

3. **Update GameManager**

```csharp
// In GameManager.cs
[SerializeField] private bool useLLMUnity = false; // Set to true
[SerializeField] private LLMUnityProvider llmUnityProvider;

private async Task InitializeSystemsAsync()
{
    if (useLLMUnity && llmUnityProvider != null)
    {
        llmProvider = llmUnityProvider;
    }
    else if (useMockLLM)
    {
        llmProvider = new MockLLMProvider();
    }

    await llmProvider.InitializeAsync();
    // ... rest of initialization
}
```

### Model Recommendations

**For PC (8GB+ RAM):**
- Llama 2 7B Q4_K_M (4GB)
- Mistral 7B Q4_K_M (4GB)
- Phi-2 Q5_K_M (2GB) - Faster but less capable

**For Mobile:**
- TinyLlama 1.1B Q4_K_M (700MB)
- Phi-2 Q2_K (1GB)

**Download Models:**
- https://huggingface.co/TheBloke
- Use GGUF format

---

## OpenAI Integration

**Status**: 🔧 Ready to Integrate

### Installation

Install OpenAI Unity package:

**Option 1: Package Manager**
```
https://github.com/srcnalt/OpenAI-Unity.git#upm
```

**Option 2: Asset Store**
Download from Unity Asset Store

### Setup

1. **Get API Key**
   - Create account at https://platform.openai.com/
   - Generate API key
   - **DO NOT commit API keys to git!**

2. **Store API Key Securely**

Create `StreamingAssets/config.json`:
```json
{
  "openai_api_key": "sk-your-key-here"
}
```

Add to `.gitignore`:
```
StreamingAssets/config.json
```

3. **Create OpenAI Provider**

`Assets/Scripts/AI/OpenAIProvider.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using OpenAI;

namespace DnD.AI
{
    public class OpenAIProvider : MonoBehaviour, ILLMProvider
    {
        [SerializeField] private string model = "gpt-4o-mini"; // Cost-effective

        private OpenAIApi openai;

        public string ProviderName => "OpenAI";
        public bool IsReady { get; private set; }

        public async Task InitializeAsync()
        {
            string apiKey = LoadApiKey();

            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("OpenAI API key not found!");
                return;
            }

            var config = new OpenAIConfiguration
            {
                ApiKey = apiKey
            };

            openai = new OpenAIApi(config);
            IsReady = true;
            Debug.Log("[OpenAI] Ready!");
        }

        private string LoadApiKey()
        {
            // Option 1: Environment variable (most secure)
            string key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(key)) return key;

            // Option 2: Config file
            string configPath = Path.Combine(Application.streamingAssetsPath, "config.json");
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                var config = JsonUtility.FromJson<OpenAIConfig>(json);
                return config.openai_api_key;
            }

            return null;
        }

        public async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return await GenerateResponseAsync("", prompt, cancellationToken);
        }

        public async Task<string> GenerateResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            if (!IsReady)
            {
                throw new InvalidOperationException("OpenAI not initialized");
            }

            var messages = new List<ChatMessage>();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new ChatMessage
                {
                    Role = "system",
                    Content = systemPrompt
                });
            }

            messages.Add(new ChatMessage
            {
                Role = "user",
                Content = userPrompt
            });

            var request = new CreateChatCompletionRequest
            {
                Model = model,
                Messages = messages,
                MaxTokens = 256,
                Temperature = 0.7f
            };

            var response = await openai.CreateChatCompletion(request);

            return response.Choices[0].Message.Content;
        }

        public async Task StreamResponseAsync(string prompt, Action<string> onTokenReceived, CancellationToken cancellationToken = default)
        {
            // Implement streaming if needed
            string response = await GenerateResponseAsync(prompt, cancellationToken);
            onTokenReceived?.Invoke(response);
        }

        public void ClearHistory()
        {
            // OpenAI is stateless, history managed by game
        }

        [Serializable]
        private class OpenAIConfig
        {
            public string openai_api_key;
        }
    }
}
```

4. **Configure in GameManager**

Same process as LLMUnity - add component and assign reference.

### Cost Optimization

**Model Selection:**
- `gpt-4o-mini`: $0.15 per 1M input tokens (recommended)
- `gpt-4o`: $3.00 per 1M tokens (for important story moments)

**Strategies:**
1. Cache common responses
2. Use GPT-4o Mini for NPCs, GPT-4o for main story
3. Implement request throttling
4. Batch non-critical requests

---

## Claude Integration

**Status**: 🔧 Ready to Integrate

Similar to OpenAI, create `ClaudeProvider.cs` implementing `ILLMProvider`.

Use the `anthropic-sdk-dotnet` or make direct API calls.

**API Endpoint**: https://api.anthropic.com/v1/messages

**Models:**
- `claude-3-haiku`: Fastest, cheapest
- `claude-3-sonnet`: Balanced
- `claude-3-opus`: Most capable

---

## Creating Custom Provider

To add any LLM service:

1. **Implement ILLMProvider**

```csharp
public class MyCustomProvider : ILLMProvider
{
    public string ProviderName => "My LLM";
    public bool IsReady { get; private set; }

    public async Task InitializeAsync()
    {
        // Setup your LLM
        IsReady = true;
    }

    public async Task<string> GenerateResponseAsync(string prompt, CancellationToken ct)
    {
        // Call your LLM API
        return "Response from my LLM";
    }

    // Implement other interface methods...
}
```

2. **Register in GameManager**

```csharp
if (useCustomProvider)
{
    llmProvider = new MyCustomProvider();
}
```

3. **Done!**

All game systems will automatically use your provider through the interface.

---

## Provider Comparison

| Provider | Cost | Latency | Quality | Offline | Setup Difficulty |
|----------|------|---------|---------|---------|------------------|
| Mock | Free | Instant | Low | ✓ | Easy |
| LLMUnity | Free | Medium | Medium | ✓ | Medium |
| OpenAI | $$$ | Low | High | ✗ | Easy |
| Claude | $$$ | Low | High | ✗ | Easy |

## Recommendations

**For Development:**
- Use Mock LLM

**For Local Play:**
- Use LLMUnity with 7B model

**For Production (Online):**
- Use GPT-4o Mini or Claude Haiku
- Cache aggressively
- Implement rate limiting

**For Best Experience:**
- Hybrid: Local for common NPCs, Cloud for main story

---

## Troubleshooting

### LLMUnity model not loading
- Check model format is GGUF
- Verify sufficient RAM
- Try smaller model

### OpenAI timeout
- Increase timeout in CancellationToken (60s)
- Check internet connection
- Verify API key is valid

### High API costs
- Implement response caching
- Use cheaper models (GPT-4o Mini)
- Limit max tokens (256 recommended)

### Slow responses
- Use streaming for better UX
- Show "thinking" indicator
- Pre-generate content during loading

---

## Next Steps

1. Choose your LLM provider
2. Follow the setup guide above
3. Test with simple prompts
4. Optimize for your use case
5. Deploy and enjoy!

For questions, check the [main README](README.md) or open an issue on GitHub.

Happy adventuring! 🎲✨
