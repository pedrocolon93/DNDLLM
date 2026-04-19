# Character Creation, Save/Load & Title Screen — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a step-by-step character creation popup with AI portrait generation, a 3-slot persistent save system that stores the full conversation history, and a full-screen title screen for New Game / Continue.

**Architecture:** Four new scripts (`SaveData`, `SaveSystem`, `CharacterCreationPopup`, `TitleScreen`) form the core. `UISceneBuilder` builds the two new Unity canvases (title + popup) via Editor menu items. `GameManager` ties everything together by showing the title screen on `MainMenu` state and the popup on `CharacterCreation` state. Chat messages are serialised into the save file so conversation history survives across sessions.

**Tech Stack:** Unity uGUI (Canvas/Button/TMP_InputField/RawImage/GridLayoutGroup), TextMeshPro, `JsonUtility` for persistence, `LLMService.GenerateImage` for portrait generation, `async/await` throughout.

---

## File Map

| Action | Path | Role |
|--------|------|------|
| Create | `Assets/Scripts/Data/SaveData.cs` | `SaveData`, `ChatMessageData`, `CharacterCreationData` data types |
| Create | `Assets/Scripts/Services/SaveSystem.cs` | Disk read/write for the 3 save slots |
| Modify | `Assets/Scripts/UI/ChatUI.cs` | Add `GetMessageHistory()` |
| Create | `Assets/Scripts/UI/CharacterCreationPopup.cs` | Step-by-step wizard MonoBehaviour |
| Create | `Assets/Scripts/UI/TitleScreen.cs` | Title screen MonoBehaviour |
| Modify | `Assets/Editor/UISceneBuilder.cs` | Add MENU button + two new `[MenuItem]` canvas builders |
| Modify | `Assets/Scripts/Managers/GameManager.cs` | Hook title screen and popup into state machine |

---

## Task 1: Data Types

**Files:**
- Create: `Assets/Scripts/Data/SaveData.cs`

- [ ] **Step 1: Create SaveData.cs**

```csharp
// Assets/Scripts/Data/SaveData.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using DnD.Core;

namespace DnD.Data
{
    [Serializable]
    public class SaveData
    {
        // Slot metadata
        public int    slotIndex;
        public string slotLabel;    // "Aric · Fighter · Lv3"
        public string lastPlayed;   // ISO-8601 UTC e.g. "2026-04-19T12:00:00Z"

        // Campaign
        public string campaignSeed;      // player's initial prompt
        public string campaignTimeline;  // DM's generated intro text

        // Character identity
        public string characterName;
        public string raceName;      // Race.ToString()
        public string className;     // CharacterClassName.ToString()
        public string appearanceDescription;
        public string backstory;

        // Character stats
        public int level;
        public int maxHP;
        public int currentHP;
        public int armorClass;
        public int str, dex, con, intel, wis, cha;

        // Game state
        public string gameState;   // GameState.ToString()

        // Full conversation history
        public List<ChatMessageData> messages;
    }

    [Serializable]
    public class ChatMessageData
    {
        public string type;   // "Player" | "DM" | "System"
        public string text;
    }

    // Passed from CharacterCreationPopup to GameManager on completion
    public struct CharacterCreationData
    {
        public string             characterName;
        public Race               race;
        public CharacterClassName characterClass;
        public string             appearanceDescription;
        public string             backstory;
        public Texture2D          portrait;  // null if generation timed out
    }
}
```

- [ ] **Step 2: Verify it compiles**

Open Unity. The Console should show no compile errors related to `SaveData.cs`. If it does, check `using DnD.Core` matches the namespace in `DnDEnums.cs` (it does — confirmed at `Assets/Scripts/Core/DnDEnums.cs`).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Data/SaveData.cs
git commit -m "feat: add SaveData, ChatMessageData, CharacterCreationData data types"
```

---

## Task 2: SaveSystem

**Files:**
- Create: `Assets/Scripts/Services/SaveSystem.cs`

- [ ] **Step 1: Create SaveSystem.cs**

```csharp
// Assets/Scripts/Services/SaveSystem.cs
using UnityEngine;
using System.IO;
using DnD.Data;

namespace DNDLLM.Services
{
    public static class SaveSystem
    {
        private const int SlotCount = 3;
        private static string SaveDir => Path.Combine(Application.persistentDataPath, "Saves");

        /// <summary>Loads one slot's SaveData + portrait. Returns (null, null) if slot is empty.</summary>
        public static (SaveData data, Texture2D portrait) Load(int slotIndex)
        {
            string jsonPath = SlotJsonPath(slotIndex);
            if (!File.Exists(jsonPath)) return (null, null);

            SaveData data = null;
            try
            {
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(jsonPath));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveSystem] Failed to parse slot {slotIndex}: {e.Message}");
                return (null, null);
            }

            Texture2D portrait = null;
            string pngPath = SlotPortraitPath(slotIndex);
            if (File.Exists(pngPath))
            {
                portrait = new Texture2D(2, 2);
                if (!portrait.LoadImage(File.ReadAllBytes(pngPath)))
                    portrait = null;
            }

            return (data, portrait);
        }

        /// <summary>Writes SaveData + optional portrait PNG to the slot's files.</summary>
        public static void Save(int slotIndex, SaveData data, Texture2D portrait)
        {
            Directory.CreateDirectory(SaveDir);
            data.slotIndex  = slotIndex;
            data.lastPlayed = System.DateTime.UtcNow.ToString("o");
            File.WriteAllText(SlotJsonPath(slotIndex), JsonUtility.ToJson(data, prettyPrint: true));
            if (portrait != null)
                File.WriteAllBytes(SlotPortraitPath(slotIndex), portrait.EncodeToPNG());
        }

        /// <summary>Deletes all files for the given slot.</summary>
        public static void Delete(int slotIndex)
        {
            string j = SlotJsonPath(slotIndex);
            string p = SlotPortraitPath(slotIndex);
            if (File.Exists(j)) File.Delete(j);
            if (File.Exists(p)) File.Delete(p);
        }

        private static string SlotJsonPath(int i)     => Path.Combine(SaveDir, $"slot_{i}.json");
        private static string SlotPortraitPath(int i)  => Path.Combine(SaveDir, $"slot_{i}_portrait.png");
    }
}
```

- [ ] **Step 2: Verify it compiles in Unity — no errors.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Services/SaveSystem.cs
git commit -m "feat: add SaveSystem with Load/Save/Delete per slot"
```

---

## Task 3: ChatUI — GetMessageHistory

**Files:**
- Modify: `Assets/Scripts/UI/ChatUI.cs`

`ChatUI.activeMessages` holds the message GameObjects. Each is named `"Msg_Player"`, `"Msg_DM"`, or `"Msg_System"`. The text lives in a `TMP_Text` child named `"Text"`.

- [ ] **Step 1: Add the using statement and method**

