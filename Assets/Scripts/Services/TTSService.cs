using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DNDLLM.Services
{
    public enum TtsProvider { OpenRouter, ElevenLabs }

    /// <summary>
    /// TTS for DM narration with two pluggable providers:
    ///   - OpenRouter (openai/gpt-audio over /chat/completions SSE)
    ///   - ElevenLabs (POST .../v1/text-to-speech/{voiceId}, raw WAV via Accept header)
    /// Both flows share the same AudioCache + WavDecoder + singleton AudioSource pipeline.
    /// </summary>
    public class TTSService : MonoBehaviour
    {
        public static TTSService Instance { get; private set; }

        [Header("General")]
        [SerializeField] private bool enabled_ = true;
        [SerializeField, Range(0f, 1f)] private float volume = 0.9f;
        [SerializeField] private TtsProvider provider = TtsProvider.ElevenLabs;

        [Header("OpenRouter")]
        [SerializeField] private string model = "openai/gpt-audio";
        [SerializeField] private string voice = "alloy";  // alloy|echo|fable|onyx|nova|shimmer
        [SerializeField] private string format = "wav";   // wav only for now

        [Header("ElevenLabs")]
        [SerializeField] private string elevenLabsApiKey   = "";
        [SerializeField] private string elevenLabsVoiceId  = "21m00Tcm4TlvDq8ikWAM"; // default "Rachel"
        [SerializeField] private string elevenLabsModel    = "eleven_monolingual_v1";

        [Header("Behavior")]
        [SerializeField] private bool autoPlay = false;
        [SerializeField] private bool useCache = true;

        public bool Enabled  { get => enabled_; set => enabled_ = value; }
        public bool AutoPlay { get => autoPlay; set => autoPlay = value; }
        public string Voice  => voice;
        public string Model  => model;
        public string Format => format;
        public TtsProvider Provider { get => provider; set => provider = value; }

        public bool IsSpeaking => _source != null && _source.isPlaying;

        public event Action<string> OnPlaybackStarted;
        public event Action<string> OnPlaybackStopped;

        private AudioSource _source;
        private string      _currentText;
        private bool        _rateLimitWarned;

        private void Awake()
        {
            if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
            else { Destroy(gameObject); return; }

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake  = false;
            _source.spatialBlend = 0f;  // 2D
            _source.volume       = volume;
        }

        public void Stop()
        {
            if (_source != null && _source.isPlaying) _source.Stop();
            if (!string.IsNullOrEmpty(_currentText))
            {
                string t = _currentText;
                _currentText = null;
                OnPlaybackStopped?.Invoke(t);
            }
        }

        /// <summary>Fire-and-forget playback. Safe to call from UI handlers.</summary>
        public async void PlayAsync(string text)
        {
            if (!enabled_ || string.IsNullOrWhiteSpace(text)) return;
            try
            {
                Stop();
                var clip = await Synthesize(text, CancellationToken.None);
                if (clip == null) return;
                _currentText = text;
                _source.clip = clip;
                _source.volume = volume;
                _source.Play();
                OnPlaybackStarted?.Invoke(text);
                StartCoroutine(WatchPlayback(text));
            }
            catch (Exception e)
            {
                Debug.LogError($"[TTSService] PlayAsync failed: {e.Message}");
            }
        }

        private System.Collections.IEnumerator WatchPlayback(string text)
        {
            while (_source != null && _source.isPlaying) yield return null;
            if (_currentText == text)
            {
                _currentText = null;
                OnPlaybackStopped?.Invoke(text);
            }
        }

        /// <summary>Synthesize audio for <paramref name="text"/>. Checks the per-provider cache first; otherwise hits the network.</summary>
        public async Task<AudioClip> Synthesize(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string cacheProvider = provider == TtsProvider.ElevenLabs ? "elevenlabs" : "openrouter";
            string cacheVoice    = provider == TtsProvider.ElevenLabs ? elevenLabsVoiceId : voice;

            if (useCache)
            {
                var cached = AudioCache.Load(cacheProvider, cacheVoice, format, text);
                if (cached != null) return cached;
            }

            return provider == TtsProvider.ElevenLabs
                ? await SynthesizeRemoteElevenLabs(text, ct)
                : await SynthesizeRemote(text, ct);
        }

        private async Task<AudioClip> SynthesizeRemote(string text, CancellationToken ct)
        {
            var llm = LLMService.Instance;
            if (llm == null || string.IsNullOrEmpty(llm.ApiKey))
            {
                Debug.LogWarning("[TTSService] No LLMService / API key — TTS disabled this session.");
                enabled_ = false;
                return null;
            }

            // Build the request JSON by hand — JsonUtility cannot emit nested objects cleanly here.
            string escapedText  = EscapeJson("Narrate: " + text);
            string escapedModel = EscapeJson(model);
            string escapedVoice = EscapeJson(voice);
            string escapedFmt   = EscapeJson(format);

            string requestJson =
                "{\"model\":\"" + escapedModel + "\"," +
                "\"stream\":true," +
                "\"modalities\":[\"text\",\"audio\"]," +
                "\"audio\":{\"voice\":\"" + escapedVoice + "\",\"format\":\"" + escapedFmt + "\"}," +
                "\"messages\":[{\"role\":\"user\",\"content\":\"" + escapedText + "\"}]}";

            string url = "https://openrouter.ai/api/v1/chat/completions";

            var handler = new AudioStreamDownloadHandler();
            using (var req = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
            {
                byte[] body = Encoding.UTF8.GetBytes(requestJson);
                req.uploadHandler   = new UnityEngine.Networking.UploadHandlerRaw(body);
                req.downloadHandler = handler;
                req.SetRequestHeader("Content-Type",  "application/json");
                req.SetRequestHeader("Accept",        "text/event-stream");
                req.SetRequestHeader("Authorization", "Bearer " + llm.ApiKey);
                req.SetRequestHeader("HTTP-Referer",  "https://github.com/google-deepmind/antigravity");
                req.SetRequestHeader("X-Title",       "DNDLLM");

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (ct.IsCancellationRequested) { req.Abort(); return null; }
                    await Task.Yield();
                }

                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[TTSService] SSE error {req.responseCode}: {req.error}");
                    // Surface rate-limit to chat once per session.
                    if (req.responseCode == 429 && !_rateLimitWarned)
                    {
                        _rateLimitWarned = true;
                        DnD.UI.ChatUI.Instance?.AddSystemMessage("TTS rate-limited — playback skipped.");
                    }
                    return null;
                }

                byte[] wav = await handler.Completion.Task;
                if (wav == null || wav.Length == 0)
                {
                    Debug.LogWarning("[TTSService] Stream yielded zero audio bytes.");
                    return null;
                }

                if (useCache) AudioCache.Save("openrouter", voice, format, text, wav);
                // Ensure we are back on Unity's main thread before touching AudioClip APIs.
                await Task.Yield();
                return WavDecoder.Decode(wav, "tts-live");
            }
        }

        private async Task<AudioClip> SynthesizeRemoteElevenLabs(string text, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(elevenLabsApiKey))
            {
                Debug.LogWarning("[TTSService] ElevenLabs API key not set — TTS disabled this session.");
                enabled_ = false;
                return null;
            }
            if (string.IsNullOrEmpty(elevenLabsVoiceId))
            {
                Debug.LogWarning("[TTSService] ElevenLabs voice id not set.");
                return null;
            }

            string url = "https://api.elevenlabs.io/v1/text-to-speech/" + Uri.EscapeDataString(elevenLabsVoiceId);

            // Forces WAV so we can reuse WavDecoder and avoid pulling in an MP3 codec.
            // ElevenLabs will return raw PCM16 WAV bytes when Accept: audio/wav is sent.
            string body =
                "{\"model_id\":\"" + EscapeJson(elevenLabsModel) + "\"," +
                "\"text\":\""      + EscapeJson(text)            + "\"}";

            using (var req = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                req.uploadHandler   = new UnityEngine.Networking.UploadHandlerRaw(bodyBytes);
                req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept",       "audio/wav");
                req.SetRequestHeader("xi-api-key",   elevenLabsApiKey);

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (ct.IsCancellationRequested) { req.Abort(); return null; }
                    await Task.Yield();
                }

                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[TTSService] ElevenLabs error {req.responseCode}: {req.error} — {req.downloadHandler?.text}");
                    if (req.responseCode == 429 && !_rateLimitWarned)
                    {
                        _rateLimitWarned = true;
                        DnD.UI.ChatUI.Instance?.AddSystemMessage("TTS rate-limited — playback skipped.");
                    }
                    return null;
                }

                byte[] wav = req.downloadHandler.data;
                if (wav == null || wav.Length == 0)
                {
                    Debug.LogWarning("[TTSService] ElevenLabs returned zero audio bytes.");
                    return null;
                }

                if (useCache) AudioCache.Save("elevenlabs", elevenLabsVoiceId, format, text, wav);
                await Task.Yield(); // back on Unity main thread before AudioClip APIs
                return WavDecoder.Decode(wav, "tts-elevenlabs");
            }
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
