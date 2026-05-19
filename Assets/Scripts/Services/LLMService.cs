using UnityEngine;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using DnD.AI;

namespace DNDLLM.Services
{
    public enum LLMProvider { OpenRouter, Ollama, Local }

    public class LLMService : MonoBehaviour
    {
        public static LLMService Instance { get; private set; }

        [Header("Provider")]
        [SerializeField] private LLMProvider provider = LLMProvider.Local;
        [SerializeField] private bool useMock = false;

        [Header("Debug sprites (skip remote image generation)")]
        [Tooltip("When ON: every image-generation call returns a procedural colored shape via DebugSpriteFactory. Default ON for fast iteration.")]
        public bool useDebugSprites = true;

        [Header("OpenRouter")]
        [SerializeField] private string apiKey = "sk-or-v1-YOUR_KEY_HERE";
        public string ApiKey => apiKey;
        [SerializeField] private string model = "openai/gpt-4o-mini";
        [SerializeField] private string imageModel = "openai/dall-e-3";

        [Header("Ollama  (text only — images still use OpenRouter)")]
        [SerializeField] private string ollamaBaseUrl = "http://localhost:11434";
        [SerializeField] private string ollamaModel = "llama3.2";

        [Header("Local OpenAI-compatible (e.g. osaurus on 127.0.0.1:1337)")]
        [SerializeField] private string localBaseUrl = "http://127.0.0.1:1337";
        [SerializeField] private string localApiKey  = "";
        [SerializeField] private string localModel   = "qwen3.6-35b-a3b-mxfp4";

        [Header("Cache")]
        [SerializeField] private bool useCache = true;

        [Header("Strategy D — vision evaluator")]
        [SerializeField] private string evalModel = "openai/gpt-4o-mini"; // any vision-capable OpenRouter model

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else Destroy(gameObject);
        }

        [System.Serializable]
        public class OpenRouterRequest
        {
            public string model;
            public List<Message> messages;
            public List<string> modalities; // "text", "image"
        }



        [System.Serializable]
        public class Message
        {
            public string role;
            public string content;
            public List<MessageImage> images; // OpenRouter specific
        }

        [System.Serializable]
        public class MessageImage
        {
            public string type;
            public ImageUrl image_url;
        }

        [System.Serializable]
        public class ImageUrl
        {
            public string url;
        }

        [System.Serializable]
        public class OpenRouterResponse
        {
            public List<Choice> choices;
        }

        [System.Serializable]
        public class Choice
        {
            public Message message;
        }

        [System.Serializable]
        public class ImageGenerationRequest
        {
            public string model;
            public string prompt;
            public int n = 1;
            public string size = "1024x1024";
        }

        [System.Serializable]
        public class ImageGenerationResponse
        {
            public List<ImageData> data;
        }

        [System.Serializable]
        public class ImageData
        {
            public string url;
        }

        public async Task<string> SendPrompt(string systemPrompt, string userPrompt)
        {
            if (useMock)
            {
                await Task.Delay(500);
                return $"[MOCK] {userPrompt}";
            }

            var messages = new List<Message>
            {
                new Message { role = "system", content = systemPrompt },
                new Message { role = "user",   content = userPrompt }
            };

            if (provider == LLMProvider.Ollama)
            {
                string url = $"{ollamaBaseUrl.TrimEnd('/')}/v1/chat/completions";
                return await SendChatCompletionAsync(url, ollamaModel, messages, authToken: null);
            }
            else if (provider == LLMProvider.Local)
            {
                string url = $"{localBaseUrl.TrimEnd('/')}/v1/chat/completions";
                return await SendChatCompletionAsync(url, localModel, messages,
                    authToken: string.IsNullOrEmpty(localApiKey) ? null : localApiKey);
            }
            else
            {
                return await SendChatCompletionAsync(
                    "https://openrouter.ai/api/v1/chat/completions",
                    model, messages, authToken: apiKey);
            }
        }