At the top of `ChatUI.cs`, add:
```csharp
using DnD.Data;
```

Then add this method to the `ChatUI` class body (after `ClearChat()`):

```csharp
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
        result.Add(new ChatMessageData { type = type, text = tmp.text });
    }
    return result;
}
```

- [ ] **Step 2: Verify it compiles in Unity — no errors.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/ChatUI.cs
git commit -m "feat: add ChatUI.GetMessageHistory for save system"
```

---

## Task 4: CharacterCreationPopup Script

**Files:**
- Create: `Assets/Scripts/UI/CharacterCreationPopup.cs`

The script manages 6 step panels (only one visible at a time), builds race/class button grids at runtime, runs portrait generation asynchronously, and fires `OnComplete` with a `CharacterCreationData` struct.

- [ ] **Step 1: Create CharacterCreationPopup.cs**

```csharp
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
```

- [ ] **Step 2: Verify it compiles in Unity — no errors.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/CharacterCreationPopup.cs
git commit -m "feat: add CharacterCreationPopup wizard with portrait generation"
```

---

## Task 5: TitleScreen Script

**Files:**
- Create: `Assets/Scripts/UI/TitleScreen.cs`

- [ ] **Step 1: Create TitleScreen.cs**

```csharp
// Assets/Scripts/UI/TitleScreen.cs
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DnD.Data;
using DNDLLM.Services;

namespace DnD.UI
{
    public class TitleScreen : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button newGameButton;

        [Header("Slot Rows (3 entries, index 0-2)")]
        [SerializeField] private Button[]   slotButtons;       // entire row is clickable
        [SerializeField] private RawImage[] slotPortraits;     // 36x36 portrait thumbnail
        [SerializeField] private TMP_Text[] slotNameTexts;     // "Aric the Bold"
        [SerializeField] private TMP_Text[] slotSubTexts;      // "Fighter · Level 3 · Human"
        [SerializeField] private TMP_Text[] slotCampaignTexts; // "The Sunken Crypts…"
        [SerializeField] private TMP_Text[] slotDateTexts;     // "2 days ago"

        // ── Events ────────────────────────────────────────────────────────
        public Action<int> OnSlotSelected;  // loaded slot index
        public Action      OnNewGame;

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            newGameButton.onClick.RemoveAllListeners();
            newGameButton.onClick.AddListener(() => OnNewGame?.Invoke());

            for (int i = 0; i < 3; i++)
            {
                var (data, portrait) = SaveSystem.Load(i);
                bool populated = data != null;

                slotButtons[i].interactable = populated;

                if (populated)
                {
                    slotNameTexts[i].text     = data.characterName;
                    slotSubTexts[i].text      = $"{data.className} · Level {data.level} · {data.raceName}";
                    string seed = data.campaignSeed ?? "";
                    slotCampaignTexts[i].text = seed.Length > 30 ? seed.Substring(0, 30) + "…" : seed;
                    slotDateTexts[i].text     = FormatDate(data.lastPlayed);
                    if (portrait != null && slotPortraits[i] != null)
                        slotPortraits[i].texture = portrait;
                }
                else
                {
                    slotNameTexts[i].text     = "Empty slot";
                    slotSubTexts[i].text      = "";
                    slotCampaignTexts[i].text = "";
                    slotDateTexts[i].text     = "";
                }

                int captured = i;
                slotButtons[i].onClick.RemoveAllListeners();
                slotButtons[i].onClick.AddListener(() => OnSlotSelected?.Invoke(captured));
            }
        }

        private static string FormatDate(string isoDate)
        {
            if (string.IsNullOrEmpty(isoDate)) return "";
            if (!DateTime.TryParse(isoDate, null, DateTimeStyles.RoundtripKind, out var dt))
                return "";
            var diff = DateTime.UtcNow - dt;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalHours   < 1) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays    < 1) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays    < 2) return "Yesterday";
            return $"{(int)diff.TotalDays} days ago";
        }
    }
}
```

- [ ] **Step 2: Verify it compiles in Unity — no errors.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/TitleScreen.cs
git commit -m "feat: add TitleScreen with 3-slot load/new game UI"
```

---

## Task 6: UISceneBuilder — MENU button + BuildTitleScreen

**Files:**
- Modify: `Assets/Editor/UISceneBuilder.cs`

Two changes in one task: (1) add a MENU button to the existing `RebuildCanvas()` method, and (2) add a new `BuildTitleScreen()` menu item.

### Part A — MENU button in RebuildCanvas()

- [ ] **Step 1: Add MENU button at the end of `RebuildCanvas()`, just before `EditorSceneManager.MarkSceneDirty(scene);`**

The split layout already fills the Canvas. The MENU button is anchored top-right *on the Canvas root* (not inside the split), so it floats above the layout.

```csharp
// ── Menu button (top-right, floats above split) ───────────────────
var menuBtnGO = MakeGO("MenuButton", canvasGO.transform);
var menuBtnRT = menuBtnGO.GetComponent<RectTransform>();
menuBtnRT.anchorMin  = new Vector2(1f, 1f);
menuBtnRT.anchorMax  = new Vector2(1f, 1f);
menuBtnRT.pivot      = new Vector2(1f, 1f);
menuBtnRT.anchoredPosition = new Vector2(-8f, -8f);
menuBtnRT.sizeDelta  = new Vector2(64f, 28f);
var menuBtnImg = menuBtnGO.AddComponent<Image>();
menuBtnImg.color = new Color32(0x12, 0x0C, 0x03, 0xCC);
var menuBtn = menuBtnGO.AddComponent<Button>();
menuBtn.targetGraphic = menuBtnImg;
var menuBtnTextGO = MakeGO("Text", menuBtnGO.transform);
var menuBtnTextRT  = menuBtnTextGO.GetComponent<RectTransform>();
menuBtnTextRT.anchorMin = Vector2.zero; menuBtnTextRT.anchorMax = Vector2.one;
menuBtnTextRT.offsetMin = Vector2.zero; menuBtnTextRT.offsetMax = Vector2.zero;
var menuBtnTMP = menuBtnTextGO.AddComponent<TextMeshProUGUI>();
menuBtnTMP.text      = "MENU";
menuBtnTMP.fontSize  = 11f;
menuBtnTMP.color     = UITheme.GoldAccent;
menuBtnTMP.alignment = TextAlignmentOptions.Center;

