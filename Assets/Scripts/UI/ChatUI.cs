// Assets/Scripts/UI/ChatUI.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DnD.Data;

namespace DnD.UI
{
    public class ChatUI : MonoBehaviour
    {
        public static ChatUI Instance { get; internal set; }

        [Header("UI References")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform contentPanel;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private Button sendButton;

        [Header("Settings")]
        [SerializeField] private int maxMessages = 100;
        [SerializeField] private bool autoScroll = true;
        [SerializeField] private float typewriterSpeed = 0.03f;

        private readonly List<GameObject> activeMessages = new List<GameObject>();
        private Coroutine typewriterCoroutine;

        public System.Action<string> OnPlayerInput;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void OnGUI()
        {
            GUI.color = Color.cyan;
            string info = $"[ChatUI] msgs={activeMessages.Count}";
            if (contentPanel != null)
                info += $" panelH={contentPanel.rect.height:F0} panelW={contentPanel.rect.width:F0}";
            else
                info += " contentPanel=NULL";
            GUI.Label(new Rect(10, 50, 700, 30), info);
            GUI.color = Color.white;
        }

        private void Start()
        {
            if (sendButton != null)
                sendButton.onClick.AddListener(SendMessage);

            if (inputField != null)
                inputField.onSubmit.AddListener(_ => { if (!string.IsNullOrWhiteSpace(inputField.text)) SendMessage(); });
        }

        // ── Public API ────────────────────────────────────────────────

        public void AddPlayerMessage(string message)  => AddMessage(message, MessageType.Player);
        public void AddSystemMessage(string message)  => AddMessage(message, MessageType.System);

        public void AddDMMessage(string message, bool useTypewriter = false)
        {
            if (useTypewriter)
            {
                if (typewriterCoroutine != null) StopCoroutine(typewriterCoroutine);
                typewriterCoroutine = StartCoroutine(TypewriterEffect(message));
            }
            else
            {
                AddMessage(message, MessageType.DM);
            }
        }

        public void AppendToDMMessage(string token)
        {
            if (activeMessages.Count == 0) return;
            var last = activeMessages[activeMessages.Count - 1];
            var tmp = last.GetComponentInChildren<TMP_Text>();
            if (tmp == null) return;
            tmp.text += token;
            if (autoScroll)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        public void ClearChat()
        {
            foreach (var msg in activeMessages)
                if (msg != null) Destroy(msg);
            activeMessages.Clear();
        }

        /// <summary>Enable/disable the player's text input (used during enemy turns / DM thinking).</summary>
        public void SetInputEnabled(bool enabled)
        {
            if (inputField != null)
            {
                inputField.interactable = enabled;
                if (!enabled) inputField.DeactivateInputField();
            }
            if (sendButton != null) sendButton.interactable = enabled;
        }

        /// <summary>Returns a snapshot of all visible messages for saving.</summary>
        public List<ChatMessageData> GetMessageHistory()
        {
            var result = new List<ChatMessageData>();
            foreach (var go in activeMessages)
            {
                if (go == null) continue;
                var tmp = go.GetComponentInChildren<TMP_Text>();
                if (tmp == null) continue;
                string name = go.name; // "Msg_Player", "Msg_DM", "Msg_System"
                string type = name.Contains("Player") ? "Player"
                            : name.Contains("DM")     ? "DM"
                            : "System";
                if (type == "System" && !name.Contains("System"))
                    Debug.LogWarning($"[ChatUI] GetMessageHistory: unrecognised message GO name '{name}', defaulting to System");
                result.Add(new ChatMessageData { type = type, text = tmp.GetParsedText() });
            }
            return result;
        }

        // ── Internal ──────────────────────────────────────────────────

        private void AddMessage(string text, MessageType type)
        {
            if (contentPanel == null) { Debug.LogError("[ChatUI] contentPanel is null"); return; }

            GameObject msgGO = BuildMessageGO(text, type);
            activeMessages.Add(msgGO);
            TrimHistory();
            // Force full canvas update so parent widths are known before rebuilding children
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentPanel);
            if (autoScroll) StartCoroutine(ScrollToBottom());
        }

        private GameObject BuildMessageGO(string text, MessageType type)
        {
            // Create with RectTransform up front — cannot add one after the fact
            var msgGO = new GameObject($"Msg_{type}", typeof(RectTransform));
            msgGO.transform.SetParent(contentPanel, false);

            // ContentSizeFitter drives height; VLG provides padding so TMP (a layout child) is measured
            var csf = msgGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var le = msgGO.AddComponent<LayoutElement>();
            le.minHeight = 24;

            var bg = msgGO.AddComponent<Image>();

            var vlg = msgGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 6, 6);
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth  = true;
            vlg.childControlHeight = true;
            vlg.spacing = 0;

            // Text child — no manual RectTransform; VLG controls sizing
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(msgGO.transform, false);
            textGO.AddComponent<RectTransform>();

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
            tmp.text = text;

            switch (type)
            {
                case MessageType.DM:
                    bg.color = UITheme.BackgroundDM;
                    tmp.color = UITheme.DmText;
                    tmp.fontSize = UITheme.FontDM;
                    tmp.fontStyle = FontStyles.Italic;
                    tmp.alignment = TextAlignmentOptions.TopLeft;
                    AttachPlayButton(msgGO, text);
                    if (DNDLLM.Services.TTSService.Instance != null
                        && DNDLLM.Services.TTSService.Instance.Enabled
                        && DNDLLM.Services.TTSService.Instance.AutoPlay)
                        DNDLLM.Services.TTSService.Instance.PlayAsync(text);
                    break;

                case MessageType.Player:
                    bg.color = UITheme.BackgroundPlayer;
                    tmp.color = UITheme.PlayerText;
                    tmp.fontSize = UITheme.FontPlayer;
                    tmp.alignment = TextAlignmentOptions.TopRight;
                    break;

                case MessageType.System:
                    bg.color = Color.clear;
                    tmp.color = UITheme.SystemText;
                    tmp.fontSize = UITheme.FontSystem;
                    tmp.fontStyle = FontStyles.Italic;
                    tmp.alignment = TextAlignmentOptions.Center;
                    break;
            }

            return msgGO;
        }

