// Assets/Scripts/UI/CharacterCreationPopup.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DnD.Core;
using DnD.Character;
using DnD.Data;
using DNDLLM.Services;

namespace DnD.UI
{
    /// <summary>
    /// Step indices:
    ///   0 = ModeSelect      (Quick Create vs Step by Step)
    ///   1 = QuickCreate     (free-text description → LLM pre-fill)
    ///   2 = Name
    ///   3 = Race
    ///   4 = Class
    ///   5 = Appearance
    ///   6 = Backstory
    ///   7 = AbilityScores   (point-buy; LLM can suggest from description)
    ///   8 = Confirm
    /// </summary>
    public class CharacterCreationPopup : MonoBehaviour
    {
        [Header("Step Panels (0=Mode 1=Quick 2=Name 3=Race 4=Class 5=App 6=Back 7=Abilities 8=Confirm)")]
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

        [Header("Step 7 — Ability Scores")]
        [SerializeField] private TMP_Text   abilityPointsRemaining;   // "Points: 15 / 27"
        [SerializeField] private TMP_Text   abilityStatusText;        // "AI is suggesting..." feedback
        [SerializeField] private Button     abilitySuggestButton;     // trigger LLM pre-fill
        // Six rows: each has a value display label and +/- buttons wired at runtime
        [SerializeField] private TMP_Text[] abilityValueLabels;       // [0]=STR … [5]=CHA
        [SerializeField] private TMP_Text[] abilityModLabels;         // "+2" etc.

        [Header("Step 8 — Confirm")]
        [SerializeField] private RawImage portraitImage;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Button   beginButton;

        [Header("Navigation")]
        [SerializeField] private Button    nextButton;
        [SerializeField] private Button    backButton;
        [SerializeField] private Button    cancelButton;
        [SerializeField] private TMP_Text  nextButtonLabel;

        [Header("Step Indicator")]
        [SerializeField] private Image[]    stepBars;         // 6 thin bars (steps 2–7 = 6 visible steps)
        [SerializeField] private TMP_Text   stepLabel;
        [SerializeField] private GameObject stepIndicatorRow;

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

        // Ability score point-buy
        // Point costs per score (index = score - 8): 8=0, 9=1, 10=2, 11=3, 12=4, 13=5, 14=7, 15=9
        private static readonly int[] PointCost = { 0, 1, 2, 3, 4, 5, 7, 9 };
        private const int TOTAL_POINTS = 27;
        private const int SCORE_MIN    = 8;
        private const int SCORE_MAX    = 15;
        private readonly int[]  _abilityScores = { 8, 8, 8, 8, 8, 8 };
        private static readonly string[] AbilityNames = { "STR", "DEX", "CON", "INT", "WIS", "CHA" };

        private static readonly string[] StepLabels =
        {
            "",                              // 0: ModeSelect
            "QUICK CREATE",                  // 1: QuickCreate
            "Step 1 of 6 — NAME",            // 2: Name
            "Step 2 of 6 — RACE",            // 3: Race
            "Step 3 of 6 — CLASS",           // 4: Class
            "Step 4 of 6 — APPEARANCE",      // 5: Appearance
            "Step 5 of 6 — BACKSTORY",       // 6: Backstory
            "Step 6 of 6 — ABILITY SCORES",  // 7: AbilityScores
            "YOUR HERO AWAITS",              // 8: Confirm
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

            if (abilitySuggestButton != null)
                abilitySuggestButton.onClick.AddListener(() => _ = SuggestAbilitiesAsync());

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

            ResetAbilityScores();
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

            bool showIndicator = step >= 2;
            if (stepIndicatorRow != null) stepIndicatorRow.SetActive(showIndicator);

            if (showIndicator && stepBars != null)
            {
                // Steps 2-8: 6 steps visible, step 2 → bar 0 filled, step 8 → all 6
                int fillCount = Mathf.Clamp(step - 1, 0, stepBars.Length);
                for (int i = 0; i < stepBars.Length; i++)
                    if (stepBars[i] != null)
                        stepBars[i].color = i < fillCount ? (Color)BarActive : (Color)BarInactive;
            }

            if (stepLabel != null)
                stepLabel.text = step < StepLabels.Length ? StepLabels[step] : "";

            bool isConfirm    = step == 8;
            bool isModeSelect = step == 0;

            if (cancelButton != null) cancelButton.gameObject.SetActive(!isConfirm && !isModeSelect);
            if (backButton   != null) backButton.gameObject.SetActive(step > 0 && !isConfirm && !isModeSelect);
            if (nextButton   != null) nextButton.gameObject.SetActive(!isConfirm && !isModeSelect);

            if (nextButtonLabel != null)
                nextButtonLabel.text = step == 1 ? "Generate" : "Next >";

            if (step == 7) RefreshAbilityUI();
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
                case 6: return true;
                case 7: return PointsSpent() <= TOTAL_POINTS;
                default: return false;
            }
        }