// Wire menuButton field on ChatUI's canvas companion (GameManager will find it)
so.FindProperty("menuButton").objectReferenceValue = menuBtn;
so.ApplyModifiedProperties();
```

**Note:** `ChatUI` does not currently have a `menuButton` field. Before adding this wiring, first add `[SerializeField] private Button menuButton;` to `ChatUI.cs` (or wire directly to GameManager). For simplicity, wire it to `GameManager` via a separate `SerializedObject` at the end of `RebuildCanvas()`:

```csharp
// Wire menuButton to GameManager if present
var gameSystemGO = GameObject.Find("GameSystem");
if (gameSystemGO != null)
{
    var gm = gameSystemGO.GetComponent<DnD.Managers.GameManager>();
    if (gm != null)
    {
        var gmSO = new SerializedObject(gm);
        gmSO.FindProperty("menuButton").objectReferenceValue = menuBtn;
        gmSO.ApplyModifiedProperties();
    }
}
```

### Part B — BuildTitleScreen menu item

- [ ] **Step 2: Add `BuildTitleScreen()` after `SetupGameManager()`**

```csharp
[MenuItem("DnD/Build Title Screen")]
public static void BuildTitleScreen()
{
    var scene = EditorSceneManager.GetActiveScene();

    // Remove old TitleScreen canvas if present
    foreach (var c in Object.FindObjectsByType<DnD.UI.TitleScreen>(FindObjectsSortMode.None))
        Undo.DestroyObjectImmediate(c.gameObject);

    // ── Canvas (sortingOrder=20, renders above everything) ─────────
    var canvasGO = new GameObject("TitleScreenCanvas");
    Undo.RegisterCreatedObjectUndo(canvasGO, "Build Title Screen");

    var canvas = canvasGO.AddComponent<Canvas>();
    canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 20;
    var scaler = canvasGO.AddComponent<CanvasScaler>();
    scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920, 1080);
    scaler.matchWidthOrHeight  = 0.5f;
    canvasGO.AddComponent<GraphicRaycaster>();

    var titleScreen = canvasGO.AddComponent<DnD.UI.TitleScreen>();

    // ── Full-screen dark background ─────────────────────────────────
    var bgGO = MakeGO("Background", canvasGO.transform);
    var bgRT = bgGO.GetComponent<RectTransform>();
    bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
    bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
    bgGO.AddComponent<Image>().color = new Color32(0x0D, 0x08, 0x05, 0xFF);

    // ── Center panel (fixed width, centered) ────────────────────────
    var panelGO = MakeGO("CenterPanel", bgGO.transform);
    var panelRT = panelGO.GetComponent<RectTransform>();
    panelRT.anchorMin       = new Vector2(0.5f, 0.5f);
    panelRT.anchorMax       = new Vector2(0.5f, 0.5f);
    panelRT.pivot           = new Vector2(0.5f, 0.5f);
    panelRT.anchoredPosition = Vector2.zero;
    panelRT.sizeDelta        = new Vector2(440f, 0f);
    var panelVLG = panelGO.AddComponent<VerticalLayoutGroup>();
    panelVLG.spacing              = 8f;
    panelVLG.padding              = new RectOffset(0, 0, 0, 0);
    panelVLG.childForceExpandWidth = true;
    panelVLG.childForceExpandHeight = false;
    panelVLG.childControlWidth   = true;
    panelVLG.childControlHeight  = true;
    panelGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

    // Logo
    AddTitleLabel(panelGO.transform, "D&D LLM", 28f, UITheme.GoldAccent, 10f);
    AddTitleLabel(panelGO.transform, "AN AI ADVENTURE", 11f, UITheme.SystemText, 2f);

    // Divider
    var divGO = MakeGO("Divider", panelGO.transform);
    divGO.AddComponent<LayoutElement>().preferredHeight = 1f;
    divGO.AddComponent<Image>().color = UITheme.GoldAccent;

    // Spacer
    var spacerLE = MakeGO("Spacer", panelGO.transform).AddComponent<LayoutElement>();
    spacerLE.preferredHeight = 8f;

    // New Game button
    var newGameGO  = MakeGO("NewGameButton", panelGO.transform);
    newGameGO.AddComponent<LayoutElement>().preferredHeight = 48f;
    var ngImg = newGameGO.AddComponent<Image>();
    ngImg.color = UITheme.GoldAccent;
    var ngBtn = newGameGO.AddComponent<Button>();
    ngBtn.targetGraphic = ngImg;
    var ngColors = ngBtn.colors;
    ngColors.highlightedColor = new Color32(0xE0, 0xC0, 0x70, 0xFF);
    ngColors.pressedColor     = new Color32(0xA0, 0x80, 0x30, 0xFF);
    ngBtn.colors = ngColors;
    var ngTextGO = MakeGO("Text", newGameGO.transform);
    var ngTextRT  = ngTextGO.GetComponent<RectTransform>();
    ngTextRT.anchorMin = Vector2.zero; ngTextRT.anchorMax = Vector2.one;
    ngTextRT.offsetMin = Vector2.zero; ngTextRT.offsetMax = Vector2.zero;
    var ngTMP = ngTextGO.AddComponent<TextMeshProUGUI>();
    ngTMP.text = "+ NEW GAME";
    ngTMP.fontSize = 16f;
    ngTMP.color = UITheme.BackgroundDeep;
    ngTMP.alignment = TextAlignmentOptions.Center;
    ngTMP.characterSpacing = 2f;

    // Continue label
    AddTitleLabel(panelGO.transform, "CONTINUE ADVENTURE", 10f, UITheme.SystemText, 1.5f);

    // Slot rows x3
    var slotButtons       = new Button[3];
    var slotPortraits     = new RawImage[3];
    var slotNameTexts     = new TMP_Text[3];
    var slotSubTexts      = new TMP_Text[3];
    var slotCampaignTexts = new TMP_Text[3];
    var slotDateTexts     = new TMP_Text[3];

    for (int i = 0; i < 3; i++)
    {
        var rowGO = MakeGO($"SlotRow_{i}", panelGO.transform);
        rowGO.AddComponent<LayoutElement>().preferredHeight = 60f;
        var rowImg = rowGO.AddComponent<Image>();
        rowImg.color = UITheme.BackgroundMid;
        var rowBtn = rowGO.AddComponent<Button>();
        rowBtn.targetGraphic = rowImg;
        var rowColors = rowBtn.colors;
        rowColors.highlightedColor = new Color32(0x2A, 0x1F, 0x0E, 0xFF);
        rowBtn.colors = rowColors;
        var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
        rowHLG.spacing = 8f;
        rowHLG.padding = new RectOffset(10, 10, 8, 8);
        rowHLG.childForceExpandHeight = true;
        rowHLG.childForceExpandWidth  = false;
        rowHLG.childControlHeight = true;
        rowHLG.childControlWidth  = true;

        // Portrait thumbnail
        var portraitGO = MakeGO("Portrait", rowGO.transform);
        portraitGO.AddComponent<LayoutElement>().preferredWidth = 40f;
        portraitGO.AddComponent<Image>().color = UITheme.BackgroundDeep;
        var rawImg = portraitGO.AddComponent<RawImage>();
        rawImg.color = Color.white;
        slotPortraits[i] = rawImg;

        // Info column
        var infoGO = MakeGO("Info", rowGO.transform);
        infoGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var infoVLG = infoGO.AddComponent<VerticalLayoutGroup>();
        infoVLG.childForceExpandWidth  = true;
        infoVLG.childForceExpandHeight = false;
        infoVLG.childControlWidth  = true;
        infoVLG.childControlHeight = true;
        infoVLG.spacing = 2f;

        slotNameTexts[i]     = AddInfoText(infoGO.transform, "Name",     UITheme.DmText,    14f);
        slotSubTexts[i]      = AddInfoText(infoGO.transform, "Sub",      UITheme.SystemText, 11f);
        slotCampaignTexts[i] = AddInfoText(infoGO.transform, "Campaign", UITheme.SystemText, 10f);
        slotDateTexts[i]     = AddInfoText(infoGO.transform, "Date",     UITheme.PlaceholderText, 10f);

        // Chevron
        var chevronGO = MakeGO("Chevron", rowGO.transform);
        chevronGO.AddComponent<LayoutElement>().preferredWidth = 20f;
        var chevTMP = chevronGO.AddComponent<TextMeshProUGUI>();
        chevTMP.text      = "›";
        chevTMP.fontSize  = 20f;
        chevTMP.color     = UITheme.GoldAccent;
        chevTMP.alignment = TextAlignmentOptions.MidlineRight;

        slotButtons[i] = rowBtn;
    }

    // ── Wire TitleScreen fields ──────────────────────────────────────
    var so = new SerializedObject(titleScreen);
    so.FindProperty("newGameButton").objectReferenceValue = ngBtn;
    SetArrayProp(so, "slotButtons",       slotButtons);
    SetArrayProp(so, "slotPortraits",     slotPortraits);
    SetArrayProp(so, "slotNameTexts",     slotNameTexts);
    SetArrayProp(so, "slotSubTexts",      slotSubTexts);
    SetArrayProp(so, "slotCampaignTexts", slotCampaignTexts);
    SetArrayProp(so, "slotDateTexts",     slotDateTexts);
    so.ApplyModifiedProperties();

    EditorSceneManager.MarkSceneDirty(scene);
    Debug.Log("[UISceneBuilder] Title screen built. Press Ctrl+S to save.");
}
```

Add these two private helpers to the class (after `AddHeader`):

```csharp
private static TMP_Text AddTitleLabel(Transform parent, string text, float size, Color color, float spacing)
{
    var go = MakeGO("Label_" + text.Replace(" ", ""), parent);
    go.AddComponent<LayoutElement>().preferredHeight = size + 10f;
    var tmp = go.AddComponent<TextMeshProUGUI>();
    tmp.text             = text;
    tmp.fontSize         = size;
    tmp.color            = color;
    tmp.alignment        = TextAlignmentOptions.Center;
    tmp.characterSpacing = spacing;
    return tmp;
}