        private IEnumerator TypewriterEffect(string fullText)
        {
            var msgGO = new GameObject("Msg_DM", typeof(RectTransform));
            msgGO.transform.SetParent(contentPanel, false);
            var csf = msgGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var le = msgGO.AddComponent<LayoutElement>();
            le.minHeight = 24;
            var bg = msgGO.AddComponent<Image>();
            bg.color = UITheme.BackgroundDM;

            var vlg = msgGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 6, 6);
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth  = true;
            vlg.childControlHeight = true;
            vlg.spacing = 0;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(msgGO.transform, false);
            textGO.AddComponent<RectTransform>();

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
            tmp.color = UITheme.DmText;
            tmp.fontSize = UITheme.FontDM;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.text = "";

            activeMessages.Add(msgGO);
            AttachPlayButton(msgGO, fullText);
            if (DNDLLM.Services.TTSService.Instance != null
                && DNDLLM.Services.TTSService.Instance.Enabled
                && DNDLLM.Services.TTSService.Instance.AutoPlay)
                DNDLLM.Services.TTSService.Instance.PlayAsync(fullText);
            TrimHistory();

            foreach (char c in fullText)
            {
                tmp.text += c;
                yield return new WaitForSeconds(typewriterSpeed);
                if (autoScroll)
                {
                    Canvas.ForceUpdateCanvases();
                    scrollRect.verticalNormalizedPosition = 0f;
                }
            }

            yield return ScrollToBottom();
            typewriterCoroutine = null;
        }

        private void TrimHistory()
        {
            while (activeMessages.Count > maxMessages)
            {
                if (activeMessages[0] != null) Destroy(activeMessages[0]);
                activeMessages.RemoveAt(0);
            }
        }

        private IEnumerator ScrollToBottom()
        {
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        private void SendMessage()
        {
            if (inputField == null || string.IsNullOrWhiteSpace(inputField.text)) return;
            string message = inputField.text;
            inputField.text = "";
            inputField.ActivateInputField();
            AddPlayerMessage(message);
            OnPlayerInput?.Invoke(message);
        }

        public enum MessageType { Player, DM, System }

        // Glyphs picked from blocks LiberationSans actually contains; avoids the
        // "character not found" warnings that ▶ / ■ would trigger without a Symbols fallback.
        private const string PlayGlyph = "›";
        private const string StopGlyph = "×";

        /// <summary>Attach a play/stop TTS button to a DM message bubble. No-op if TTSService is missing or disabled.</summary>
        private void AttachPlayButton(GameObject bubble, string text)
        {
            var tts = DNDLLM.Services.TTSService.Instance;
            if (tts == null || !tts.Enabled) return;

            var btnGO = new GameObject("TTSButton", typeof(RectTransform));
            btnGO.transform.SetParent(bubble.transform, false);
            var rt = btnGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot     = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(28, 22);
            rt.anchoredPosition = new Vector2(-4, -4);

            var img = btnGO.AddComponent<Image>();
            img.color = new Color(0.15f, 0.1f, 0.05f, 0.8f);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;

            var textGO = new GameObject("Label", typeof(RectTransform));
            textGO.transform.SetParent(btnGO.transform, false);
            var trt = textGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = PlayGlyph;
            tmp.fontSize = 14f;
            tmp.color = UITheme.GoldAccent;
            tmp.alignment = TextAlignmentOptions.Center;

            string snapshot = text;
            btn.onClick.AddListener(() =>
            {
                var live = DNDLLM.Services.TTSService.Instance;
                if (live == null) return;
                if (tmp.text == StopGlyph) live.Stop();
                else                       live.PlayAsync(snapshot);
            });

            Action<string> onStart = s => { if (s == snapshot) tmp.text = StopGlyph; };
            Action<string> onStop  = s => { if (s == snapshot) tmp.text = PlayGlyph; };
            tts.OnPlaybackStarted += onStart;
            tts.OnPlaybackStopped += onStop;

            var cleanup = btnGO.AddComponent<TTSButtonCleanup>();
            cleanup.OnDestroyed = () =>
            {
                var s = DNDLLM.Services.TTSService.Instance;
                if (s == null) return;
                s.OnPlaybackStarted -= onStart;
                s.OnPlaybackStopped -= onStop;
            };
        }
    }

    internal class TTSButtonCleanup : MonoBehaviour
    {
        public Action OnDestroyed;
        private void OnDestroy() { OnDestroyed?.Invoke(); }
    }
}
