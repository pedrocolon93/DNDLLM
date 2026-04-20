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
    /// <summary>
    /// Step indices:
    ///   0 = ModeSelect  (Quick Create vs Step by Step)
    ///   1 = QuickCreate (free-text description → LLM pre-fill)
    ///   2 = Name
    ///   3 = Race
    ///   4 = Class
    ///   5 = Appearance
    ///   6 = Backstory
    ///   7 = Confirm
    /// </summary>
    public class CharacterCreationPopup : MonoBehaviour
    {
        [Header("Step Panels (0=Mode 1=Quick 2=Name 3=Race 4=Class 5=Appearance 6=Backstory 7=Confirm)")]
        [SerializeField] private GameObject[] stepPanels;

        [Header("Step 0 — Mode Select")]
        [SerializeField] private Button modeQuickButton;
        [SerializeField] private Button modeStepButton;

        [Header("Step 1 — Quick Create")]
        [SerializeField] private TMP_InputField quickDescriptionInput;
        [SerializeField] private TMP_Text       quickStatusText;

        [Header("Step 2 — Name")]
        [SerializeField] private TMP_InputField nameInput;

        [Header("Step 3 — Race (container; buttons built at runtime)")]
        [SerializeField] private RectTransform raceGridContainer;

        [Header("Step 4 — Class (container; buttons built at runtime)")]
        [SerializeField] private RectTransform classGridContainer;

        [Header("Step 5 — Appearance")]
        [SerializeField] private TMP_InputField appearanceInput;

        [Header("Step 6 — Backstory")]
        [SerializeField] private TMP_InputField backstoryInput;

        [Header("Step 7 — Confirm")]
        [SerializeField] private RawImage portraitImage;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Button   beginButton;

        [Header("Navigation")]
        [SerializeField] private Button    nextButton;
        [SerializeField] private Button    backButton;
        [SerializeField] private Button    cancelButton;
        [SerializeField] private TMP_Text  nextButtonLabel;  // text child of nextButton

        [Header("Step Indicator")]
        [SerializeField] private Image[]    stepBars;          // 5 thin bars
        [SerializeField] private TMP_Text   stepLabel;
        [SerializeField] private GameObject stepIndicatorRow;  // hide on steps 0-1

        // ── Public ──────────────────────────────────────────────────────
        public int TargetSlotIndex { get; set; }
        public Action<CharacterCreationData> OnComplete;
        public Action OnCancelled;

        // ── Private state ────────────────────────────────────────────────
        private int                _step;
        private Race               _selectedRace;
        private CharacterClassName _selectedClass;
        private bool               _raceSelected;
        private bool               _classSelected;
        private Texture2D          _portrait;
        private Button[]           _raceButtons;
        private Button[]           _classButtons;

        private static readonly string[] StepLabels =
        {
            "",                             // 0: ModeSelect
            "QUICK CREATE",                 // 1: QuickCreate
            "Step 1 of 5 — NAME",           // 2: Name
            "Step 2 of 5 — RACE",           // 3: Race
            "Step 3 of 5 — CLASS",          // 4: Class
            "Step 4 of 5 — APPEARANCE",     // 5: Appearance
            "Step 5 of 5 — BACKSTORY",      // 6: Backstory
            "YOUR HERO AWAITS",             // 7: Confirm
        };

        private static readonly Color32 BarActive   = new Color32(0xC8, 0xA0, 0x50, 0xFF);
        private static readonly Color32 BarInactive = new Color32(0x4A, 0x38, 0x20, 0xFF);
        private static readonly Color32 BtnSelected = new Color32(0xC8, 0xA0, 0x50, 0xFF);
        private static readonly Color32 BtnNormal   = new Color32(0x2A, 0x1F, 0x0E, 0xFF);

        private void Start()
        {
            if (nextButton == null || backButton == null || cancelButton == null || beginButton == null)
            {
                Debug.LogError("[CharacterCreationPopup] One or more navigation buttons are not assigned.", this);
                return;
            }

            nextButton.onClick.AddListener(OnNext);
            backButton.onClick.AddListener(OnBack);
            cancelButton.onClick.AddListener(OnCancel);
            beginButton.onClick.AddListener(OnBeginAdventure);
            beginButton.interactable = false;

            if (modeQuickButton != null) modeQuickButton.onClick.AddListener(SelectModeQuickCreate);
            if (modeStepButton  != null) modeStepButton.onClick.AddListener(SelectModeStepByStep);

            BuildRaceButtons();
            BuildClassButtons();

            ShowStep(0);
        }

        // ── Called by GameManager ────────────────────────────────────────
        public void Open(int slotIndex)
        {
            TargetSlotIndex  = slotIndex;
            _raceSelected    = false;
            _classSelected   = false;
            _portrait        = null;

            if (nameInput               != null) nameInput.text               = "";
            if (appearanceInput         != null) appearanceInput.text         = "";
            if (backstoryInput          != null) backstoryInput.text          = "";
            if (quickDescriptionInput   != null) quickDescriptionInput.text   = "";
            if (portraitImage           != null) { portraitImage.texture = null; portraitImage.color = Color.clear; }

            gameObject.SetActive(true);
            ShowStep(0);
        }

        // ── Mode buttons ─────────────────────────────────────────────────
        public void SelectModeQuickCreate() => ShowStep(1);
        public void SelectModeStepByStep()  => ShowStep(2);

        // ── Step navigation ──────────────────────────────────────────────
        private void ShowStep(int step)
        {
            _step = step;

            for (int i = 0; i < stepPanels.Length; i++)
                if (stepPanels[i] != null) stepPanels[i].SetActive(i == step);

            // Step indicator only visible for steps 2-7
            bool showIndicator = step >= 2;
            if (stepIndicatorRow != null) stepIndicatorRow.SetActive(showIndicator);

            if (showIndicator && stepBars != null)
            {
                // step 2 → 1 bar filled, step 6 → 5 bars filled, step 7 → all 5
                int fillCount = Mathf.Clamp(step - 1, 0, 5);
                for (int i = 0; i < stepBars.Length; i++)
                    if (stepBars[i] != null)
                        stepBars[i].color = i < fillCount ? (Color)BarActive : (Color)BarInactive;
            }

            if (stepLabel != null)
                stepLabel.text = step < StepLabels.Length ? StepLabels[step] : "";

            bool isConfirm    = step == 7;
            bool isModeSelect = step == 0;

            if (cancelButton != null) cancelButton.gameObject.SetActive(!isConfirm && !isModeSelect);
            if (backButton   != null) backButton.gameObject.SetActive(step > 0 && !isConfirm && !isModeSelect);
            if (nextButton   != null) nextButton.gameObject.SetActive(!isConfirm && !isModeSelect);

            // Label the action button contextually
            if (nextButtonLabel != null)
                nextButtonLabel.text = step == 1 ? "Generate" : "Next >";

            if (isConfirm) _ = GeneratePortraitAsync();
        }

        private bool CanAdvance()
        {
            switch (_step)
            {
                case 1: return !string.IsNullOrWhiteSpace(quickDescriptionInput?.text);
                case 2: return !string.IsNullOrWhiteSpace(nameInput?.text);
                case 3: return _raceSelected;
                case 4: return _classSelected;
                case 5: return !string.IsNullOrWhiteSpace(appearanceInput?.text);
                case 6: return true;   // backstory optional
                default: return false;
            }
        }

        private void OnNext()
        {
            if (_step == 1)      // Quick Create: fire LLM parse then jump to Confirm
            {
                if (CanAdvance()) _ = QuickCreateAsync();
                return;
            }
            if (CanAdvance()) ShowStep(_step + 1);
        }

        private void OnBack()
        {
            if (_step <= 0) return;
            // From Name (2) or Quick (1) go back to Mode Select (0)
            int prev = (_step == 2 || _step == 1) ? 0 : _step - 1;
            ShowStep(prev);
        }

        private void OnCancel()
        {
            gameObject.SetActive(false);
            OnCancelled?.Invoke();
        }

        // ── Quick Create ─────────────────────────────────────────────────
        private async Task QuickCreateAsync()
        {
            if (quickStatusText != null) quickStatusText.text = "Analyzing your character...";
            if (nextButton != null) nextButton.interactable = false;

            string desc   = quickDescriptionInput != null ? quickDescriptionInput.text.Trim() : "";
            string system = "You are a D&D character parser. Extract character details from the user description.";
            string user   = $"Parse this D&D character description and return EXACTLY this format, one field per line:\n" +
                            $"NAME: [character name]\n" +
                            $"RACE: [one of: Human, Elf, Dwarf, Halfling, Dragonborn, Gnome, HalfElf, HalfOrc, Tiefling]\n" +
                            $"CLASS: [one of: Fighter, Wizard, Rogue, Cleric, Barbarian, Ranger, Paladin, Monk, Bard, Druid, Warlock, Sorcerer]\n" +
                            $"APPEARANCE: [physical description, 1-2 sentences]\n" +
                            $"BACKSTORY: [character background, 1-3 sentences]\n\n" +
                            $"Character description: {desc}";

            string name = "", race = "", cls = "", appearance = "", backstory = "";

            try
            {
                string response = await LLMService.Instance.SendPrompt(system, user);
                foreach (var rawLine in response.Split('\n'))
                {
                    string line = rawLine.Trim();
                    if      (line.StartsWith("NAME:"))       name       = line.Substring(5).Trim();
                    else if (line.StartsWith("RACE:"))       race       = line.Substring(5).Trim();
                    else if (line.StartsWith("CLASS:"))      cls        = line.Substring(6).Trim();
                    else if (line.StartsWith("APPEARANCE:")) appearance = line.Substring(11).Trim();
                    else if (line.StartsWith("BACKSTORY:"))  backstory  = line.Substring(10).Trim();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterCreationPopup] Quick create parse failed: {e.Message}");
            }

            // Apply parsed values (fall back to desc-based defaults if parsing failed)
            if (nameInput != null)
                nameInput.text = !string.IsNullOrEmpty(name) ? name
                                 : (desc.Length > 0 ? desc.Split(' ')[0] : "Hero");

            if (!string.IsNullOrEmpty(race) &&
                Enum.TryParse<Race>(race.Replace("-","").Replace(" ","").Replace("'",""), true, out var parsedRace))
                SelectRace((int)parsedRace);
            else
                SelectRace(0); // Default Human

            if (!string.IsNullOrEmpty(cls) &&
                Enum.TryParse<CharacterClassName>(cls.Replace(" ",""), true, out var parsedClass))
                SelectClass((int)parsedClass);
            else
                SelectClass(0); // Default Fighter

            if (appearanceInput != null)
                appearanceInput.text = !string.IsNullOrEmpty(appearance) ? appearance : desc;

            if (backstoryInput != null)
                backstoryInput.text = backstory;

            if (quickStatusText != null) quickStatusText.text = "";
            if (nextButton != null) nextButton.interactable = true;

            ShowStep(7); // Jump straight to Confirm
        }

        // ── Race / Class button grids ────────────────────────────────────
        private void BuildRaceButtons()
        {
            if (raceGridContainer == null) return;
            var races = (Race[])Enum.GetValues(typeof(Race));
            _raceButtons = new Button[races.Length];
            for (int i = 0; i < races.Length; i++)
            {
                int idx = i;
                var btn = CreateOptionButton(races[i].ToString(), raceGridContainer);
                if (btn != null)
                {
                    btn.onClick.AddListener(() => SelectRace(idx));
                    _raceButtons[idx] = btn;
                }
            }
        }

        private void BuildClassButtons()
        {
            if (classGridContainer == null) return;
            var classes = (CharacterClassName[])Enum.GetValues(typeof(CharacterClassName));
            _classButtons = new Button[classes.Length];
            for (int i = 0; i < classes.Length; i++)
            {
                int idx = i;
                var btn = CreateOptionButton(classes[i].ToString(), classGridContainer);
                if (btn != null)
                {
                    btn.onClick.AddListener(() => SelectClass(idx));
                    _classButtons[idx] = btn;
                }
            }
        }

        private Button CreateOptionButton(string label, RectTransform parent)
        {
            if (parent == null) return null;
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
            if (_raceButtons == null) return;
            for (int i = 0; i < _raceButtons.Length; i++)
                if (_raceButtons[i] != null)
                    _raceButtons[i].GetComponent<Image>().color = i == idx ? (Color)BtnSelected : (Color)BtnNormal;
        }

        private void SelectClass(int idx)
        {
            _selectedClass = (CharacterClassName)idx;
            _classSelected = true;
            if (_classButtons == null) return;
            for (int i = 0; i < _classButtons.Length; i++)
                if (_classButtons[i] != null)
                    _classButtons[i].GetComponent<Image>().color = i == idx ? (Color)BtnSelected : (Color)BtnNormal;
        }

        // ── Portrait generation ──────────────────────────────────────────
        private async Task GeneratePortraitAsync()
        {
            if (beginButton != null) beginButton.interactable = false;

            string raceName   = _raceSelected  ? _selectedRace.ToString()   : "Human";
            string className  = _classSelected ? _selectedClass.ToString()  : "Adventurer";
            string charName   = nameInput != null ? nameInput.text.Trim()   : "Hero";
            string appearance = appearanceInput != null ? appearanceInput.text.Trim() : "";

            if (statsText != null)
                statsText.text = $"{charName}\n{raceName} {className} · Lv1\n\n<i>Generating portrait...</i>";

            string prompt = $"Fantasy RPG character portrait, {raceName} {className}" +
                            (string.IsNullOrEmpty(appearance) ? "" : $", {appearance}") +
                            ", painterly art style, face and shoulders, plain dark background.";

            var generateTask = LLMService.Instance.GenerateImage(prompt);
            var timeoutTask  = Task.Delay(30000);
            var completed    = await Task.WhenAny(generateTask, timeoutTask);

            if (completed == generateTask)
            {
                try
                {
                    _portrait = await generateTask;
                    if (_portrait != null && portraitImage != null)
                    {
                        portraitImage.texture = _portrait;
                        portraitImage.color   = Color.white;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CharacterCreationPopup] Portrait generation failed: {e.Message}");
                }
            }

            string portraitNote = _portrait == null
                ? "\n<size=10><color=#666666>Portrait unavailable — check API key</color></size>"
                : "";
            if (statsText != null)
                statsText.text = $"{charName}\n{raceName} {className} · Lv1{portraitNote}";

            if (beginButton != null) beginButton.interactable = true;
        }

        // ── Confirm ──────────────────────────────────────────────────────
        private void OnBeginAdventure()
        {
            var data = new CharacterCreationData
            {
                characterName         = nameInput        != null ? nameInput.text.Trim()        : "Hero",
                race                  = _selectedRace,
                characterClass        = _selectedClass,
                appearanceDescription = appearanceInput  != null ? appearanceInput.text.Trim()  : "",
                backstory             = backstoryInput   != null ? backstoryInput.text.Trim()   : "",
                portrait              = _portrait
            };
            gameObject.SetActive(false);
            OnComplete?.Invoke(data);
        }
    }
}