private static TMP_Text AddInfoText(Transform parent, string name, Color color, float size)
{
    var go = MakeGO(name, parent);
    var le = go.AddComponent<LayoutElement>();
    le.preferredHeight = size + 4f;
    var tmp = go.AddComponent<TextMeshProUGUI>();
    tmp.color    = color;
    tmp.fontSize = size;
    tmp.enableWordWrapping = false;
    return tmp;
}

private static void SetArrayProp<T>(SerializedObject so, string propName, T[] values)
    where T : UnityEngine.Object
{
    var prop = so.FindProperty(propName);
    prop.arraySize = values.Length;
    for (int i = 0; i < values.Length; i++)
        prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
}
```

- [ ] **Step 3: Verify UISceneBuilder.cs compiles (no errors in Unity console).**

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/UISceneBuilder.cs
git commit -m "feat: UISceneBuilder — MENU button and Build Title Screen menu item"
```

---

## Task 7: UISceneBuilder — BuildCharacterPopup

**Files:**
- Modify: `Assets/Editor/UISceneBuilder.cs`

- [ ] **Step 1: Add `BuildCharacterPopup()` after `BuildTitleScreen()`**

```csharp
[MenuItem("DnD/Build Character Popup")]
public static void BuildCharacterPopup()
{
    var scene = EditorSceneManager.GetActiveScene();

    // Remove old popup canvas
    foreach (var p in Object.FindObjectsByType<DnD.UI.CharacterCreationPopup>(FindObjectsSortMode.None))
        Undo.DestroyObjectImmediate(p.gameObject);

    // ── Canvas (sortingOrder=10) ────────────────────────────────────
    var canvasGO = new GameObject("CharacterPopupCanvas");
    Undo.RegisterCreatedObjectUndo(canvasGO, "Build Char Popup");

    var canvas = canvasGO.AddComponent<Canvas>();
    canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 10;
    var scaler = canvasGO.AddComponent<CanvasScaler>();
    scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920, 1080);
    scaler.matchWidthOrHeight  = 0.5f;
    canvasGO.AddComponent<GraphicRaycaster>();

    var popup = canvasGO.AddComponent<DnD.UI.CharacterCreationPopup>();

    // ── Semi-transparent overlay ────────────────────────────────────
    var overlayGO = MakeGO("Overlay", canvasGO.transform);
    var overlayRT = overlayGO.GetComponent<RectTransform>();
    overlayRT.anchorMin = Vector2.zero; overlayRT.anchorMax = Vector2.one;
    overlayRT.offsetMin = Vector2.zero; overlayRT.offsetMax = Vector2.zero;
    overlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

    // ── Popup panel (centered, fixed size) ─────────────────────────
    var panelGO = MakeGO("PopupPanel", overlayGO.transform);
    var panelRT = panelGO.GetComponent<RectTransform>();
    panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
    panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
    panelRT.pivot            = new Vector2(0.5f, 0.5f);
    panelRT.anchoredPosition = Vector2.zero;
    panelRT.sizeDelta        = new Vector2(420f, 520f);
    panelGO.AddComponent<Image>().color = UITheme.BackgroundDeep;
    var panelVLG = panelGO.AddComponent<VerticalLayoutGroup>();
    panelVLG.padding              = new RectOffset(0, 0, 0, 0);
    panelVLG.spacing              = 0f;
    panelVLG.childForceExpandWidth  = true;
    panelVLG.childForceExpandHeight = false;
    panelVLG.childControlWidth   = true;
    panelVLG.childControlHeight  = true;

    // Header
    var headerGO = MakeGO("Header", panelGO.transform);
    headerGO.AddComponent<LayoutElement>().preferredHeight = 44f;
    headerGO.AddComponent<Image>().color = new Color32(0x12, 0x0C, 0x03, 0xFF);
    var headerTMP = MakeGO("Text", headerGO.transform);
    var hRT = headerTMP.GetComponent<RectTransform>();
    hRT.anchorMin = Vector2.zero; hRT.anchorMax = Vector2.one;
    hRT.offsetMin = Vector2.zero; hRT.offsetMax = Vector2.zero;
    var hTMP = headerTMP.AddComponent<TextMeshProUGUI>();
    hTMP.text = "CREATE YOUR HERO";
    hTMP.fontSize = UITheme.FontHeader;
    hTMP.color = UITheme.GoldAccent;
    hTMP.alignment = TextAlignmentOptions.Center;
    hTMP.characterSpacing = 1.5f;

    // Step indicator row (5 bars)
    var barRowGO = MakeGO("StepIndicator", panelGO.transform);
    barRowGO.AddComponent<LayoutElement>().preferredHeight = 10f;
    barRowGO.AddComponent<Image>().color = UITheme.BackgroundMid;
    var barHLG = barRowGO.AddComponent<HorizontalLayoutGroup>();
    barHLG.padding  = new RectOffset(16, 16, 3, 3);
    barHLG.spacing  = 4f;
    barHLG.childForceExpandHeight = true;
    barHLG.childForceExpandWidth  = true;
    barHLG.childControlHeight = true;
    barHLG.childControlWidth  = true;
    var stepBars = new Image[5];
    for (int i = 0; i < 5; i++)
    {
        var barGO = MakeGO($"Bar{i}", barRowGO.transform);
        stepBars[i] = barGO.AddComponent<Image>();
        stepBars[i].color = new Color32(0x4A, 0x38, 0x20, 0xFF);
    }

    // Step label
    var stepLabelGO = MakeGO("StepLabel", panelGO.transform);
    stepLabelGO.AddComponent<LayoutElement>().preferredHeight = 26f;
    stepLabelGO.AddComponent<Image>().color = UITheme.BackgroundMid;
    var slRT = stepLabelGO.GetComponent<RectTransform>();
    var stepLabelTMP = stepLabelGO.AddComponent<TextMeshProUGUI>();
    stepLabelTMP.text      = "Step 1 of 5 — NAME";
    stepLabelTMP.fontSize  = 11f;
    stepLabelTMP.color     = UITheme.SystemText;
    stepLabelTMP.alignment = TextAlignmentOptions.Center;

    // Content container (fills remaining space)
    var contentGO = MakeGO("ContentContainer", panelGO.transform);
    contentGO.AddComponent<LayoutElement>().flexibleHeight = 1f;
    contentGO.AddComponent<Image>().color = Color.clear;

    // Helper: full-fill child RT inside contentGO
    System.Func<string, GameObject> makeStepPanel = (name) =>
    {
        var go = MakeGO(name, contentGO.transform);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        go.AddComponent<Image>().color = Color.clear;
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 16, 8);
        vlg.spacing = 10f;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        return go;
    };

    // ── Step panels ─────────────────────────────────────────────────
    var stepPanels = new GameObject[6];

    // Panel 0: Name
    stepPanels[0] = makeStepPanel("NamePanel");
    AddPromptLabel(stepPanels[0].transform, "What is your hero called?");
    var nameInput = MakeInputField(stepPanels[0].transform, "Adventurer", false);

    // Panel 1: Race
    stepPanels[1] = makeStepPanel("RacePanel");
    AddPromptLabel(stepPanels[1].transform, "Choose your race:");
    var raceGrid = MakeGO("RaceGrid", stepPanels[1].transform);
    raceGrid.AddComponent<Image>().color = Color.clear;
    raceGrid.AddComponent<LayoutElement>().flexibleHeight = 1f;
    var raceGLG = raceGrid.AddComponent<GridLayoutGroup>();
    raceGLG.cellSize    = new Vector2(115f, 36f);
    raceGLG.spacing     = new Vector2(4f, 4f);
    raceGLG.constraint  = GridLayoutGroup.Constraint.FixedColumnCount;
    raceGLG.constraintCount = 3;

    // Panel 2: Class
    stepPanels[2] = makeStepPanel("ClassPanel");
    AddPromptLabel(stepPanels[2].transform, "Choose your class:");
    var classGrid = MakeGO("ClassGrid", stepPanels[2].transform);
    classGrid.AddComponent<Image>().color = Color.clear;
    classGrid.AddComponent<LayoutElement>().flexibleHeight = 1f;
    var classGLG = classGrid.AddComponent<GridLayoutGroup>();
    classGLG.cellSize    = new Vector2(85f, 36f);
    classGLG.spacing     = new Vector2(4f, 4f);
    classGLG.constraint  = GridLayoutGroup.Constraint.FixedColumnCount;
    classGLG.constraintCount = 4;

    // Panel 3: Appearance
    stepPanels[3] = makeStepPanel("AppearancePanel");
    AddPromptLabel(stepPanels[3].transform, "Describe your hero's appearance:");
    var appearanceInput = MakeInputField(stepPanels[3].transform, "Tall, scarred warrior with dark hair...", true);
    stepPanels[3].GetComponent<VerticalLayoutGroup>().childForceExpandHeight = true;

    // Panel 4: Backstory
    stepPanels[4] = makeStepPanel("BackstoryPanel");
    AddPromptLabel(stepPanels[4].transform, "What brought you to this adventure?");
    var backstoryInput = MakeInputField(stepPanels[4].transform, "My village was destroyed...", true);
    stepPanels[4].GetComponent<VerticalLayoutGroup>().childForceExpandHeight = true;

    // Panel 5: Confirm
    stepPanels[5] = makeStepPanel("ConfirmPanel");
    var confirmHLG = MakeGO("PortraitRow", stepPanels[5].transform);
    confirmHLG.AddComponent<LayoutElement>().flexibleHeight = 1f;
    confirmHLG.AddComponent<Image>().color = Color.clear;
    var cHLG = confirmHLG.AddComponent<HorizontalLayoutGroup>();
    cHLG.spacing = 12f;
    cHLG.childForceExpandHeight = true;
    cHLG.childForceExpandWidth  = false;
    cHLG.childControlHeight = true;
    cHLG.childControlWidth  = true;

    // Portrait image
    var portraitGO = MakeGO("PortraitImage", confirmHLG.transform);
    portraitGO.AddComponent<LayoutElement>().preferredWidth = 100f;
    portraitGO.AddComponent<Image>().color = UITheme.BackgroundMid;
    var portraitRaw = portraitGO.AddComponent<RawImage>();
    portraitRaw.color = Color.white;

    // Stats text
    var statsGO = MakeGO("StatsText", confirmHLG.transform);
    statsGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
    var statsTMP = statsGO.AddComponent<TextMeshProUGUI>();
    statsTMP.color    = UITheme.DmText;
    statsTMP.fontSize = 14f;
    statsTMP.alignment = TextAlignmentOptions.TopLeft;

    // Begin button (inside confirmPanel, below portrait row)
    var beginGO = MakeGO("BeginButton", stepPanels[5].transform);
    beginGO.AddComponent<LayoutElement>().preferredHeight = 44f;
    var beginImg = beginGO.AddComponent<Image>();
    beginImg.color = UITheme.GoldAccent;
    var beginBtn = beginGO.AddComponent<Button>();
    beginBtn.targetGraphic = beginImg;
    var beginBtnColors = beginBtn.colors;
    beginBtnColors.disabledColor = new Color32(0x4A, 0x38, 0x20, 0xFF);
    beginBtn.colors = beginBtnColors;
    var beginTextGO = MakeGO("Text", beginGO.transform);
    var btRT = beginTextGO.GetComponent<RectTransform>();
    btRT.anchorMin = Vector2.zero; btRT.anchorMax = Vector2.one;
    btRT.offsetMin = Vector2.zero; btRT.offsetMax = Vector2.zero;
    var beginTMP = beginTextGO.AddComponent<TextMeshProUGUI>();
    beginTMP.text      = "Begin Adventure";
    beginTMP.fontSize  = 16f;
    beginTMP.color     = UITheme.BackgroundDeep;
    beginTMP.alignment = TextAlignmentOptions.Center;

    // ── Nav row ─────────────────────────────────────────────────────
    var navRowGO = MakeGO("NavRow", panelGO.transform);
    navRowGO.AddComponent<LayoutElement>().preferredHeight = 44f;
    navRowGO.AddComponent<Image>().color = UITheme.BackgroundMid;
    var navHLG = navRowGO.AddComponent<HorizontalLayoutGroup>();
    navHLG.padding  = new RectOffset(12, 12, 6, 6);
    navHLG.spacing  = 8f;
    navHLG.childForceExpandHeight = true;
    navHLG.childForceExpandWidth  = false;
    navHLG.childControlHeight = true;
    navHLG.childControlWidth  = true;

    var cancelBtn = MakeNavButton("CancelButton", "Cancel", navRowGO.transform, UITheme.BackgroundDeep, UITheme.SystemText, 80f);
    var backBtn   = MakeNavButton("BackButton",   "< Back", navRowGO.transform, UITheme.BackgroundDeep, UITheme.DmText, 80f);
    // Spacer
    var navSpacer = MakeGO("Spacer", navRowGO.transform);
    navSpacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
    var nextBtn   = MakeNavButton("NextButton",   "Next >", navRowGO.transform, UITheme.GoldAccent, UITheme.BackgroundDeep, 80f);

    // ── Wire CharacterCreationPopup fields ───────────────────────────
    var so = new SerializedObject(popup);
    SetArrayProp(so, "stepPanels", stepPanels);
    so.FindProperty("nameInput").objectReferenceValue         = nameInput;
    so.FindProperty("raceGridContainer").objectReferenceValue = raceGrid.GetComponent<RectTransform>();
    so.FindProperty("classGridContainer").objectReferenceValue= classGrid.GetComponent<RectTransform>();
    so.FindProperty("appearanceInput").objectReferenceValue   = appearanceInput;
    so.FindProperty("backstoryInput").objectReferenceValue    = backstoryInput;
    so.FindProperty("portraitImage").objectReferenceValue     = portraitRaw;
    so.FindProperty("statsText").objectReferenceValue         = statsTMP;
    so.FindProperty("beginButton").objectReferenceValue       = beginBtn;
    so.FindProperty("nextButton").objectReferenceValue        = nextBtn;
    so.FindProperty("backButton").objectReferenceValue        = backBtn;
    so.FindProperty("cancelButton").objectReferenceValue      = cancelBtn;
    SetArrayProp(so, "stepBars", stepBars);
    so.FindProperty("stepLabel").objectReferenceValue         = stepLabelTMP;
    so.ApplyModifiedProperties();

    // Deactivate all step panels except 0
    for (int i = 1; i < stepPanels.Length; i++)
        stepPanels[i].SetActive(false);

    EditorSceneManager.MarkSceneDirty(scene);
    Debug.Log("[UISceneBuilder] Character popup built. Press Ctrl+S to save.");
}
```

