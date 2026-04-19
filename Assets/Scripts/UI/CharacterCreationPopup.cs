// Assets/Scripts/UI/CharacterCreationPopup.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DnD.Core;
using DnD.Data;
using DNDLLM.Services;

namespace DnD.UI
{
    public class CharacterCreationPopup : MonoBehaviour
    {
        [Header("Step Panels (0=Name 1=Race 2=Class 3=Appearance 4=Backstory 5=Confirm)")]
        [SerializeField] private GameObject[] stepPanels;

        [Header("Step 0 — Name")]
        [SerializeField] private TMP_InputField nameInput;

        [Header("Step 1 — Race (container; buttons built at runtime)")]
        [SerializeField] private RectTransform raceGridContainer;

        [Header("Step 2 — Class (container; buttons built at runtime)")]
        [SerializeField] private RectTransform classGridContainer;

        [Header("Step 3 — Appearance")]
        [SerializeField] private TMP_InputField appearanceInput;

        [Header("Step 4 — Backstory")]
        [SerializeField] private TMP_InputField backstoryInput;

        [Header("Step 5 — Confirm")]
        [SerializeField] private RawImage  portraitImage;
        [SerializeField] private TMP_Text  statsText;
        [SerializeField] private Button    beginButton;

        [Header("Navigation")]
        [SerializeField] private Button    nextButton;
        [SerializeField] private Button    backButton;
        [SerializeField] private Button    cancelButton;

        [Header("Step Indicator")]
        [SerializeField] private Image[]   stepBars;   // 5 bars
        [SerializeField] private TMP_Text  stepLabel;

        // ── Public ──────────────────────────────────────────────────────
        public int TargetSlotIndex { get; set; }
        public Action<CharacterCreationData> OnComplete;
        public Action OnCancelled;

        // ── Private state ────────────────────────────────────────────────
        private int               _step;
        private Race              _selectedRace;
        private CharacterClassName _selectedClass;
        private bool              _raceSelected;
        private bool              _classSelected;
        private Texture2D         _portrait;
        private Button[]          _raceButtons;
        private Button[]          _classButtons;

        private static readonly string[] StepLabels =
        {
            "Step 1 of 5 — NAME",
            "Step 2 of 5 — RACE",
            "Step 3 of 5 — CLASS",
            "Step 4 of 5 — APPEARANCE",
            "Step 5 of 5 — BACKSTORY",
            "YOUR HERO AWAITS"
        };

        private static readonly Color32 BarActive   = new Color32(0xC8, 0xA0, 0x50, 0xFF);
        private static readonly Color32 BarInactive = new Color32(0x4A, 0x38, 0x20, 0xFF);
        private static readonly Color32 BtnSelected = new Color32(0xC8, 0xA0, 0x50, 0xFF);
        private static readonly Color32 BtnNormal   = new Color32(0x2A, 0x1F, 0x0E, 0xFF);

        private void Start()
        {
            nextButton.onClick.AddListener(OnNext);
            backButton.onClick.AddListener(OnBack);
            cancelButton.onClick.AddListener(OnCancel);
            beginButton.onClick.AddListener(OnBeginAdventure);
            beginButton.interactable = false;

            BuildRaceButtons();
            BuildClassButtons();

            ShowStep(0);
        }

        // ── Called by GameManager to open the popup ─────────────────────
        public void Open(int slotIndex)
        {
            TargetSlotIndex = slotIndex;
            _raceSelected   = false;
            _classSelected  = false;
            _portrait       = null;
            nameInput.text        = "";
            appearanceInput.text  = "";
            backstoryInput.text   = "";
            if (portraitImage != null) portraitImage.texture = null;
            gameObject.SetActive(true);
            ShowStep(0);
        }

        // ── Step navigation ──────────────────────────────────────────────
        private void ShowStep(int step)
        {
            _step = step;
            for (int i = 0; i < stepPanels.Length; i++)
                if (stepPanels[i] != null) stepPanels[i].SetActive(i == step);

            // Fill bars: all bars before current step are active
            for (int i = 0; i < stepBars.Length; i++)
                stepBars[i].color = (i < Mathf.Max(step, 1)) ? (Color)BarActive : (Color)BarInactive;

            if (stepLabel != null)
                stepLabel.text = StepLabels[Mathf.Min(step, StepLabels.Length - 1)];

            bool isConfirm = step == 5;
            backButton.gameObject.SetActive(step > 0 && !isConfirm);
            cancelButton.gameObject.SetActive(!isConfirm);
            nextButton.gameObject.SetActive(!isConfirm);

            if (isConfirm) _ = GeneratePortraitAsync();
        }

