using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace DNDLLM.Services
{
    public class LLMService : MonoBehaviour
    {
        public static LLMService Instance { get; private set; }

        [SerializeField] private string apiKey = "sk-or-v1-YOUR_KEY_HERE";
        [SerializeField] private bool useMock = true;
        [SerializeField] private string model = "openai/gpt-3.5-turbo";
        [SerializeField] private string imageModel = "openai/dall-e-3"; 
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
                return $"[MOCK LLM RESPONSE] Based on '{userPrompt}' with context '{systemPrompt}'.";
            }

            string url = "https://openrouter.ai/api/v1/chat/completions";

            var requestData = new OpenRouterRequest
            {
                model = this.model,
                messages = new List<Message>
                {
                    new Message { role = "system", content = systemPrompt },
                    new Message { role = "user", content = userPrompt }
                }
            };

            string jsonData = JsonUtility.ToJson(requestData);

            using (UnityEngine.Networking.UnityWebRequest request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
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
                    Debug.Log($"OpenRouter Response: {request.downloadHandler.text}");
                    var responseData = JsonUtility.FromJson<OpenRouterResponse>(request.downloadHandler.text);
                    if (responseData != null && responseData.choices != null && responseData.choices.Count > 0)
                    {
                        return responseData.choices[0].message.content;
                    }
                }
                else
                {
                    Debug.LogError($"OpenRouter Error: {request.error} - {request.downloadHandler.text}");
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