Add two more private helpers (after `AddInfoText`):

```csharp
private static void AddPromptLabel(Transform parent, string text)
{
    var go  = MakeGO("Prompt", parent);
    go.AddComponent<LayoutElement>().preferredHeight = 24f;
    var tmp = go.AddComponent<TextMeshProUGUI>();
    tmp.text      = text;
    tmp.fontSize  = 13f;
    tmp.color     = UITheme.SystemText;
    tmp.alignment = TextAlignmentOptions.TopLeft;
}

private static TMP_InputField MakeInputField(Transform parent, string placeholder, bool multiline)
{
    var go  = MakeGO("InputField", parent);
    var le  = go.AddComponent<LayoutElement>();
    if (multiline) le.flexibleHeight = 1f;
    else           le.preferredHeight = 40f;
    go.AddComponent<Image>().color = UITheme.BackgroundMid;
    var field = go.AddComponent<TMP_InputField>();
    if (multiline)
    {
        field.lineType = TMP_InputField.LineType.MultiLineNewline;
        field.textComponent = null; // set below
    }

    var textAreaGO = MakeGO("Text Area", go.transform);
    var taRT = textAreaGO.GetComponent<RectTransform>();
    taRT.anchorMin = Vector2.zero; taRT.anchorMax = Vector2.one;
    taRT.offsetMin = new Vector2(8, 4); taRT.offsetMax = new Vector2(-8, -4);
    textAreaGO.AddComponent<RectMask2D>();
    field.textViewport = taRT;

    var phGO = MakeGO("Placeholder", textAreaGO.transform);
    var phRT = phGO.GetComponent<RectTransform>();
    phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
    phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;
    var phTMP = phGO.AddComponent<TextMeshProUGUI>();
    phTMP.text      = placeholder;
    phTMP.color     = UITheme.PlaceholderText;
    phTMP.fontSize  = UITheme.FontInput;
    phTMP.fontStyle = FontStyles.Italic;
    phTMP.enableWordWrapping = multiline;
    field.placeholder = phTMP;

    var txtGO = MakeGO("Text", textAreaGO.transform);
    var txtRT = txtGO.GetComponent<RectTransform>();
    txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
    txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
    var txtTMP = txtGO.AddComponent<TextMeshProUGUI>();
    txtTMP.color    = UITheme.InputText;
    txtTMP.fontSize = UITheme.FontInput;
    txtTMP.enableWordWrapping = multiline;
    field.textComponent = txtTMP;

    return field;
}

private static Button MakeNavButton(string name, string label, Transform parent, Color32 bgColor, Color32 textColor, float width)
{
    var go  = MakeGO(name, parent);
    go.AddComponent<LayoutElement>().preferredWidth = width;
    var img = go.AddComponent<Image>();
    img.color = bgColor;
    var btn = go.AddComponent<Button>();
    btn.targetGraphic = img;
    var textGO = MakeGO("Text", go.transform);
    var tRT = textGO.GetComponent<RectTransform>();
    tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
    tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
    var tmp = textGO.AddComponent<TextMeshProUGUI>();
    tmp.text      = label;
    tmp.fontSize  = 13f;
    tmp.color     = textColor;
    tmp.alignment = TextAlignmentOptions.Center;
    return btn;
}
```

