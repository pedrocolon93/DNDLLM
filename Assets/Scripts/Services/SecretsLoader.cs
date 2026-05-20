// Assets/Scripts/Services/SecretsLoader.cs
//
// Reads API keys from Assets/StreamingAssets/local_secrets.json at runtime.
// The file is gitignored; commit local_secrets.json.example as a template.
//
// Each LLMService/TTSService field with a placeholder value (empty, REDACTED, or
// the well-known "PUT_YOUR_*_HERE") is overlaid with the matching entry from the
// JSON. Real values typed into the Inspector still win, so collaborators can keep
// their config out of source control without breaking the existing workflow.

using System;
using System.IO;
using UnityEngine;

namespace DNDLLM.Services
{
    [Serializable]
    public sealed class LocalSecrets
    {
        public string openRouterApiKey;
        public string localApiKey;
        public string elevenLabsApiKey;
    }

    public static class SecretsLoader
    {
        private static LocalSecrets _cached;
        private static bool _loaded;

        public static LocalSecrets Load()
        {
            if (_loaded) return _cached;
            _loaded = true;

            string path = Path.Combine(Application.streamingAssetsPath, "local_secrets.json");
            if (!File.Exists(path))
            {
                Debug.Log($"[SecretsLoader] No local_secrets.json at {path} — using Inspector values only.");
                return _cached;
            }

            try
            {
                string json = File.ReadAllText(path);
                _cached = JsonUtility.FromJson<LocalSecrets>(json);
                Debug.Log("[SecretsLoader] Loaded API keys from StreamingAssets/local_secrets.json.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SecretsLoader] Failed to parse local_secrets.json: {e.Message}");
                _cached = null;
            }
            return _cached;
        }

        /// <summary>Returns true when the supplied value looks like a placeholder rather than a real key.</summary>
        public static bool IsPlaceholder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            string s = value.Trim();
            if (s.Equals("REDACTED", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.IndexOf("PUT_YOUR", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.StartsWith("sk-or-v1-YOUR", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>If <paramref name="current"/> looks like a placeholder, return <paramref name="fallback"/>; else keep current.</summary>
        public static string Resolve(string current, string fallback)
            => IsPlaceholder(current) ? (fallback ?? "") : current;
    }
}