        private bool CanAdvance()
        {
            switch (_step)
            {
                case 0: return !string.IsNullOrWhiteSpace(nameInput.text);
                case 1: return _raceSelected;
                case 2: return _classSelected;
                case 3: return !string.IsNullOrWhiteSpace(appearanceInput.text);
                case 4: return true;   // backstory optional
                default: return false;
            }
        }

        private void OnNext()
        {
            if (CanAdvance()) ShowStep(_step + 1);
        }

        private void OnBack()
        {
            if (_step > 0) ShowStep(_step - 1);
        }

        private void OnCancel()
        {
            gameObject.SetActive(false);
            OnCancelled?.Invoke();
        }

        // ── Race / Class button grids ────────────────────────────────────
        private void BuildRaceButtons()
        {
            var races = (Race[])Enum.GetValues(typeof(Race));
            _raceButtons = new Button[races.Length];
            for (int i = 0; i < races.Length; i++)
            {
                int idx = i;
                var btn = CreateOptionButton(races[i].ToString(), raceGridContainer);
                btn.onClick.AddListener(() => SelectRace(idx));
                _raceButtons[idx] = btn;
            }
        }

        private void BuildClassButtons()
        {
            var classes = (CharacterClassName[])Enum.GetValues(typeof(CharacterClassName));
            _classButtons = new Button[classes.Length];
            for (int i = 0; i < classes.Length; i++)
            {
                int idx = i;
                var btn = CreateOptionButton(classes[i].ToString(), classGridContainer);
                btn.onClick.AddListener(() => SelectClass(idx));
                _classButtons[idx] = btn;
            }
        }

        private Button CreateOptionButton(string label, RectTransform parent)
        {
            var go  = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = BtnNormal;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color32(0x3A, 0x2F, 0x1E, 0xFF);
            btn.colors = colors;
            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var trt = textGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 12f;
            tmp.color = UITheme.DmText;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            return btn;
        }

        private void SelectRace(int idx)
        {
            _selectedRace = (Race)idx;
            _raceSelected = true;
            for (int i = 0; i < _raceButtons.Length; i++)
                _raceButtons[i].GetComponent<Image>().color = i == idx ? (Color)BtnSelected : (Color)BtnNormal;
        }

        private void SelectClass(int idx)
        {
            _selectedClass = (CharacterClassName)idx;
            _classSelected = true;
            for (int i = 0; i < _classButtons.Length; i++)
                _classButtons[i].GetComponent<Image>().color = i == idx ? (Color)BtnSelected : (Color)BtnNormal;
        }

        // ── Portrait generation ──────────────────────────────────────────
        private async Task GeneratePortraitAsync()
        {
            beginButton.interactable = false;
            if (statsText != null)
                statsText.text = $"{nameInput.text.Trim()}\n{_selectedRace} {_selectedClass} · Lv1\n\nGenerating portrait...";

            string prompt = $"Fantasy RPG character portrait, {_selectedRace} {_selectedClass}, " +
                            $"{appearanceInput.text.Trim()}, painterly art style, " +
                            "face and shoulders, plain dark background.";

            var generateTask = LLMService.Instance.GenerateImage(prompt);
            var timeoutTask  = Task.Delay(30000);
            var completed    = await Task.WhenAny(generateTask, timeoutTask);

            if (completed == generateTask && generateTask.Result != null)
            {
                _portrait = generateTask.Result;
                if (portraitImage != null) portraitImage.texture = _portrait;
            }

            if (statsText != null)
                statsText.text = $"{nameInput.text.Trim()}\n{_selectedRace} {_selectedClass} · Lv1";

            if (beginButton != null) beginButton.interactable = true;
        }

        // ── Confirm ──────────────────────────────────────────────────────
        private void OnBeginAdventure()
        {
            var data = new CharacterCreationData
            {
                characterName         = nameInput.text.Trim(),
                race                  = _selectedRace,
                characterClass        = _selectedClass,
                appearanceDescription = appearanceInput.text.Trim(),
                backstory             = backstoryInput.text.Trim(),
                portrait              = _portrait
            };
            gameObject.SetActive(false);
            OnComplete?.Invoke(data);
        }
    }
}