- [ ] **Step 2: Verify UISceneBuilder.cs compiles — no errors in Unity console.**

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/UISceneBuilder.cs
git commit -m "feat: UISceneBuilder — Build Character Popup menu item"
```

---

## Task 8: GameManager Integration

**Files:**
- Modify: `Assets/Scripts/Managers/GameManager.cs`

### 8a — Add fields

- [ ] **Step 1: Add new serialized fields and a private current-slot tracker (after existing `[Header("AI Configuration")]` block)**

```csharp
[Header("UI — set by UISceneBuilder")]
[SerializeField] private DnD.UI.TitleScreen             titleScreen;
[SerializeField] private DnD.UI.CharacterCreationPopup  characterPopup;
[SerializeField] private UnityEngine.UI.Button          menuButton;

private int _currentSlotIndex = 0;  // slot being played / to save into
```

Also add these `using` directives at the top of the file (if not already present):

```csharp
using DnD.Data;
using DNDLLM.Services;
```

### 8b — Wire menu button in Start

- [ ] **Step 2: In `Start()`, after the `ChatUI.Instance.OnPlayerInput += HandlePlayerInput;` line, add:**

```csharp
if (menuButton != null)
    menuButton.onClick.AddListener(OnMenuButtonPressed);
```

### 8c — ShowMainMenu

- [ ] **Step 3: Replace `ShowMainMenu()` entirely:**

```csharp
private void ShowMainMenu()
{
    if (titleScreen != null)
    {
        titleScreen.gameObject.SetActive(true);
        titleScreen.OnNewGame     = OnNewGameSelected;
        titleScreen.OnSlotSelected = OnSlotSelected;
        titleScreen.Refresh();
    }
    else
    {
        // Fallback: no title screen in scene yet
        if (ChatUI.Instance == null) return;
        ChatUI.Instance.AddSystemMessage("=== WELCOME TO D&D LLM ===");
        ChatUI.Instance.AddSystemMessage("Describe the adventure you want to embark on...");
    }
}
```

### 8d — New Game and Slot Selected handlers

- [ ] **Step 4: Add these three methods after `ShowMainMenu()`:**

```csharp
private void OnNewGameSelected()
{
    // Pick first empty slot; fall back to slot 0 if all full
    int slot = 0;
    for (int i = 0; i < 3; i++)
    {
        var (data, _) = DNDLLM.Services.SaveSystem.Load(i);
        if (data == null) { slot = i; break; }
        if (i == 2) slot = 0; // all full — overwrite slot 0
    }
    _currentSlotIndex = slot;

    if (titleScreen != null) titleScreen.gameObject.SetActive(false);
    if (ChatUI.Instance != null)
    {
        ChatUI.Instance.ClearChat();
        ChatUI.Instance.AddSystemMessage("=== NEW ADVENTURE ===");
        ChatUI.Instance.AddSystemMessage("Describe the adventure you want to embark on...");
    }
    // GameState stays MainMenu; player types prompt → StartCampaignAsync transitions to CharacterCreation
}

