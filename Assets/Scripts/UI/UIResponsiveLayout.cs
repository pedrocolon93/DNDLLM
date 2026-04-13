using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DNDLLM.UI
{
    /// <summary>
    /// Ensures the UI Canvas scales correctly on all resolutions.
    ///
    /// Expected scene structure (all direct children of the Canvas):
    ///   Canvas
    ///   ├── Scroll View  – chat history, fills screen minus input row
    ///   ├── InputField   – player text input, bottom bar (left of button)
    ///   └── Button       – send button, bottom-right corner
    ///
    /// Attach to the root Canvas GameObject.
    /// Works both in Edit mode (via OnValidate) and at runtime (via Awake).
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    public class UIResponsiveLayout : MonoBehaviour
    {
        [Header("Reference Resolution")]
        [Tooltip("Design resolution. All sizes below are in these units.")]
        public Vector2 referenceResolution = new Vector2(1920, 1080);

        [Range(0f, 1f)]
        [Tooltip("0 = scale to match width, 1 = match height, 0.5 = balanced")]
        public float matchWidthOrHeight = 0.5f;

        [Header("Bottom Input Bar")]
        [Tooltip("Height of the input row in reference pixels")]
        public float inputRowHeight = 60f;

        [Tooltip("Width of the Send button in reference pixels")]
        public float buttonWidth = 160f;

        private void Awake() => Apply();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
                Apply();
        }
#endif

        public void Apply()
        {
            ConfigureCanvasScaler();
            ConfigureRects();
        }

        // ── Canvas Scaler ─────────────────────────────────────────────────────

        private void ConfigureCanvasScaler()
        {
            CanvasScaler cs = GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = referenceResolution;
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cs.matchWidthOrHeight = matchWidthOrHeight;
            cs.referencePixelsPerUnit = 100f;
        }

        // ── Element Layout ────────────────────────────────────────────────────

        private void ConfigureRects()
        {
            ScrollRect scrollView = GetComponentInChildren<ScrollRect>(true);
            TMP_InputField inputField = GetComponentInChildren<TMP_InputField>(true);
            Button sendButton = GetComponentInChildren<Button>(true);

            if (scrollView != null)
                LayoutScrollView(scrollView.GetComponent<RectTransform>());

            if (inputField != null)
                LayoutInputField(inputField.GetComponent<RectTransform>());

            if (sendButton != null)
                LayoutButton(sendButton.GetComponent<RectTransform>());

            // Content inside ScrollView
            if (scrollView != null)
            {
                ConfigureScrollContent(scrollView);
                if (scrollView.viewport != null)
                    Stretch(scrollView.viewport);
            }
        }

        /// <summary>Chat scroll view fills the canvas, leaving inputRowHeight at the bottom.</summary>
        private void LayoutScrollView(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            // offsetMin=(0, inputRowHeight), offsetMax=(0,0)
            // anchoredPosition = (offsetMin + offsetMax) / 2 = (0, inputRowHeight/2)
            // sizeDelta = offsetMax - offsetMin = (0, -inputRowHeight)
            rt.anchoredPosition = new Vector2(0f, inputRowHeight * 0.5f);
            rt.sizeDelta = new Vector2(0f, -inputRowHeight);
        }

        /// <summary>Input field spans the full bottom bar width minus the button.</summary>
        private void LayoutInputField(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // offsetMin=(0,0), offsetMax=(-buttonWidth, inputRowHeight)
            rt.anchoredPosition = new Vector2(-buttonWidth * 0.5f, inputRowHeight * 0.5f);
            rt.sizeDelta = new Vector2(-buttonWidth, inputRowHeight);
        }

        /// <summary>Button docks to bottom-right corner.</summary>
        private void LayoutButton(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(buttonWidth, inputRowHeight);
        }

        private static void ConfigureScrollContent(ScrollRect scrollView)
        {
            RectTransform content = scrollView.content;
            if (content == null) return;

            // Grows downward from the top, full width
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(10, 10, 10, 10);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
