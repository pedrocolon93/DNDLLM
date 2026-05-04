using System;
using System.Collections.Generic;
using UnityEngine;

namespace DNDLLM.Services
{
    /// <summary>
    /// Lightweight OnGUI spinner shown whenever any long-running async work is in flight.
    /// Refcounted: nested begin/end pairs from concurrent ops stack cleanly; the indicator
    /// hides when the count returns to zero.
    ///
    /// Usage:
    ///     using (BusyIndicator.Show("Painting map..."))
    ///     {
    ///         await LongRunningWork();
    ///     }
    /// </summary>
    public class BusyIndicator : MonoBehaviour
    {
        public static BusyIndicator Instance { get; private set; }

        private readonly List<string> _labels = new List<string>();
        private float _spinAngle;

        public static IDisposable Show(string label)
        {
            var inst = EnsureInstance();
            return inst != null ? inst.BeginInternal(label) : new NullToken();
        }

        private static BusyIndicator EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("BusyIndicator");
            DontDestroyOnLoad(go);
            return go.AddComponent<BusyIndicator>(); // Awake sets Instance
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private IDisposable BeginInternal(string label)
        {
            _labels.Add(label ?? "");
            return new Token(this, _labels.Count - 1);
        }

        private void EndInternal(int handle)
        {
            // Remove first occurrence of the label by reference position. Index drift is fine —
            // we only care that the refcount goes back to zero in matching order most of the time.
            if (handle >= 0 && handle < _labels.Count) _labels.RemoveAt(handle);
            else if (_labels.Count > 0) _labels.RemoveAt(_labels.Count - 1);
        }

        private void Update()
        {
            if (_labels.Count > 0) _spinAngle += 540f * Time.unscaledDeltaTime;
        }

        // Cached styles (built lazily on first OnGUI — GUI styles cannot be created in field initializers).
        private GUIStyle _spinStyle;
        private GUIStyle _labelStyle;

        private void OnGUI()
        {
            if (_labels.Count == 0) return;

            if (_spinStyle == null)
            {
                _spinStyle = new GUIStyle(GUI.skin.label)
                {
                    normal = { textColor = new Color(1f, 0.85f, 0.2f) },
                    fontSize = 18,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                };
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    normal = { textColor = Color.white },
                    fontSize = 13,
                    alignment = TextAnchor.MiddleLeft,
                };
            }

            string label = _labels[_labels.Count - 1];
            int extra = _labels.Count - 1;
            string text = extra > 0 ? $"{label}  (+{extra} more)" : label;

            // Measure to right-size the pill
            var textContent = new GUIContent(text);
            float textW = _labelStyle.CalcSize(textContent).x;
            float pillW = Mathf.Min(Screen.width - 32f, textW + 56f);
            float pillH = 32f;
            float x = Screen.width - pillW - 16f;
            float y = 16f;

            // Background pill
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Box(new Rect(x, y, pillW, pillH), GUIContent.none);
            GUI.color = prev;

            // Spinner glyph — cycle through 4 frames using the spin angle
            string[] frames = { "|", "/", "-", "\\" };
            int idx = (int)((_spinAngle / 90f) % 4);
            if (idx < 0) idx += 4;
            GUI.Label(new Rect(x + 8f, y + 4f, 24f, pillH - 8f), frames[idx], _spinStyle);

            // Label
            GUI.Label(new Rect(x + 36f, y + 4f, pillW - 44f, pillH - 8f), text, _labelStyle);
        }

        // ── token types ────────────────────────────────────────────────────

        private sealed class Token : IDisposable
        {
            private readonly BusyIndicator _owner;
            private readonly int _handle;
            private bool _disposed;
            public Token(BusyIndicator owner, int handle) { _owner = owner; _handle = handle; }
            public void Dispose()
            {
                if (_disposed || _owner == null) return;
                _disposed = true;
                _owner.EndInternal(_handle);
            }
        }

        private sealed class NullToken : IDisposable { public void Dispose() { } }
    }
}