        private void OnNext()
        {
            if (_step == 1) { if (CanAdvance()) _ = QuickCreateAsync(); return; }
            if (CanAdvance()) ShowStep(_step + 1);
        }

        private void OnBack()
        {
            if (_step <= 0) return;
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
                            $"BACKSTORY: [character background, 1-3 sentences]\n" +
                            $"STR: [8-15]\nDEX: [8-15]\nCON: [8-15]\nINT: [8-15]\nWIS: [8-15]\nCHA: [8-15]\n\n" +
                            $"Character description: {desc}";

            string name = "", race = "", cls = "", appearance = "", backstory = "";
            int[] suggestedScores = null;

            try
            {
                string response = await LLMService.Instance.SendPrompt(system, user);
                var parsed = new Dictionary<string, string>();
                foreach (var rawLine in response.Split('\n'))
                {
                    string line = rawLine.Trim();
                    int colon = line.IndexOf(':');
                    if (colon < 0) continue;
                    string key = line.Substring(0, colon).Trim().ToUpper();
                    string val = line.Substring(colon + 1).Trim();
                    parsed[key] = val;
                }
                parsed.TryGetValue("NAME",       out name);
                parsed.TryGetValue("RACE",       out race);
                parsed.TryGetValue("CLASS",      out cls);
                parsed.TryGetValue("APPEARANCE", out appearance);
                parsed.TryGetValue("BACKSTORY",  out backstory);

                suggestedScores = ParseAbilityScores(parsed);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterCreationPopup] Quick create parse failed: {e.Message}");
            }

            if (nameInput != null)
                nameInput.text = !string.IsNullOrEmpty(name) ? name
                                 : (desc.Length > 0 ? desc.Split(' ')[0] : "Hero");

            if (!string.IsNullOrEmpty(race) &&
                Enum.TryParse<Race>(race.Replace("-","").Replace(" ","").Replace("'",""), true, out var parsedRace))
                SelectRace((int)parsedRace);
            else SelectRace(0);

            if (!string.IsNullOrEmpty(cls) &&
                Enum.TryParse<CharacterClassName>(cls.Replace(" ",""), true, out var parsedClass))
                SelectClass((int)parsedClass);
            else SelectClass(0);

            if (appearanceInput != null)
                appearanceInput.text = !string.IsNullOrEmpty(appearance) ? appearance : desc;
            if (backstoryInput != null)
                backstoryInput.text = backstory;

            if (suggestedScores != null)
                ApplyAbilityScores(suggestedScores);

            if (quickStatusText != null) quickStatusText.text = "";
            if (nextButton != null) nextButton.interactable = true;

