using UnityEngine;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DNDLLM.Services
{
    /// <summary>Disk cache for TTS audio, keyed by SHA256(provider|voice|format|text).</summary>
    public static class AudioCache
    {
        private static string CacheDirectory => Path.Combine(Application.persistentDataPath, "AudioCache");

        public static void Init()
        {
            if (!Directory.Exists(CacheDirectory)) Directory.CreateDirectory(CacheDirectory);
        }

        public static string Key(string provider, string voice, string format, string text)
        {
            using (var sha = SHA256.Create())
            {
                string composite = $"{provider}|{voice}|{format}|{text}";
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(composite));
                var sb = new StringBuilder(64);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string PathFor(string key) => Path.Combine(CacheDirectory, key + ".wav");

        public static AudioClip Load(string provider, string voice, string format, string text)
        {
            Init();
            string path = PathFor(Key(provider, voice, format, text));
            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                return WavDecoder.Decode(bytes, "tts-cached");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AudioCache] Load failed for {path}: {e.Message}. Deleting.");
                try { File.Delete(path); } catch { /* ignore */ }
                return null;
            }
        }

        public static void Save(string provider, string voice, string format, string text, byte[] wavBytes)
        {
            if (wavBytes == null || wavBytes.Length == 0) return;
            Init();
            string path = PathFor(Key(provider, voice, format, text));
            try { File.WriteAllBytes(path, wavBytes); }
            catch (System.Exception e) { Debug.LogWarning($"[AudioCache] Save failed: {e.Message}"); }
        }
    }
}
