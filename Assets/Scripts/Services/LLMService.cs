using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace DNDLLM.Services
{
    public enum LLMProvider { OpenRouter, Ollama }

    public class LLMService : MonoBehaviour
    {
        public static LLMService Instance { get; private set; }

        [Header("Provider")]
        [SerializeField] private LLMProvider provider = LLMProvider.OpenRouter;
        [SerializeField] private bool useMock = false;

        [Header("OpenRouter")]
        [SerializeField] private string apiKey = "sk-or-v1-YOUR_KEY_HERE";
        public string ApiKey => apiKey;
        [SerializeField] private string model = "openai/gpt-4o-mini";
        [SerializeField] private string imageModel = "openai/dall-e-3";

        [Header("Ollama  (text only — images still use OpenRouter)")]
        [SerializeField] private string ollamaBaseUrl = "http://localhost:11434";
        [SerializeField] private string ollamaModel = "llama3.2";

        [Header("Cache")]
        [SerializeField] private bool useCache = true;

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