            ShowStep(7); // Jump to ability scores so player can review/adjust
        }

        // ── Ability Scores ────────────────────────────────────────────────

        private void ResetAbilityScores()
        {
            for (int i = 0; i < 6; i++) _abilityScores[i] = 8;
        }

        private int PointsSpent()
        {
            int spent = 0;
            foreach (int s in _abilityScores)
                spent += PointCost[Mathf.Clamp(s - SCORE_MIN, 0, PointCost.Length - 1)];
            return spent;
        }

        private int PointsRemaining() => TOTAL_POINTS - PointsSpent();

        /// <summary>Called by UISceneBuilder-wired +/- buttons.</summary>
        public void AdjustAbilityScore(int abilityIndex, int delta) => AdjustScore(abilityIndex, delta);

        private void AdjustScore(int abilityIndex, int delta)
        {
            int current = _abilityScores[abilityIndex];
            int newScore = Mathf.Clamp(current + delta, SCORE_MIN, SCORE_MAX);
            int newSpent = PointsSpent()
                - PointCost[current - SCORE_MIN]
                + PointCost[newScore - SCORE_MIN];

            if (newSpent > TOTAL_POINTS && delta > 0) return; // can't afford
            _abilityScores[abilityIndex] = newScore;
            RefreshAbilityUI();
        }

        private void RefreshAbilityUI()
        {
            if (abilityPointsRemaining != null)
                abilityPointsRemaining.text = $"Points remaining: {PointsRemaining()} / {TOTAL_POINTS}";

            for (int i = 0; i < 6; i++)
            {
                if (abilityValueLabels != null && i < abilityValueLabels.Length && abilityValueLabels[i] != null)
                    abilityValueLabels[i].text = _abilityScores[i].ToString();

                if (abilityModLabels != null && i < abilityModLabels.Length && abilityModLabels[i] != null)
                {
                    int mod = ((_abilityScores[i] - 10) / 2);
                    abilityModLabels[i].text = mod >= 0 ? $"+{mod}" : mod.ToString();
                }
            }
        }

        private void ApplyAbilityScores(int[] scores)
        {
            if (scores == null || scores.Length < 6) return;
            for (int i = 0; i < 6; i++)
                _abilityScores[i] = Mathf.Clamp(scores[i], SCORE_MIN, SCORE_MAX);
            // Clamp to point-buy budget if over
            while (PointsSpent() > TOTAL_POINTS)
            {
                // Reduce the highest score by 1 to fit
                int highest = 0;
                for (int i = 1; i < 6; i++)
                    if (_abilityScores[i] > _abilityScores[highest]) highest = i;
                _abilityScores[highest]--;
            }
        }

        private int[] ParseAbilityScores(Dictionary<string, string> parsed)
        {
            int[] scores = new int[6];
            string[] keys = { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
            bool anyFound = false;
            for (int i = 0; i < 6; i++)
            {
                if (parsed.TryGetValue(keys[i], out string val)
                    && int.TryParse(val.Trim(), out int v))
                {
                    scores[i] = v;
                    anyFound = true;
                }
                else scores[i] = 8;
            }
            return anyFound ? scores : null;
        }

        /// <summary>Calls the LLM to suggest ability score allocation based on character description.</summary>
        private async Task SuggestAbilitiesAsync()
        {
            if (abilitySuggestButton != null) abilitySuggestButton.interactable = false;
            if (abilityStatusText   != null) abilityStatusText.text = "AI is suggesting scores...";

            string raceName    = _raceSelected  ? _selectedRace.ToString()  : "Human";
            string className   = _classSelected ? _selectedClass.ToString() : "Adventurer";
            string appearance  = appearanceInput  != null ? appearanceInput.text.Trim()  : "";
            string backstory   = backstoryInput   != null ? backstoryInput.text.Trim()   : "";
            string description = $"{raceName} {className}. {appearance} {backstory}".Trim();

            string system = "You are a D&D 5e character builder. Suggest ability scores using point-buy (total ≤ 27 points, scores 8-15).";
            string user   = $"Suggest fitting ability scores for: {description}\n" +
                            "Reply ONLY with these 6 lines:\n" +
                            "STR: [8-15]\nDEX: [8-15]\nCON: [8-15]\nINT: [8-15]\nWIS: [8-15]\nCHA: [8-15]";

            try
            {
                string response = await LLMService.Instance.SendPrompt(system, user);
                var parsed = new Dictionary<string, string>();
                foreach (var rawLine in response.Split('\n'))
                {
                    string line = rawLine.Trim();
                    int colon = line.IndexOf(':');
                    if (colon < 0) continue;
                    parsed[line.Substring(0, colon).Trim().ToUpper()] = line.Substring(colon + 1).Trim();
                }
                var scores = ParseAbilityScores(parsed);
                if (scores != null) ApplyAbilityScores(scores);
                if (abilityStatusText != null) abilityStatusText.text = "Scores suggested! Adjust as you like.";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterCreationPopup] Ability score suggestion failed: {e.Message}");
                if (abilityStatusText != null) abilityStatusText.text = "Suggestion failed — set scores manually.";
            }

            RefreshAbilityUI();
            if (abilitySuggestButton != null) abilitySuggestButton.interactable = true;
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
            tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
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
            {
                string abStr = $"STR {_abilityScores[0]}  DEX {_abilityScores[1]}  CON {_abilityScores[2]}\n" +
                               $"INT {_abilityScores[3]}  WIS {_abilityScores[4]}  CHA {_abilityScores[5]}";
                statsText.text = $"{charName}\n{raceName} {className} · Lv1\n\n{abStr}\n\n<i>Generating portrait...</i>";
            }

            string prompt = $"Square, 1:1 aspect ratio. Fantasy RPG character portrait, {raceName} {className}" +
                            (string.IsNullOrEmpty(appearance) ? "" : $", {appearance}") +
                            ". Painterly RPG art style, face and torso visible, centered in frame, " +
                            "plain solid dark background filling all corners. " +
                            "Exactly square composition, no letterboxing, no borders.";

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
            {
                string abStr = $"STR {_abilityScores[0]}  DEX {_abilityScores[1]}  CON {_abilityScores[2]}\n" +
                               $"INT {_abilityScores[3]}  WIS {_abilityScores[4]}  CHA {_abilityScores[5]}";
                statsText.text = $"{charName}\n{raceName} {className} · Lv1\n\n{abStr}{portraitNote}";
            }

            if (beginButton != null) beginButton.interactable = true;
        }

        // ── Confirm ──────────────────────────────────────────────────────
        private void OnBeginAdventure()
        {
            var data = new CharacterCreationData
            {
                characterName         = nameInput       != null ? nameInput.text.Trim()       : "Hero",
                race                  = _selectedRace,
                characterClass        = _selectedClass,
                appearanceDescription = appearanceInput != null ? appearanceInput.text.Trim() : "",
                backstory             = backstoryInput  != null ? backstoryInput.text.Trim()  : "",
                abilities             = new AbilityScores(
                    _abilityScores[0], _abilityScores[1], _abilityScores[2],
                    _abilityScores[3], _abilityScores[4], _abilityScores[5]),
                portrait              = _portrait
            };
            gameObject.SetActive(false);
            OnComplete?.Invoke(data);
        }
    }
}
