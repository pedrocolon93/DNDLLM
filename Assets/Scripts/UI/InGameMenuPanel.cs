using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DnD.UI
{
    /// <summary>
    /// In-game overlay menu: Save, Load/Main Menu, DM voice toggles, Resume.
    /// Opened by the MENU HUD button; wired to GameManager via public Actions.
    /// </summary>
    public class InGameMenuPanel : MonoBehaviour
    {
        public static InGameMenuPanel Instance { get; private set; }

        // Events wired by GameManager
        public System.Action OnSave;
        public System.Action OnLoad;
        public System.Action OnEditMap;
        public System.Action<bool> OnTTSEnabledChanged;
        public System.Action<bool> OnTTSAutoPlayChanged;

        // Set by UISceneBuilder. Hosts dynamically-built TTS toggle rows.
        [SerializeField] private GameObject controlsContainer;
        [SerializeField] private Button     saveButton;
        [SerializeField] private Button     loadButton;
        [SerializeField] private Button     editMapButton;
        [SerializeField] private Button     resumeButton;

        private GameObject _ttsEnabledRow, _ttsAutoPlayRow;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        // The editor-time AddListener calls in UISceneBuilder are non-persistent and don't
        // survive scene save. Re-bind every button here each time the panel opens so they
        // actually fire regardless of editor vs. play-mode entry path.
        private void OnEnable()
        {
            if (saveButton != null)
            {
                saveButton.onClick.RemoveAllListeners();
                saveButton.onClick.AddListener(() => OnSave?.Invoke());
            }
            if (loadButton != null)
            {
                loadButton.onClick.RemoveAllListeners();
                loadButton.onClick.AddListener(() => OnLoad?.Invoke());
            }
            if (editMapButton != null)
            {
                editMapButton.onClick.RemoveAllListeners();
                editMapButton.onClick.AddListener(() => { Close(); OnEditMap?.Invoke(); });
            }
            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveAllListeners();
                resumeButton.onClick.AddListener(Close);
            }
        }

        // Signature kept (with ignored grid params) so existing callers in GameManager don't
        // have to change. The per-tile regenerate rows lived here historically; tile regen is
        // now reachable from EditMapPanel.
        public void Open(int playerX = 0, int playerY = 0)
        {
            gameObject.SetActive(true);
            RebuildTTSRows();
        }

        public void Close() => gameObject.SetActive(false);

        public void RebuildTTSRows()
        {
            if (_ttsEnabledRow  != null) Destroy(_ttsEnabledRow);
            if (_ttsAutoPlayRow != null) Destroy(_ttsAutoPlayRow);
            if (controlsContainer == null) return;

            var tts = DNDLLM.Services.TTSService.Instance;
            bool enabledInit  = tts != null && tts.Enabled;
            bool autoPlayInit = tts != null && tts.AutoPlay;

            _ttsEnabledRow  = BuildToggleRow(controlsContainer.transform, "DM voice",           enabledInit,  v => {
                if (tts != null) tts.Enabled = v;
                OnTTSEnabledChanged?.Invoke(v);
            });
            _ttsAutoPlayRow = BuildToggleRow(controlsContainer.transform, "Auto-play DM voice", autoPlayInit, v => {
                if (tts != null) tts.AutoPlay = v;
                OnTTSAutoPlayChanged?.Invoke(v);
            });
        }

        private GameObject BuildToggleRow(Transform parent, string label, bool initialValue, System.Action<bool> onChanged)
        {
            var row = new GameObject($"ToggleRow_{label}", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(4, 4, 2, 2);
            hlg.spacing = 6;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 26;
            le.flexibleWidth = 1;

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(row.transform, false);
            labelGO.AddComponent<LayoutElement>().preferredWidth = 220;
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 10f; tmp.color = UITheme.SystemText;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            var tgGO = new GameObject("Toggle", typeof(RectTransform));
            tgGO.transform.SetParent(row.transform, false);
            tgGO.AddComponent<LayoutElement>().preferredWidth = 40;
            var bg = tgGO.AddComponent<Image>();
            bg.color = initialValue ? UITheme.GoldAccent : new Color(0.2f, 0.15f, 0.1f);
            var btn = tgGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() =>
            {
                bool newVal = !(bg.color == (Color)UITheme.GoldAccent);
                bg.color = newVal ? UITheme.GoldAccent : new Color(0.2f, 0.15f, 0.1f);
                onChanged?.Invoke(newVal);
            });

            return row;
        }
    }
}