private void OnSlotSelected(int slotIndex)
{
    if (titleScreen != null) titleScreen.gameObject.SetActive(false);
    LoadSlot(slotIndex);
}

private void OnMenuButtonPressed()
{
    // Save current progress then show title screen
    SaveCurrentSlot();
    ChangeState(GameState.MainMenu);
}
```

### 8e — StartCharacterCreation

- [ ] **Step 5: Replace `StartCharacterCreation()` entirely:**

```csharp
private void StartCharacterCreation()
{
    if (characterPopup != null)
    {
        characterPopup.OnComplete  = OnCharacterCreationComplete;
        characterPopup.OnCancelled = () => ChangeState(GameState.MainMenu);
        characterPopup.Open(_currentSlotIndex);
    }
    else
    {
        // Fallback: text-based creation
        if (ChatUI.Instance == null) return;
        ChatUI.Instance.AddSystemMessage("=== CHARACTER CREATION ===");
        ChatUI.Instance.AddSystemMessage("Describe your hero -- class, background, personality.");
    }
}
```

### 8f — OnCharacterCreationComplete

- [ ] **Step 6: Add this method after `StartCharacterCreation()`:**

```csharp
private void OnCharacterCreationComplete(CharacterCreationData data)
{
    // Apply to playerCharacter
    if (playerCharacter == null)
    {
        var go = new GameObject("Player");
        playerCharacter = go.AddComponent<CharacterStats>();
        DontDestroyOnLoad(go);
    }
    playerCharacter.characterName = data.characterName;

    CharacterClass charClass = CreateBasicClass(data.characterClass,
        data.characterClass == CharacterClassName.Fighter  ? 10 :
        data.characterClass == CharacterClassName.Wizard   ?  6 :
        data.characterClass == CharacterClassName.Rogue    ?  8 : 8);

    playerCharacter.characterClass = charClass;
    playerCharacter.abilities = AbilityScores.GenerateRandom();
    playerCharacter.Initialize();

    // Show summary in chat
    if (ChatUI.Instance != null)
    {
        ChatUI.Instance.AddSystemMessage($"Character created: {data.characterName}");
        ChatUI.Instance.AddSystemMessage($"Class: {data.characterClass} | Race: {data.race}");
        ChatUI.Instance.AddSystemMessage($"HP: {playerCharacter.maxHitPoints} | AC: {playerCharacter.armorClass}");
        if (!string.IsNullOrEmpty(data.backstory))
            ChatUI.Instance.AddDMMessage(data.backstory);
    }

    // Save immediately
    SaveCurrentSlot(data.portrait);

    ChangeState(GameState.Exploration);
}
```

### 8g — SaveCurrentSlot and LoadSlot

- [ ] **Step 7: Add `SaveCurrentSlot()` and `LoadSlot()` after `OnCharacterCreationComplete()`:**

```csharp
private void SaveCurrentSlot(Texture2D portrait = null)
{
    if (playerCharacter == null) return;

    var saveData = new SaveData
    {
        characterName         = playerCharacter.characterName,
        raceName              = playerCharacter.race.ToString(),
        className             = playerCharacter.characterClass != null
                                    ? playerCharacter.characterClass.className.ToString()
                                    : "",
        level                 = playerCharacter.level,
        maxHP                 = playerCharacter.maxHitPoints,
        currentHP             = playerCharacter.currentHitPoints,
        armorClass            = playerCharacter.armorClass,
        str                   = playerCharacter.abilities.GetScore(AbilityScore.Strength),
        dex                   = playerCharacter.abilities.GetScore(AbilityScore.Dexterity),
        con                   = playerCharacter.abilities.GetScore(AbilityScore.Constitution),
        intel                 = playerCharacter.abilities.GetScore(AbilityScore.Intelligence),
        wis                   = playerCharacter.abilities.GetScore(AbilityScore.Wisdom),
        cha                   = playerCharacter.abilities.GetScore(AbilityScore.Charisma),
        campaignSeed          = currentCampaign != null ? "" : "",  // filled below
        campaignTimeline      = currentCampaign?.timelineText ?? "",
        gameState             = currentState.ToString(),
        messages              = ChatUI.Instance != null
                                    ? ChatUI.Instance.GetMessageHistory()
                                    : new System.Collections.Generic.List<ChatMessageData>()
    };

    // slotLabel shown on title screen
    saveData.slotLabel = $"{saveData.characterName} · {saveData.className} · Lv{saveData.level}";

    DNDLLM.Services.SaveSystem.Save(_currentSlotIndex, saveData, portrait);
    Debug.Log($"[GameManager] Saved slot {_currentSlotIndex}: {saveData.slotLabel}");
}

