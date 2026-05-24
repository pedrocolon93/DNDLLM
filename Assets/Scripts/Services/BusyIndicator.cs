using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DNDLLM.Services
{
    /// <summary>
    /// Modal busy overlay shown whenever any long-running async work is in flight.
    /// Refcounted: nested begin/end pairs from concurrent ops stack cleanly; the indicator
    /// hides when the count returns to zero. The backdrop blocks input while visible.
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

        // Built lazily on first Show()
        private Canvas        _canvas;
        private CanvasGroup   _backdropGroup;
        private CanvasGroup   _panelGroup;
        private RectTransform _panelRect;
        private RectTransform _spinnerRect;
        private TMP_Text      _labelTmp;

        // Animation state: _t glides toward _target (0 = hidden, 1 = shown).
        private float _t;
        private float _target;
        private const float FadeInDuration  = 0.18f;
        private const float FadeOutDuration = 0.14f;
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
            return go.AddComponent<BusyIndicator>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
        }

        private IDisposable BeginInternal(string label)
        {
            _labels.Add(label ?? "");
            _target = 1f;
            if (_canvas != null) _canvas.enabled = true;
            UpdateLabel();
            return new Token(this, _labels.Count - 1);
        }

        private void EndInternal(int handle)
        {
            if (handle >= 0 && handle < _labels.Count) _labels.RemoveAt(handle);
            else if (_labels.Count > 0) _labels.RemoveAt(_labels.Count - 1);

            if (_labels.Count == 0) _target = 0f;
            else UpdateLabel();
        }

        private void UpdateLabel()
        {
            if (_labelTmp == null) return;
            if (_labels.Count == 0) { _labelTmp.text = ""; return; }
            string top = _labels[_labels.Count - 1];
            int extra = _labels.Count - 1;
            _labelTmp.text = extra > 0 ? $"{top}  (+{extra} more)" : top;
        }

        private void Update()
        {
            // Always spin while the panel has any visibility.
            if (_t > 0.001f || _target > 0.001f)
                _spinAngle = (_spinAngle + 540f * Time.unscaledDeltaTime) % 360f;

            if (Mathf.Approximately(_t, _target))
            {
                if (_t <= 0.001f && _canvas != null && _canvas.enabled)
                    _canvas.enabled = false;
                return;
            }

            float speed = 1f / (_target > _t ? FadeInDuration : FadeOutDuration);
            _t = Mathf.MoveTowards(_t, _target, speed * Time.unscaledDeltaTime);

            // Ease-out cubic for the panel scale; backdrop uses linear alpha.
            float eased = 1f - Mathf.Pow(1f - _t, 3f);
            if (_backdropGroup != null) _backdropGroup.alpha = _t * 0.62f;
            if (_panelGroup    != null) _panelGroup.alpha    = _t;
            if (_panelRect != null)
            {
                float s = Mathf.Lerp(0.85f, 1f, eased);
                _panelRect.localScale = new Vector3(s, s, 1f);
            }
            if (_spinnerRect != null)
                _spinnerRect.localRotation = Quaternion.Euler(0f, 0f, -_spinAngle);
        }

        // ── UI construction ──────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGO = new GameObject("BusyCanvas");
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32767; // sit above every other canvas
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();
            _canvas.enabled = false;

            // Full-screen backdrop — blocks raycasts so input is suspended while busy.
            var backdropGO = new GameObject("Backdrop", typeof(RectTransform));
            backdropGO.transform.SetParent(_canvas.transform, false);
            var bdRT = (RectTransform)backdropGO.transform;
            bdRT.anchorMin = Vector2.zero; bdRT.anchorMax = Vector2.one;
            bdRT.offsetMin = Vector2.zero; bdRT.offsetMax = Vector2.zero;
            var bdImg = backdropGO.AddComponent<Image>();
            bdImg.color = Color.black;
            bdImg.raycastTarget = true;
            _backdropGroup = backdropGO.AddComponent<CanvasGroup>();
            _backdropGroup.alpha = 0f;
            _backdropGroup.blocksRaycasts = true;
            _backdropGroup.interactable = false;

            // Center panel
            var panelGO = new GameObject("Panel", typeof(RectTransform));
            panelGO.transform.SetParent(_canvas.transform, false);
            _panelRect = (RectTransform)panelGO.transform;
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot     = new Vector2(0.5f, 0.5f);
            _panelRect.sizeDelta = new Vector2(360f, 200f);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color32(0x14, 0x0E, 0x05, 0xF2); // dark wood
            _panelGroup = panelGO.AddComponent<CanvasGroup>();
            _panelGroup.alpha = 0f;
            _panelGroup.blocksRaycasts = false;
            _panelGroup.interactable = false;

            // Gold border (4 thin Images on each edge)
            AddBorder(_panelRect, new Color32(0xC9, 0xA2, 0x55, 0xFF), 2f);

            // Spinner — rotating ring
            var spinnerGO = new GameObject("Spinner", typeof(RectTransform));
            spinnerGO.transform.SetParent(_panelRect, false);
            _spinnerRect = (RectTransform)spinnerGO.transform;
            _spinnerRect.anchorMin = new Vector2(0.5f, 0.5f);
            _spinnerRect.anchorMax = new Vector2(0.5f, 0.5f);
            _spinnerRect.pivot     = new Vector2(0.5f, 0.5f);
            _spinnerRect.anchoredPosition = new Vector2(0f, 26f);
            _spinnerRect.sizeDelta = new Vector2(64f, 64f);
            var spinnerImg = spinnerGO.AddComponent<Image>();
            spinnerImg.sprite = BuildRingSprite();
            spinnerImg.type   = Image.Type.Filled;
            spinnerImg.fillMethod = Image.FillMethod.Radial360;
            spinnerImg.fillOrigin = (int)Image.Origin360.Top;
            spinnerImg.fillAmount = 0.75f;
            spinnerImg.color = new Color32(0xC9, 0xA2, 0x55, 0xFF); // gold

            // Label
            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(_panelRect, false);
            var labelRT = (RectTransform)labelGO.transform;
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(1f, 0.5f);
            labelRT.offsetMin = new Vector2(16f, 12f);
            labelRT.offsetMax = new Vector2(-16f, -8f);
            _labelTmp = labelGO.AddComponent<TextMeshProUGUI>();
            _labelTmp.text      = "";
            _labelTmp.fontSize  = 18f;
            _labelTmp.color     = new Color(0.94f, 0.92f, 0.88f, 1f);
            _labelTmp.alignment = TextAlignmentOptions.Center;
            _labelTmp.fontStyle = FontStyles.Bold;
            _labelTmp.enableWordWrapping = true;
        }

        private static void AddBorder(RectTransform parent, Color color, float thickness)
        {
            void Edge(string name, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = aMin; rt.anchorMax = aMax;
                rt.offsetMin = offMin; rt.offsetMax = offMax;
                var img = go.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
            }
            Edge("Top",    new Vector2(0,1), new Vector2(1,1), new Vector2(0,-thickness), new Vector2(0, 0));
            Edge("Bottom", new Vector2(0,0), new Vector2(1,0), new Vector2(0, 0),         new Vector2(0, thickness));
            Edge("Left",   new Vector2(0,0), new Vector2(0,1), new Vector2(0, 0),         new Vector2(thickness, 0));
            Edge("Right",  new Vector2(1,0), new Vector2(1,1), new Vector2(-thickness,0), new Vector2(0, 0));
        }

        private static Sprite _cachedRing;
        private static Sprite BuildRingSprite()
        {
            if (_cachedRing != null) return _cachedRing;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            float cx = size * 0.5f, cy = size * 0.5f;
            float rOuter = size * 0.46f;
            float rInner = size * 0.34f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx, dy = y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r < rInner - 1f || r > rOuter + 1f) { pixels[y * size + x] = new Color32(0, 0, 0, 0); continue; }
                // Soft anti-aliasing at both edges
                float a = 1f;
                if (r < rInner) a = Mathf.Clamp01(r - (rInner - 1f));
                else if (r > rOuter) a = Mathf.Clamp01((rOuter + 1f) - r);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _cachedRing = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _cachedRing.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _cachedRing;
        }

        // ── token types ──────────────────────────────────────────────────────

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