        // Shared chat-completion helper — used by both OpenRouter and Ollama paths
        private async Task<string> SendChatCompletionAsync(
            string url, string modelName, List<Message> messages, string authToken)
        {
            var requestData = new OpenRouterRequest { model = modelName, messages = messages };
            string jsonData = JsonUtility.ToJson(requestData);

            using (var request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
                request.uploadHandler   = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrEmpty(authToken))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {authToken}");
                    request.SetRequestHeader("HTTP-Referer", "https://github.com/google-deepmind/antigravity");
                    request.SetRequestHeader("X-Title", "DNDLLM");
                }

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[LLMService] Chat response: {request.downloadHandler.text}");
                    var responseData = JsonUtility.FromJson<OpenRouterResponse>(request.downloadHandler.text);
                    if (responseData?.choices?.Count > 0)
                        return responseData.choices[0].message.content;
                }
                else
                {
                    Debug.LogError($"[LLMService] Chat error ({url}): {request.error} — {request.downloadHandler.text}");
                }
            }

            return "Error calling LLM API.";
        }

        public async Task<Texture2D> GenerateImage(string prompt)
        {
            if (useDebugSprites)
            {
                await Task.Yield();
                return DNDLLM.Utils.DebugSpriteFactory.MakeTile(prompt, prompt, 128);
            }
            if (useCache)
            {
                Texture2D cachedTex = ImageCache.Load(prompt);
                if (cachedTex != null)
                {
                    Debug.Log($"[LLMService] Loaded image from cache for prompt: {prompt}");
                    return cachedTex;
                }
            }
            if (useMock)
            {
                await Task.Delay(1000); 
                Texture2D tex = new Texture2D(256, 256);
                Color randomColor = new Color(Random.value, Random.value, Random.value);
                for (int x = 0; x < 256; x++)
                {
                    for (int y = 0; y < 256; y++)
                    {
                        tex.SetPixel(x, y, randomColor);
                    }
                }
                tex.Apply();
                return tex;
            }

            // Google Gemini Image Generation (via Chat Completions)
            if (this.imageModel.ToLower().StartsWith("google/"))
            {
                Debug.Log($"Generating Image with Gemini: {this.imageModel}");
                string chatUrl = "https://openrouter.ai/api/v1/chat/completions";
                
                var requestData = new OpenRouterRequest
                {
                    model = this.imageModel,
                    messages = new List<Message>
                    {
                        new Message { role = "user", content = prompt }
                    },
                    modalities = new List<string> { "image", "text" }
                };

                string jsonData = JsonUtility.ToJson(requestData);

                using (UnityEngine.Networking.UnityWebRequest request = new UnityEngine.Networking.UnityWebRequest(chatUrl, "POST"))
                {
                    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
                    request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                    request.SetRequestHeader("HTTP-Referer", "https://github.com/google-deepmind/antigravity");
                    request.SetRequestHeader("X-Title", "DNDLLM");

                    var operation = request.SendWebRequest();
                    while (!operation.isDone) await Task.Yield();

                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"Gemini Image Response: {request.downloadHandler.text}");
                        var responseData = JsonUtility.FromJson<OpenRouterResponse>(request.downloadHandler.text);
                        if (responseData != null && responseData.choices != null && responseData.choices.Count > 0)
                        {
                            var msg = responseData.choices[0].message;
                            
                            // Check for structured images field first (Official OpenRouter spec)
                            if (msg.images != null && msg.images.Count > 0)
                            {
                                string imgUrl = msg.images[0].image_url.url;
                                Texture2D tex = ParseBase64Image(imgUrl);
                                if (tex != null && useCache) ImageCache.Save(prompt, tex);
                                return tex;
                            }

                            // Fallback
                            Texture2D fallbackTex = ParseBase64Image(msg.content);
                            if (fallbackTex != null && useCache) ImageCache.Save(prompt, fallbackTex);
                            return fallbackTex;
                        }
                    }
                    else
                    {
                        Debug.LogError($"Gemini Image Error: {request.error} - {request.downloadHandler.text}");
                    }
                }
                return null;
            }

            // Standard OpenAI-compatible Image Generation (DALL-E etc.)
            string url = "https://openrouter.ai/api/v1/images/generations";
            
            var reqData = new ImageGenerationRequest
            {
                model = this.imageModel,
                prompt = prompt
            };

            string json = JsonUtility.ToJson(reqData);

            using (UnityEngine.Networking.UnityWebRequest request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                request.SetRequestHeader("HTTP-Referer", "https://github.com/google-deepmind/antigravity");
                request.SetRequestHeader("X-Title", "DNDLLM");

                var operation = request.SendWebRequest();

                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.Log($"Image Gen Response: {request.downloadHandler.text}");
                    var responseData = JsonUtility.FromJson<ImageGenerationResponse>(request.downloadHandler.text);
                    if (responseData != null && responseData.data != null && responseData.data.Count > 0)
                    {
                        string imageUrl = responseData.data[0].url;
                        Texture2D tex = await DownloadTexture(imageUrl);
                        if (tex != null && useCache) ImageCache.Save(prompt, tex);
                        return tex;
                    }
                }
                else
                {
                    Debug.LogError($"Image Gen Error: {request.error} - {request.downloadHandler.text}");
                }
            }
            return null;
        }

        /// <summary>
        /// Generates a tile image using Gemini multimodal: sends the style anchor image as a
        /// reference so all tiles share the same art style. No disk caching — every tile is unique.
        /// </summary>
        public async Task<Texture2D> GenerateStyledTile(string tilePrompt, Texture2D styleAnchor)
        {
            if (useDebugSprites)
            {
                await Task.Yield();
                return DNDLLM.Utils.DebugSpriteFactory.MakeTile(tilePrompt, tilePrompt, 64);
            }
            if (useCache)
            {
                Texture2D cachedTex = ImageCache.Load(tilePrompt);
                if (cachedTex != null)
                {
                    Debug.Log($"[LLMService] StyledTile loaded from cache.");
                    return cachedTex;
                }
            }

            if (useMock)
            {
                await Task.Delay(100);
                Texture2D tex = new Texture2D(64, 64);
                Color col = new Color(0.55f, 0.5f, 0.45f); // floor default
                if (tilePrompt.Contains("wall"))    col = new Color(0.35f, 0.3f, 0.25f);
                if (tilePrompt.Contains("door"))    col = new Color(0.55f, 0.38f, 0.18f);
                if (tilePrompt.Contains("chest"))   col = new Color(0.75f, 0.65f, 0.1f);
                if (tilePrompt.Contains("monster") || tilePrompt.Contains("lair")) col = new Color(0.5f, 0.1f, 0.1f);
                if (tilePrompt.Contains("portal")  || tilePrompt.Contains("exit")) col = new Color(0.25f, 0.1f, 0.75f);
                Color[] pixels = new Color[64 * 64];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }

            if (!this.imageModel.ToLower().StartsWith("google/"))
                return await GenerateImage(tilePrompt);

            // Build multimodal content array: [image_url part, text part]
            string escapedPrompt = EscapeJson(tilePrompt);
            string contentArray;

            if (styleAnchor != null)
            {
                byte[] pngBytes = styleAnchor.EncodeToPNG();
                string base64 = System.Convert.ToBase64String(pngBytes);
                // base64 alphabet is URL-safe; no JSON escaping needed for the data
                contentArray = "[{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,"
                             + base64
                             + "\"}},{\"type\":\"text\",\"text\":\""
                             + escapedPrompt + "\"}]";
            }
            else
            {
                contentArray = "[{\"type\":\"text\",\"text\":\"" + escapedPrompt + "\"}]";
            }

            string requestJson = "{\"model\":\"" + EscapeJson(this.imageModel)
                               + "\",\"messages\":[{\"role\":\"user\",\"content\":"
                               + contentArray
                               + "}],\"modalities\":[\"image\",\"text\"]}";

            string chatUrl = "https://openrouter.ai/api/v1/chat/completions";
            using (var request = new UnityEngine.Networking.UnityWebRequest(chatUrl, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(requestJson);
                request.uploadHandler   = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type",  "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                request.SetRequestHeader("HTTP-Referer",  "https://github.com/google-deepmind/antigravity");
                request.SetRequestHeader("X-Title",       "DNDLLM");

                var op = request.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    string raw = request.downloadHandler.text;
                    Debug.Log($"[LLMService] StyledTile response (first 300 chars): {raw.Substring(0, Mathf.Min(300, raw.Length))}");
                    var responseData = JsonUtility.FromJson<OpenRouterResponse>(raw);
                    if (responseData?.choices?.Count > 0)
                    {
                        var msg = responseData.choices[0].message;
                        if (msg.images != null && msg.images.Count > 0)
                        {
                            Texture2D tex = ParseBase64Image(msg.images[0].image_url.url);
                            if (tex != null && useCache) ImageCache.Save(tilePrompt, tex);
                            return tex;
                        }
                        Texture2D fallback = ParseBase64Image(msg.content);
                        if (fallback != null && useCache) ImageCache.Save(tilePrompt, fallback);
                        return fallback;
                    }
                }
                else
                {
                    Debug.LogError($"[LLMService] StyledTile error: {request.error} — {request.downloadHandler.text}");
                }
            }
            return null;
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"",  "\\\"")
                    .Replace("\n",  "\\n")
                    .Replace("\r",  "\\r")
                    .Replace("\t",  "\\t");
        }

        // ── Tool-aware chat (OpenAI-compatible function calling) ─────────────

        [System.Serializable] private class ToolFunctionDto { public string name; public string arguments; }
        [System.Serializable] private class ToolCallDto     { public string id; public string type; public ToolFunctionDto function; }
        [System.Serializable] private class MessageWithToolsDto { public string role; public string content; public List<ToolCallDto> tool_calls; }
        [System.Serializable] private class ChoiceWithToolsDto  { public MessageWithToolsDto message; }
        [System.Serializable] private class ResponseWithToolsDto { public List<ChoiceWithToolsDto> choices; }

        public async Task<LLMChatResult> ChatWithToolsAsync(
            IList<LLMChatMessage> messages,
            IList<LLMTool> tools,
            CancellationToken ct = default)
        {
            string url, modelName, authToken;
            if (provider == LLMProvider.Ollama)
            {
                url = $"{ollamaBaseUrl.TrimEnd('/')}/v1/chat/completions";
                modelName = ollamaModel;
                authToken = null;
            }
            else if (provider == LLMProvider.Local)
            {
                url = $"{localBaseUrl.TrimEnd('/')}/v1/chat/completions";
                modelName = localModel;
                authToken = string.IsNullOrEmpty(localApiKey) ? null : localApiKey;
            }
            else
            {
                url = "https://openrouter.ai/api/v1/chat/completions";
                modelName = model;
                authToken = apiKey;
            }

            string body = BuildChatToolsRequestJson(modelName, messages, tools);

            using (var request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
                request.uploadHandler   = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrEmpty(authToken))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {authToken}");
                    request.SetRequestHeader("HTTP-Referer", "https://github.com/google-deepmind/antigravity");
                    request.SetRequestHeader("X-Title", "DNDLLM");
                }

                var op = request.SendWebRequest();
                while (!op.isDone)
                {
                    if (ct.IsCancellationRequested) { request.Abort(); ct.ThrowIfCancellationRequested(); }
                    await Task.Yield();
                }

                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[LLMService] Tool chat error ({url}): {request.error} — {request.downloadHandler.text}");
                    return new LLMChatResult { Text = "Error calling LLM API." };
                }

                string raw = request.downloadHandler.text;
                Debug.Log($"[LLMService] Tool chat response: {raw}");
                return ParseChatToolsResponse(raw);
            }
        }

        private static string BuildChatToolsRequestJson(string modelName, IList<LLMChatMessage> messages, IList<LLMTool> tools)
        {
            var sb = new StringBuilder(1024);
            sb.Append('{');
            sb.Append("\"model\":\"").Append(EscapeJson(modelName)).Append('"');

            // messages
            sb.Append(",\"messages\":[");
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendMessage(sb, messages[i]);
            }
            sb.Append(']');

            // tools
            if (tools != null && tools.Count > 0)
            {
                sb.Append(",\"tools\":[");
                for (int i = 0; i < tools.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var t = tools[i];
                    sb.Append("{\"type\":\"function\",\"function\":{\"name\":\"")
                      .Append(EscapeJson(t.Name))
                      .Append("\",\"description\":\"")
                      .Append(EscapeJson(t.Description))
                      .Append("\",\"parameters\":")
                      .Append(t.ParametersJsonSchema)
                      .Append("}}");
                }
                sb.Append(']');
                sb.Append(",\"tool_choice\":\"auto\"");
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendMessage(StringBuilder sb, LLMChatMessage m)
        {
            sb.Append('{');
            sb.Append("\"role\":\"").Append(EscapeJson(m.Role)).Append('"');

            if (m.Role == "tool")
            {
                sb.Append(",\"tool_call_id\":\"").Append(EscapeJson(m.ToolCallId ?? "")).Append('"');
                if (!string.IsNullOrEmpty(m.Name))
                    sb.Append(",\"name\":\"").Append(EscapeJson(m.Name)).Append('"');
                sb.Append(",\"content\":\"").Append(EscapeJson(m.Content ?? "")).Append('"');
            }
            else if (m.ToolCalls != null && m.ToolCalls.Count > 0)
            {
                // assistant-with-tool-calls: content may be null
                if (m.Content != null)
                    sb.Append(",\"content\":\"").Append(EscapeJson(m.Content)).Append('"');
                else
                    sb.Append(",\"content\":null");

                sb.Append(",\"tool_calls\":[");
                for (int j = 0; j < m.ToolCalls.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    var c = m.ToolCalls[j];
                    sb.Append("{\"id\":\"").Append(EscapeJson(c.Id))
                      .Append("\",\"type\":\"function\",\"function\":{\"name\":\"")
                      .Append(EscapeJson(c.Name))
                      .Append("\",\"arguments\":\"")
                      .Append(EscapeJson(c.ArgumentsJson ?? "{}"))
                      .Append("\"}}");
                }
                sb.Append(']');
            }
            else if (!string.IsNullOrEmpty(m.ImageDataUrl))
            {
                // Multimodal user content: array of [text part, image_url part]. The base64 inside
                // the URL only needs JSON-string-level escaping for quotes/backslashes — the data
                // itself uses a URL-safe alphabet — but EscapeJson is cheap and safe to apply.
                sb.Append(",\"content\":[")
                  .Append("{\"type\":\"text\",\"text\":\"").Append(EscapeJson(m.Content ?? "")).Append("\"},")
                  .Append("{\"type\":\"image_url\",\"image_url\":{\"url\":\"").Append(EscapeJson(m.ImageDataUrl)).Append("\"}}")
                  .Append(']');
            }
            else
            {
                sb.Append(",\"content\":\"").Append(EscapeJson(m.Content ?? "")).Append('"');
            }

            sb.Append('}');
        }

        // ── Strategy D: holistic map generation + evaluate/refine loop ────

        /// <summary>
        /// Asks the LLM to distribute story elements across an NxN logical grid.
        /// Returns a LogicalGrid with terrain_type/feature/description per cell.
        /// </summary>
        public async Task<DNDLLM.Map.LogicalGrid> GenerateLogicalGridAsync(int size, string story)
        {
            string sys = "You are a DND map architect. You output ONLY valid JSON matching the requested schema — no prose, no markdown fences.";
            string usr =
                $"Create a logical {size}x{size} grid for this story:\n{story}\n\n" +
                $"Distribute the major elements (buildings, terrain changes, points of interest) across tiles logically. " +
                $"Adjacent tiles must flow naturally into one another (no abrupt biome changes).\n\n" +
                $"Output JSON of the form:\n" +
                $"{{\"size\": {size}, \"tiles\": [ {{\"x\":0,\"y\":0,\"terrain_type\":\"grass\",\"feature\":\"tavern\",\"description\":\"A bustling tavern\"}}, ... ]}}\n\n" +
                $"Rules:\n" +
                $"- Include EVERY cell from (0,0) to ({size-1},{size-1}). That is exactly {size*size} tiles.\n" +
                $"- terrain_type is one short keyword: grass, dirt, stone, water, sand, wood, cobble, wall, cliff, void.\n" +
                $"- feature is a short noun (tavern, monastery, armory, well, statue, tree, ...) or empty string if none.\n" +
                $"- description is one short sentence for the image generator.";
            string raw = await SendPrompt(sys, usr);
            if (string.IsNullOrEmpty(raw)) return null;

            // Strip markdown fences if the model included them despite the system instruction.
            raw = raw.Trim();
            if (raw.StartsWith("```"))
            {
                int firstNl = raw.IndexOf('\n');
                if (firstNl >= 0) raw = raw.Substring(firstNl + 1);
                int fence = raw.LastIndexOf("```");
                if (fence >= 0) raw = raw.Substring(0, fence);
                raw = raw.Trim();
            }

            try
            {
                return JsonUtility.FromJson<DNDLLM.Map.LogicalGrid>(raw);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LLMService] LogicalGrid parse failed: {e.Message}\nRaw: {raw}");
                return null;
            }
        }

        /// <summary>
        /// Generates ONE cohesive isometric map image for the entire NxN grid (Strategy D, phase 2).
        /// Mirrors the meta-prompt from DndMapGenerator/main.py:generate_holistic_map.
        /// </summary>
        public async Task<Texture2D> GenerateHolisticMapAsync(DNDLLM.Map.LogicalGrid grid, bool gridMode)
        {
            if (grid == null || grid.tiles == null || grid.tiles.Count == 0) return null;

            int size = grid.size;
            if (useDebugSprites)
            {
                await Task.Yield();
                return RenderLogicalGridAsTexture(grid, 64);
            }
            var sb = new StringBuilder(2048);
            if (gridMode)
            {
                sb.Append($"Generate a single, seamless isometric fantasy DND battlemap image divided into a {size}x{size} grid of tiles. ");
                sb.Append("The entire image must be rendered as ONE unified painting — each section blends naturally into its neighbors through continuous terrain, consistent lighting, and matching ground textures.\n\n");
                sb.Append("CRITICAL RENDERING RULES:\n");
                sb.Append($"- The output image MUST be perfectly square. Each of the {size}x{size} tiles must occupy an equal square portion of the canvas.\n");
                sb.Append("- Use a consistent isometric 3/4 view angle across the ENTIRE image.\n");
                sb.Append("- Consistent global lighting: single soft light source from the top-left.\n");
                sb.Append("- Ground terrain must flow seamlessly across tile edges.\n");
                sb.Append("- AVOID drawing visible grid lines, borders, or tile boundaries between sections.\n");
                sb.Append("- Consistent art style and scale throughout.\n");
                sb.Append("- Painterly hand-illustrated DND map aesthetic (Forgotten Realms style).\n\n");
                sb.Append("GRID LAYOUT (column x, row y — origin top-left):\n");
                foreach (var t in grid.tiles)
                {
                    string feat = string.IsNullOrEmpty(t.feature) ? "" : $" with a {t.feature}";
                    sb.Append($"Tile({t.x},{t.y}): {t.terrain_type}{feat}. {t.description}\n");
                }
                sb.Append("\nThe final image should look like a single cohesive isometric map illustration that a DM would hand players at the table.");
            }
            else
            {
                sb.Append("Paint a single continuous isometric fantasy village illustration for a tabletop RPG. ");
                sb.Append("The scene depicts a hilltop village viewed from a 3/4 bird's-eye angle. ");
                sb.Append("This must be ONE seamless painting — imagine an artist painting the entire scene in a single session on one canvas, with terrain, paths, and grass flowing naturally throughout. No borders, no panels, no dividing lines of any kind.\n\n");
                sb.Append("Layout by region:\n");
                foreach (var t in grid.tiles)
                {
                    string col = t.x == 0 ? "left" : (t.x == size / 2 ? "center" : "right");
                    string row = t.y == 0 ? "upper" : (t.y == size / 2 ? "middle" : "lower");
                    string feat = string.IsNullOrEmpty(t.feature) ? "" : $" with a {t.feature}";
                    sb.Append($"{row}-{col}: {t.terrain_type}{feat}. {t.description}\n");
                }
                sb.Append("\nArt direction: Hand-painted isometric view with warm afternoon lighting from the top-left casting soft shadows to the bottom-right. ");
                sb.Append("Forgotten Realms / Pathfinder adventure map aesthetic. ");
                sb.Append("All structures at the same consistent scale and perspective angle. ");
                sb.Append("Dirt paths and grass connect naturally between areas. ");
                sb.Append("Fill the ENTIRE square canvas edge-to-edge with terrain — extend to every border of the image. ");
                sb.Append("No empty sky margins. One cohesive painting — NOT a collage of separate panels.");
            }
            return await GenerateImage(sb.ToString());
        }

        /// <summary>
        /// Sends a rendered map image to a vision-capable LLM and asks for a short revision instruction
        /// (or "PERFECT" if no fixes are needed). Returns the raw instruction text.
        /// </summary>
        /// <summary>Renders a LogicalGrid as a procedurally-coloured composite Texture2D for debug mode.</summary>
        private Texture2D RenderLogicalGridAsTexture(DNDLLM.Map.LogicalGrid grid, int cellPx)
        {
            int size = grid.size;
            int dim = size * cellPx;
            var tex = new Texture2D(dim, dim, TextureFormat.RGBA32, false);
            var fill = DNDLLM.Utils.DebugSpriteFactory.ColorForTerrain("stone");
            var px = new Color[dim * dim];
            for (int i = 0; i < px.Length; i++) px[i] = fill;
            tex.SetPixels(px);
            foreach (var t in grid.tiles)
            {
                if (t == null) continue;
                Color c = DNDLLM.Utils.DebugSpriteFactory.ColorForTerrain(t.terrain_type);
                int gx = t.x * cellPx;
                int gy = (size - 1 - t.y) * cellPx; // y inverted so (0,0) renders at top-left
                for (int yy = 0; yy < cellPx; yy++)
                for (int xx = 0; xx < cellPx; xx++) tex.SetPixel(gx + xx, gy + yy, c);
                var (shape, col) = DNDLLM.Utils.DebugSpriteFactory.BadgeForFeature(t.feature);
                if (shape != DNDLLM.Utils.DebugSpriteFactory.Shape.None)
                    DNDLLM.Utils.DebugSpriteFactory.DrawShape(tex,
                        gx + cellPx / 2, gy + cellPx / 2, cellPx / 3, shape, col);
                // cell border
                for (int i = 0; i < cellPx; i++)
                {
                    tex.SetPixel(gx + i, gy, Color.black);
                    tex.SetPixel(gx + i, gy + cellPx - 1, Color.black);
                    tex.SetPixel(gx, gy + i, Color.black);
                    tex.SetPixel(gx + cellPx - 1, gy + i, Color.black);
                }
            }
            tex.Apply();
            return tex;
        }

        public async Task<string> EvaluateMapImageAsync(Texture2D image, DNDLLM.Map.LogicalGrid grid)
        {
            if (useDebugSprites) { await Task.Yield(); return "PERFECT"; }
            if (image == null) return "";
            byte[] png = image.EncodeToPNG();
            string b64 = System.Convert.ToBase64String(png);

            var expected = new StringBuilder();
            int size = grid?.size ?? 0;
            if (grid?.tiles != null)
                foreach (var t in grid.tiles)
                    if (!string.IsNullOrEmpty(t.feature))
                        expected.Append($"  - At grid ({t.x},{t.y}): {t.feature} — {t.description}\n");

            string promptText =
                $"You are reviewing a {size}x{size} isometric DND battlemap painting.\n\n" +
                $"Expected features:\n{expected}\n\n" +
                "Check for:\n" +
                "- Missing or unrecognizable features\n" +
                "- Visible seams, black lines, or tile boundaries\n" +
                "- Terrain that doesn't flow naturally\n" +
                "- Inconsistent art style or lighting\n" +
                "- Stairs leading nowhere, structures floating, paths dead-ending\n" +
                "- Scale mismatches between people, buildings, objects\n\n" +
                "Reply with ONLY a short instruction (2-3 sentences) describing what to fix. " +
                "Do NOT explain what you see or list what is correct. " +
                "If nothing needs fixing, reply with exactly: PERFECT";

            // Hand-built JSON: text + image_url content array. Same shape as GenerateStyledTile.
            string requestJson =
                "{\"model\":\"" + EscapeJson(evalModel) + "\"," +
                "\"messages\":[{\"role\":\"user\",\"content\":[" +
                "{\"type\":\"text\",\"text\":\"" + EscapeJson(promptText) + "\"}," +
                "{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64," + b64 + "\"}}" +
                "]}]}";

            string url = "https://openrouter.ai/api/v1/chat/completions";
            using (var request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);
                request.uploadHandler   = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type",  "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                request.SetRequestHeader("HTTP-Referer",  "https://github.com/google-deepmind/antigravity");
                request.SetRequestHeader("X-Title",       "DNDLLM");

                var op = request.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[LLMService] Map eval error: {request.error} — {request.downloadHandler.text}");
                    return "";
                }

                var responseData = JsonUtility.FromJson<OpenRouterResponse>(request.downloadHandler.text);
                if (responseData?.choices?.Count > 0)
                    return (responseData.choices[0].message.content ?? "").Trim();
            }
            return "";
        }

        /// <summary>
        /// Sends a rendered map image + a short revision instruction to the image model and gets back
        /// a refined map image. Uses the Gemini multimodal path (image+text → image).
        /// </summary>
        public async Task<Texture2D> RefineMapImageAsync(Texture2D image, string feedback)
        {
            if (useDebugSprites) { await Task.Yield(); return image; }
            if (image == null || string.IsNullOrEmpty(feedback)) return image;

            string instruction =
                "This is an isometric DND battlemap painting. Apply the following improvements while " +
                "keeping the overall composition, art style, and layout intact:\n\n" +
                feedback + "\n\n" +
                "Return the improved version of the full map as a single square image.";

            // Reuse the GenerateStyledTile multimodal path: it already does text+image → image via Gemini.
            // We pass the current map as the "style anchor" and the feedback as the prompt.
            // GenerateStyledTile bypasses cache for non-google models, so force a Gemini-style call here:
            if (!this.imageModel.ToLower().StartsWith("google/"))
            {
                Debug.LogWarning("[LLMService] RefineMapImageAsync requires a google/* imageModel for multimodal refinement; returning original image.");
                return image;
            }
            return await GenerateStyledTile(instruction, image);
        }

        private static LLMChatResult ParseChatToolsResponse(string rawJson)
        {
            var result = new LLMChatResult();
            try
            {
                var dto = JsonUtility.FromJson<ResponseWithToolsDto>(rawJson);
                if (dto?.choices == null || dto.choices.Count == 0) return result;
                var msg = dto.choices[0].message;
                if (msg == null) return result;

                if (msg.tool_calls != null && msg.tool_calls.Count > 0)
                {
                    foreach (var tc in msg.tool_calls)
                    {
                        if (tc?.function == null) continue;
                        result.ToolCalls.Add(new LLMToolCall
                        {
                            Id = tc.id,
                            Name = tc.function.name,
                            ArgumentsJson = tc.function.arguments,
                        });
                    }
                }
                result.Text = msg.content;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LLMService] Failed to parse tool-call response: {e.Message}\nRaw: {rawJson}");
            }
            return result;
        }

        private Texture2D ParseBase64Image(string content)
        {
            // Regex to find data:image/png;base64,....
            // Matches "data:image/png;base64," followed by the base64 string, ending at a quote, paren, bracket, or whitespace.
            // Adjusted regex to capture until a non-base64 character or common delimiter
            var regex = new System.Text.RegularExpressions.Regex(@"data:image\/[a-zA-Z]+;base64,([a-zA-Z0-9+/=]+)");
            var match = regex.Match(content);

            if (match.Success)
            {
                string base64 = match.Groups[1].Value;
                
                try 
                {
                    byte[] imageBytes = System.Convert.FromBase64String(base64);
                    Texture2D tex = new Texture2D(2, 2);
                    if (tex.LoadImage(imageBytes))
                    {
                        return tex;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to convert base64: {e.Message}");
                }
            }
            
            // IMPROVED DEBUGGING
            Debug.LogError($"Failed to parse base64 image from Gemini response. Content preview: {content.Substring(0, Mathf.Min(content.Length, 500))}...");
            return null;
        }

        private async Task<Texture2D> DownloadTexture(string url)
        {
            using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    return UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
                }
                else
                {
                    Debug.LogError($"Texture Download Error: {request.error}");
                    return null;
                }
            }
        }
    }
}