private void LoadSlot(int slotIndex)
{
    var (data, portrait) = DNDLLM.Services.SaveSystem.Load(slotIndex);
    if (data == null)
    {
        Debug.LogWarning($"[GameManager] Slot {slotIndex} is empty.");
        ChangeState(GameState.MainMenu);
        return;
    }

    _currentSlotIndex = slotIndex;

    // Restore character
    if (playerCharacter == null)
    {
        var go = new GameObject("Player");
        playerCharacter = go.AddComponent<CharacterStats>();
        DontDestroyOnLoad(go);
    }
    playerCharacter.characterName    = data.characterName;
    playerCharacter.level            = data.level;
    playerCharacter.maxHitPoints     = data.maxHP;
    playerCharacter.currentHitPoints = data.currentHP;
    playerCharacter.armorClass       = data.armorClass;
    playerCharacter.abilities = new AbilityScores(
        data.str, data.dex, data.con, data.intel, data.wis, data.cha);

    // Restore campaign
    if (!string.IsNullOrEmpty(data.campaignTimeline))
        currentCampaign = new DnD.AI.StoryTimeline { timelineText = data.campaignTimeline };

    // Restore chat history
    if (ChatUI.Instance != null)
    {
        ChatUI.Instance.ClearChat();
        if (data.messages != null)
        {
            foreach (var msg in data.messages)
            {
                switch (msg.type)
                {
                    case "Player": ChatUI.Instance.AddPlayerMessage(msg.text); break;
                    case "DM":     ChatUI.Instance.AddDMMessage(msg.text);     break;
                    default:       ChatUI.Instance.AddSystemMessage(msg.text); break;
                }
            }
        }
        ChatUI.Instance.AddSystemMessage("--- Adventure resumed ---");
    }

    // Transition to saved state
    if (System.Enum.TryParse<GameState>(data.gameState, out var savedState))
        ChangeState(savedState);
    else
        ChangeState(GameState.Exploration);
}
```

**Note:** `campaignSeed` saving requires capturing the original player prompt string. Add a `private string _campaignSeed;` field and set it inside `StartCampaignAsync(string campaignPrompt)`:

```csharp
private async Task StartCampaignAsync(string campaignPrompt)
{
    _campaignSeed = campaignPrompt;   // ← add this line at the top
    // ... rest of the existing method unchanged
}
```

Then in `SaveCurrentSlot`, replace `campaignSeed = currentCampaign != null ? "" : ""` with:

```csharp
campaignSeed = _campaignSeed ?? "",
```

- [ ] **Step 8: Verify GameManager.cs compiles — no errors in Unity console.**

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/Managers/GameManager.cs
git commit -m "feat: integrate title screen, character popup, and save/load into GameManager"
```

---

## Task 9: Scene Setup — Wire GameManager + Run Builders

**Files:**
- `Assets/masterscene.unity` (modified via Unity Editor operations)

- [ ] **Step 1: Open `masterscene` in Unity. From the top menu:**
  - Run `DnD → Rebuild UI Canvas` (adds MENU button, rewires ChatUI)
  - Run `DnD → Setup Game Manager`
  - Run `DnD → Build Title Screen`
  - Run `DnD → Build Character Popup`

- [ ] **Step 2: Wire `GameManager` fields in the Inspector**

Select the `GameSystem` GameObject in the Hierarchy. In the Inspector, find the `GameManager` component. Drag:
- `TitleScreenCanvas` → **Title Screen** field
- `CharacterPopupCanvas` → **Character Popup** field
- `Canvas/MenuButton` → **Menu Button** field

(If `UISceneBuilder.RebuildCanvas()` auto-wired `menuButton` to GameManager via `SerializedObject`, this step is already done — verify by checking the Inspector.)

- [ ] **Step 3: Press Ctrl+S to save the scene.**

- [ ] **Step 4: Enter Play mode. Verify:**
  - Title screen appears immediately (D&D LLM logo, "+ NEW GAME", 3 slots)
  - Clicking "NEW GAME" hides title screen and shows campaign prompt in chat
  - Typing a campaign prompt → DM responds → character popup appears (step-by-step wizard)
  - Wizard steps through Name → Race → Class → Appearance → Backstory → Confirm
  - Confirm screen shows portrait generating (or instant colour in mock mode)
  - "Begin Adventure" transitions to Exploration state with map visible
  - Console shows `[GameManager] Saved slot 0: YourName · Fighter · Lv1`
  - Pressing "MENU" button saves and returns to title screen
  - Slot 0 now shows your character name + class
  - Clicking the slot resumes the game and replays chat history

- [ ] **Step 5: Commit**

```bash
git add Assets/masterscene.unity
git commit -m "feat: wire title screen and character popup in masterscene"
```

---

## Self-Review Checklist

**Spec coverage:**
- [x] Step-by-step wizard with 6 steps (Name/Race/Class/Appearance/Backstory/Confirm) → Tasks 4, 7
- [x] Portrait generation from appearance field → Task 4 `GeneratePortraitAsync`
- [x] 3 named save slots → Task 2 `SaveSystem`, constant `SlotCount = 3`
- [x] SaveData includes campaignSeed, campaignTimeline, all character fields, gameState, messages → Task 1
- [x] Conversation history serialised → Task 3 `GetMessageHistory`, Task 8g
- [x] Full-screen title screen on launch → Tasks 5, 6
- [x] Slot rows show portrait thumbnail, name, class, level, campaign, date → Task 6 `BuildTitleScreen`
- [x] In-game MENU button saves + returns to title → Task 6 Part A, Task 8d
- [x] Load slot restores character stats + chat history + game state → Task 8g `LoadSlot`
- [x] New Game picks first empty slot, falls back to slot 0 → Task 8d `OnNewGameSelected`
