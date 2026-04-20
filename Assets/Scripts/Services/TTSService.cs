using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DNDLLM.Services
{
    /// <summary>
    /// OpenRouter-backed TTS for DM narration. Streams audio via /chat/completions SSE,
    /// caches WAVs on disk, plays through a singleton AudioSource.
    /// </summary>
    public class TTSService : MonoBehaviour
    {
        public static TTSService Instance { get; private set; }

        [Header("General")]
        [SerializeField] private bool enabled_ = true;
        [SerializeField, Range(0f, 1f)] private float volume = 0.9f;

        [Header("OpenRouter")]
        [SerializeField] private string model = "openai/gpt-audio";
        [SerializeField] private string voice = "alloy";  // alloy|echo|fable|onyx|nova|shimmer
        [SerializeField] private string format = "wav";   // wav only for now

        [Header("Behavior")]
        [SerializeField] private bool autoPlay = false;
        [SerializeField] private bool useCache = true;

        public bool Enabled  { get => enabled_; set => enabled_ = value; }
        public bool AutoPlay { get => autoPlay; set => autoPlay = value; }
        public string Voice  => voice;
        public string Model  => model;
        public string Format => format;

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

        /// <summary>Synthesize audio for <paramref name="text"/>. Checks cache first; otherwise streams from OpenRouter.</summary>
        public async Task<AudioClip> Synthesize(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            if (useCache)
            {
                var cached = AudioCache.Load("openrouter", voice, format, text);
                if (cached != null) return cached;
            }

            // Network path — filled in by Task 7.
            return await SynthesizeRemote(text, ct);
        }

        private Task<AudioClip> SynthesizeRemote(string text, CancellationToken ct)
        {
            Debug.LogWarning("[TTSService] SynthesizeRemote not yet implemented.");
            return Task.FromResult<AudioClip>(null);
        }
    }
}
