# UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the unstyled default Unity UI with a parchment-and-gold D&D theme: side-by-side layout (map 60% left, chat 40% right), delete legacy UIManager/GameManager, fix the map display via RenderTexture.

**Architecture:** A Unity Editor [MenuItem] script rebuilds the Canvas hierarchy programmatically. `ChatUI.cs` creates message GameObjects from code using `UITheme` color constants instead of prefabs. A `MapCameraController` creates a RenderTexture at runtime and wires it to a `RawImage` in the map panel — fixing the "three strips" display bug. The modern `DnD.Managers.GameManager` is extended to trigger map generation and uses better-formatted system messages.

**Tech Stack:** Unity uGUI, TextMeshPro, Unity Editor scripting ([MenuItem]), RenderTexture, `DnD.Managers.GameManager`, `DnD.UI.ChatUI`.

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `Assets/Scripts/UI/UIManager.cs` | **Delete** | Legacy — replaced by ChatUI |
| `Assets/Scripts/Core/GameManager.cs` | **Delete** | Legacy — replaced by DnD.Managers.GameManager |
| `Assets/Scripts/UI/UITheme.cs` | **Create** | All Color32 and font-size constants |
| `Assets/Scripts/UI/ChatUI.cs` | **Modify** | Remove prefab fields; create message GOs from code using UITheme |
| `Assets/Scripts/UI/MapCameraController.cs` | **Create** | Creates RenderTexture at runtime; wires to RawImage named "MapDisplay" |
| `Assets/Editor/UISceneBuilder.cs` | **Create** | [MenuItem] that tears down old Canvas and builds the new one |
| `Assets/Scripts/Map/MapGenerator.cs` | **Modify** | Remove `DNDLLM.Core.GameManager` reference; add ChatUI loading messages; use MapCamera |
| `Assets/Scripts/Managers/GameManager.cs` | **Modify** | Trigger `MapGenerator.GenerateMap()`; improve system message text |

---

## Task 1: Delete legacy files

**Files:**
- Delete: `Assets/Scripts/UI/UIManager.cs`
- Delete: `Assets/Scripts/Core/GameManager.cs`

- [ ] **Step 1: Delete UIManager.cs**

```bash
rm Assets/Scripts/UI/UIManager.cs
```

- [ ] **Step 2: Delete Core/GameManager.cs**

```bash
rm Assets/Scripts/Core/GameManager.cs
```

- [ ] **Step 3: Search for any remaining DNDLLM.Core references**

```bash
grep -rl "DNDLLM\.Core" Assets/Scripts --include="*.cs"
```

Expected output — only MapGenerator (fixed in Task 7):
```
Assets/Scripts/Map/MapGenerator.cs
```

If any other files appear (e.g. `ActionHandler.cs`, `StoryEngine.cs`), open each one and remove the `using DNDLLM.Core;` line and any calls to `DNDLLM.Core.GameManager`. Those scripts route through UIManager which is also deleted, so those calls are dead code.

- [ ] **Step 4: Verify compilation in Unity**

Open Unity. Only `MapGenerator.cs` should show an error (fixed in Task 7):
```
Assets/Scripts/Map/MapGenerator.cs: The type or namespace 'Core' does not exist
```

- [ ] **Step 5: Commit**

```bash
git add -u
git commit -m "chore: delete legacy UIManager and Core.GameManager"
```

---

## Task 2: Create UITheme.cs

**Files:**
- Create: `Assets/Scripts/UI/UITheme.cs`

- [ ] **Step 1: Create the file**

```csharp
// Assets/Scripts/UI/UITheme.cs
using UnityEngine;

namespace DnD.UI
{
    public static class UITheme
    {
        // Backgrounds
        public static readonly Color32 BackgroundDeep   = new Color32(0x1A, 0x10, 0x05, 0xFF);
        public static readonly Color32 BackgroundMid    = new Color32(0x1E, 0x15, 0x08, 0xFF);
        public static readonly Color32 BackgroundDM     = new Color32(0x2A, 0x1F, 0x0E, 0xFF);
        public static readonly Color32 BackgroundPlayer = new Color32(0x1A, 0x0F, 0x05, 0xFF);

        // Text
        public static readonly Color32 GoldAccent    = new Color32(0xC8, 0xA0, 0x50, 0xFF);
        public static readonly Color32 DmText        = new Color32(0xD4, 0xB8, 0x7A, 0xFF);
        public static readonly Color32 PlayerText    = new Color32(0xF0, 0xD0, 0x90, 0xFF);
        public static readonly Color32 SystemText    = new Color32(0xA0, 0x80, 0x60, 0xFF);
        public static readonly Color32 InputText     = new Color32(0xE8, 0xD0, 0xA0, 0xFF);
        public static readonly Color32 PlaceholderText = new Color32(0x6B, 0x50, 0x30, 0xFF);

        // Font sizes
        public const float FontHeader  = 13f;
        public const float FontDM      = 16f;
        public const float FontPlayer  = 16f;
        public const float FontSystem  = 13f;
        public const float FontInput   = 15f;
    }
}
```

- [ ] **Step 2: Verify compilation in Unity — no new errors**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/UITheme.cs
git commit -m "feat: add UITheme color and font-size constants"
```

---

## Task 3: Refactor ChatUI.cs

**Files:**
- Modify: `Assets/Scripts/UI/ChatUI.cs`

ChatUI no longer uses prefabs. Messages are built programmatically from `UITheme` constants. The three `[SerializeField] private GameObject *Prefab` fields are removed.

- [ ] **Step 1: Replace the entire content of ChatUI.cs**

```csharp
// Assets/Scripts/UI/ChatUI.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DnD.UI
{
    public class ChatUI : MonoBehaviour
    {
        public static ChatUI Instance { get; private set; }

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

        // ── Internal ──────────────────────────────────────────────────

        private void AddMessage(string text, MessageType type)
        {
            if (contentPanel == null) { Debug.LogError("[ChatUI] contentPanel is null"); return; }

            GameObject msgGO = BuildMessageGO(text, type);
            activeMessages.Add(msgGO);
            TrimHistory();
            if (autoScroll) StartCoroutine(ScrollToBottom());
        }

        private GameObject BuildMessageGO(string text, MessageType type)
        {
            var msgGO = new GameObject($"Msg_{type}");
            msgGO.transform.SetParent(contentPanel, false);

            // Size fitter so the bubble grows with text
            var csf = msgGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var le = msgGO.AddComponent<LayoutElement>();
            le.minHeight = 24;

            // Bubble background
            var bg = msgGO.AddComponent<Image>();

            // Text child fills the bubble with padding
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(msgGO.transform, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10, 6);
            textRT.offsetMax = new Vector2(-10, -6);

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.enableWordWrapping = true;
            tmp.text = text;

            switch (type)
            {
                case MessageType.DM:
                    bg.color = UITheme.BackgroundDM;
                    tmp.color = UITheme.DmText;
                    tmp.fontSize = UITheme.FontDM;
                    tmp.fontStyle = FontStyles.Italic;
                    tmp.alignment = TextAlignmentOptions.TopLeft;
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
            var msgGO = new GameObject("Msg_DM");
            msgGO.transform.SetParent(contentPanel, false);
            var csf = msgGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var le = msgGO.AddComponent<LayoutElement>();
            le.minHeight = 24;
            var bg = msgGO.AddComponent<Image>();
            bg.color = UITheme.BackgroundDM;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(msgGO.transform, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10, 6);
            textRT.offsetMax = new Vector2(-10, -6);

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.enableWordWrapping = true;
            tmp.color = UITheme.DmText;
            tmp.fontSize = UITheme.FontDM;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.text = "";

            activeMessages.Add(msgGO);
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
    }
}
```

- [ ] **Step 2: Verify compilation in Unity — no errors**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/ChatUI.cs
git commit -m "refactor: ChatUI creates messages programmatically using UITheme, removes prefab dependency"
```

---

## Task 4: Create MapCameraController.cs

**Files:**
- Create: `Assets/Scripts/UI/MapCameraController.cs`

This component lives on the `MapCamera` GameObject. At `Awake` it creates a `RenderTexture`, sets itself as the camera's target, and assigns the texture to the `RawImage` named `"MapDisplay"` in the scene.

- [ ] **Step 1: Create the file**

```csharp
// Assets/Scripts/UI/MapCameraController.cs
using UnityEngine;
using UnityEngine.UI;

namespace DnD.UI
{
    [RequireComponent(typeof(Camera))]
    public class MapCameraController : MonoBehaviour
    {
        public static MapCameraController Instance { get; private set; }

        [SerializeField] private int renderWidth  = 1024;
        [SerializeField] private int renderHeight = 1024;

        private RenderTexture renderTexture;
        private Camera mapCamera;

        private void Awake()
        {
            Instance = this;
            mapCamera = GetComponent<Camera>();

            renderTexture = new RenderTexture(renderWidth, renderHeight, 16, RenderTextureFormat.ARGB32);
            renderTexture.Create();
            mapCamera.targetTexture = renderTexture;

            // Find the RawImage in the scene named "MapDisplay" and assign texture
            var rawImages = FindObjectsOfType<RawImage>();
            foreach (var ri in rawImages)
            {
                if (ri.gameObject.name == "MapDisplay")
                {
                    ri.texture = renderTexture;
                    break;
                }
            }
        }

        private void OnDestroy()
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
        }

        public Camera MapCamera => mapCamera;
    }
}
```

- [ ] **Step 2: Verify compilation in Unity — no errors**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/MapCameraController.cs
git commit -m "feat: add MapCameraController to wire RenderTexture to MapDisplay RawImage"
```

---

## Task 5: Create UISceneBuilder.cs Editor script

**Files:**
- Create: `Assets/Editor/UISceneBuilder.cs`

This runs once from the Unity menu (`DnD → Rebuild UI Canvas`) to tear down the existing Canvas and build the new one. It also adds a `MapCamera` GameObject and the `MapCameraController` component. Save the scene after running it.

- [ ] **Step 1: Create the Editor folder if it doesn't exist**

```bash
mkdir -p Assets/Editor
```

- [ ] **Step 2: Create UISceneBuilder.cs**

```csharp
// Assets/Editor/UISceneBuilder.cs
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;
using DnD.UI;

public static class UISceneBuilder
{
    [MenuItem("DnD/Rebuild UI Canvas")]
    public static void RebuildCanvas()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // ── Remove old Canvas ─────────────────────────────────────────
        foreach (var c in Object.FindObjectsOfType<Canvas>())
            Undo.DestroyObjectImmediate(c.gameObject);

        // ── Canvas root ───────────────────────────────────────────────
        var canvasGO = new GameObject("Canvas");
        Undo.RegisterCreatedObjectUndo(canvasGO, "Rebuild Canvas");

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ChatUI lives on the Canvas root
        var chatUI = canvasGO.AddComponent<ChatUI>();

        // ── Horizontal split (fills entire canvas) ────────────────────
        var split = MakeGO("Split", canvasGO.transform);
        var splitRT = split.GetComponent<RectTransform>();
        splitRT.anchorMin = Vector2.zero;
        splitRT.anchorMax = Vector2.one;
        splitRT.offsetMin = Vector2.zero;
        splitRT.offsetMax = Vector2.zero;
        var hlg = split.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 4;
        hlg.padding = new RectOffset(4, 4, 4, 4);
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childControlHeight = true;
        hlg.childControlWidth = true;

        // ── Map panel (60%) ───────────────────────────────────────────
        var mapPanel = MakePanel("MapPanel", split.transform, UITheme.BackgroundMid);
        mapPanel.AddComponent<LayoutElement>().flexibleWidth = 1.5f;
        var mapVLG = mapPanel.AddComponent<VerticalLayoutGroup>();
        mapVLG.childForceExpandWidth = true;
        mapVLG.childForceExpandHeight = false;
        mapVLG.childControlWidth = true;
        mapVLG.childControlHeight = true;

        AddHeader(mapPanel.transform, "✦  THE MAP  ✦");

        var mapDisplayGO = MakeGO("MapDisplay", mapPanel.transform);
        mapDisplayGO.AddComponent<RawImage>().color = Color.white;
        var mapDisplayLE = mapDisplayGO.AddComponent<LayoutElement>();
        mapDisplayLE.flexibleHeight = 1;
        mapDisplayLE.flexibleWidth = 1;

        // ── Chat panel (40%) ──────────────────────────────────────────
        var chatPanel = MakePanel("ChatPanel", split.transform, UITheme.BackgroundDeep);
        chatPanel.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var chatVLG = chatPanel.AddComponent<VerticalLayoutGroup>();
        chatVLG.childForceExpandWidth = true;
        chatVLG.childForceExpandHeight = false;
        chatVLG.childControlWidth = true;
        chatVLG.childControlHeight = true;

        AddHeader(chatPanel.transform, "✦  DUNGEON MASTER  ✦");

        // ── Scroll view ───────────────────────────────────────────────
        var scrollGO = MakeGO("ScrollView", chatPanel.transform);
        scrollGO.AddComponent<Image>().color = Color.clear;
        var scrollLE = scrollGO.AddComponent<LayoutElement>();
        scrollLE.flexibleHeight = 1;
        scrollLE.flexibleWidth = 1;
        var scrollRect = scrollGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.scrollSensitivity = 20;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGO = MakeGO("Viewport", scrollGO.transform);
        var viewportRT = viewportGO.GetComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewportGO.AddComponent<Image>().color = Color.clear;
        var mask = viewportGO.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        scrollRect.viewport = viewportRT;

        var contentGO = MakeGO("Content", viewportGO.transform);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot    = new Vector2(0.5f, 1f);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;
        var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
        contentVLG.spacing = 4;
        contentVLG.padding = new RectOffset(8, 8, 8, 8);
        contentVLG.childForceExpandWidth = true;
        contentVLG.childForceExpandHeight = false;
        contentVLG.childControlWidth = true;
        contentVLG.childControlHeight = true;
        var contentCSF = contentGO.AddComponent<ContentSizeFitter>();
        contentCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = contentRT;

        // ── Input row ─────────────────────────────────────────────────
        var inputRow = MakeGO("InputRow", chatPanel.transform);
        var inputRowLE = inputRow.AddComponent<LayoutElement>();
        inputRowLE.preferredHeight = 44;
        inputRowLE.minHeight = 44;
        var inputRowHLG = inputRow.AddComponent<HorizontalLayoutGroup>();
        inputRowHLG.spacing = 4;
        inputRowHLG.padding = new RectOffset(8, 8, 6, 6);
        inputRowHLG.childForceExpandHeight = true;
        inputRowHLG.childForceExpandWidth = false;
        inputRowHLG.childControlHeight = true;
        inputRowHLG.childControlWidth = true;

        // Input field container
        var inputGO = MakeGO("InputField", inputRow.transform);
        inputGO.AddComponent<Image>().color = UITheme.BackgroundDeep;
        inputGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        var inputField = inputGO.AddComponent<TMP_InputField>();

        // Text Area
        var textAreaGO = MakeGO("Text Area", inputGO.transform);
        var textAreaRT = textAreaGO.GetComponent<RectTransform>();
        textAreaRT.anchorMin = Vector2.zero;
        textAreaRT.anchorMax = Vector2.one;
        textAreaRT.offsetMin = new Vector2(8, 4);
        textAreaRT.offsetMax = new Vector2(-8, -4);
        textAreaGO.AddComponent<RectMask2D>();
        inputField.textViewport = textAreaRT;

        // Placeholder
        var phGO = MakeGO("Placeholder", textAreaGO.transform);
        var phRT = phGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;
        var phTMP = phGO.AddComponent<TextMeshProUGUI>();
        phTMP.text = "What do you do?";
        phTMP.color = UITheme.PlaceholderText;
        phTMP.fontSize = UITheme.FontInput;
        phTMP.fontStyle = FontStyles.Italic;
        phTMP.enableWordWrapping = false;
        inputField.placeholder = phTMP;

        // Text
        var inputTextGO = MakeGO("Text", textAreaGO.transform);
        var inputTextRT = inputTextGO.GetComponent<RectTransform>();
        inputTextRT.anchorMin = Vector2.zero; inputTextRT.anchorMax = Vector2.one;
        inputTextRT.offsetMin = Vector2.zero; inputTextRT.offsetMax = Vector2.zero;
        var inputTMP = inputTextGO.AddComponent<TextMeshProUGUI>();
        inputTMP.color = UITheme.InputText;
        inputTMP.fontSize = UITheme.FontInput;
        inputTMP.enableWordWrapping = false;
        inputField.textComponent = inputTMP;

        // Send button
        var btnGO = MakeGO("SendButton", inputRow.transform);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = UITheme.GoldAccent;
        btnGO.AddComponent<LayoutElement>().preferredWidth = 60;
        var btn = btnGO.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor    = UITheme.GoldAccent;
        colors.highlightedColor = new Color32(0xE0, 0xC0, 0x70, 0xFF);
        colors.pressedColor     = new Color32(0xA0, 0x80, 0x30, 0xFF);
        btn.colors = colors;
        var btnTextGO = MakeGO("Text", btnGO.transform);
        var btnTextRT = btnTextGO.GetComponent<RectTransform>();
        btnTextRT.anchorMin = Vector2.zero; btnTextRT.anchorMax = Vector2.one;
        btnTextRT.offsetMin = Vector2.zero; btnTextRT.offsetMax = Vector2.zero;
        var btnTMP = btnTextGO.AddComponent<TextMeshProUGUI>();
        btnTMP.text = "▶";
        btnTMP.fontSize = 20;
        btnTMP.color = UITheme.BackgroundDeep;
        btnTMP.alignment = TextAlignmentOptions.Center;

        // ── Wire ChatUI serialized fields ─────────────────────────────
        var so = new SerializedObject(chatUI);
        so.FindProperty("scrollRect").objectReferenceValue    = scrollRect;
        so.FindProperty("contentPanel").objectReferenceValue  = contentRT;
        so.FindProperty("inputField").objectReferenceValue    = inputField;
        so.FindProperty("sendButton").objectReferenceValue    = btn;
        so.ApplyModifiedProperties();

        // ── MapCamera ─────────────────────────────────────────────────
        // Remove old MapCamera if present
        var oldMapCam = GameObject.Find("MapCamera");
        if (oldMapCam != null) Undo.DestroyObjectImmediate(oldMapCam);

        var mapCamGO = new GameObject("MapCamera");
        Undo.RegisterCreatedObjectUndo(mapCamGO, "Create MapCamera");
        mapCamGO.tag = "MapCamera";

        var mapCam = mapCamGO.AddComponent<Camera>();
        mapCam.orthographic = true;
        mapCam.orthographicSize = 5f;
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = new Color32(0x1E, 0x15, 0x08, 0xFF);
        mapCam.transform.position = new Vector3(4.5f, 10f, 4.5f);
        mapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        mapCamGO.AddComponent<MapCameraController>();

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[UISceneBuilder] Canvas rebuilt. Press Ctrl+S to save the scene.");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static GameObject MakeGO(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static GameObject MakePanel(string name, Transform parent, Color32 color)
    {
        var go = MakeGO(name, parent);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static void AddHeader(Transform parent, string title)
    {
        var headerGO = MakeGO("Header", parent);
        var headerLE = headerGO.AddComponent<LayoutElement>();
        headerLE.preferredHeight = 40;
        headerLE.minHeight = 40;
        headerGO.AddComponent<Image>().color = new Color32(0x12, 0x0C, 0x03, 0xFF);

        // Title text
        var textGO = MakeGO("Text", headerGO.transform);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero; textRT.offsetMax = new Vector2(0, -2);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = title;
        tmp.fontSize = UITheme.FontHeader;
        tmp.color = UITheme.GoldAccent;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.characterSpacing = 1.5f;

        // Gold divider line
        var divGO = MakeGO("Divider", headerGO.transform);
        var divRT = divGO.GetComponent<RectTransform>();
        divRT.anchorMin = new Vector2(0, 0);
        divRT.anchorMax = new Vector2(1, 0);
        divRT.pivot     = new Vector2(0.5f, 0f);
        divRT.offsetMin = new Vector2(8, 0);
        divRT.offsetMax = new Vector2(-8, 2);
        divGO.AddComponent<Image>().color = UITheme.GoldAccent;
    }
}
```

- [ ] **Step 3: Verify compilation in Unity — no errors**

- [ ] **Step 4: Commit**

```bash
git add Assets/Editor/UISceneBuilder.cs
git commit -m "feat: add UISceneBuilder Editor script to rebuild Canvas with parchment theme"
```

---

## Task 6: Run UISceneBuilder and save the scene

**Files:**
- Modify: `Assets/masterscene.unity` (via Unity Editor)

- [ ] **Step 1: Open Unity, go to menu DnD → Rebuild UI Canvas**

The Console should print:
```
[UISceneBuilder] Canvas rebuilt. Press Ctrl+S to save the scene.
```

Check the Hierarchy — it should show:
```
Canvas
  Split
    MapPanel
      Header
      MapDisplay
    ChatPanel
      Header
      ScrollView
      InputRow
MapCamera
```

- [ ] **Step 2: Press Ctrl+S to save the scene**

- [ ] **Step 3: Enter Play mode**

Expected: game view shows two dark-brown panels side by side with gold header text. The map panel shows a dark background (RenderTexture not yet wired to map tiles — that's Task 7). The chat panel shows the input row with gold send button.

- [ ] **Step 4: Commit**

```bash
git add Assets/masterscene.unity
git commit -m "feat: rebuild scene Canvas with parchment-and-gold D&D layout"
```

---

## Task 7: Update MapGenerator.cs

**Files:**
- Modify: `Assets/Scripts/Map/MapGenerator.cs`

Two changes: (1) remove the `DNDLLM.Core.GameManager` state-change call at the end of `GenerateMap`, (2) add ChatUI loading messages, (3) update `AdjustCamera` to use the MapCamera instead of `Camera.main`.

- [ ] **Step 1: Remove the `DNDLLM.Core` using and fix the end of GenerateMap**

Remove line 4: `using DNDLLM.Core;`

Remove lines 104–107 (the `DNDLLM.Core.GameManager.Instance.ChangeState(...)` block):
```csharp
// DELETE these lines:
if (DNDLLM.Core.GameManager.Instance != null)
    DNDLLM.Core.GameManager.Instance.ChangeState(DNDLLM.Core.GameState.CharacterGeneration);
```

Replace with (just below the ASCII log):
```csharp
            Debug.Log("Map Generation Complete.");
```

- [ ] **Step 2: Add ChatUI loading messages at the start and end of GenerateMap**

At the top of `GenerateMap`, after the cleanup loop (line 49), add:
```csharp
            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("⚙  Generating map...");
```

Just before the final `Debug.Log("Map Generation Complete.");`, add:
```csharp
            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("✦  Map ready.");
```

- [ ] **Step 3: Update AdjustCamera() to use MapCamera**

Replace the entire `AdjustCamera()` method with:

```csharp
        private void AdjustCamera()
        {
            // Use the dedicated MapCamera (renders to RenderTexture) instead of Camera.main
            Camera cam = null;
            var mapCamGO = GameObject.Find("MapCamera");
            if (mapCamGO != null) cam = mapCamGO.GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            float centerX = (width - 1) * cellSize / 2f;
            float centerZ = (height - 1) * cellSize / 2f;
            cam.transform.position = new Vector3(centerX, 10f, centerZ);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.orthographic = true;

            float targetHeight = height * cellSize;
            float targetWidth  = width  * cellSize;
            float screenRatio  = (float)Screen.width / (float)Screen.height;
            float targetRatio  = targetWidth / targetHeight;

            cam.orthographicSize = screenRatio >= targetRatio
                ? (targetHeight / 2f) + 1f
                : (targetHeight / 2f * (targetRatio / screenRatio)) + 1f;
        }
```

- [ ] **Step 4: Verify compilation in Unity — no errors**

- [ ] **Step 5: Enter Play mode — verify loading message appears in chat panel**

Expected Console + chat panel:
```
⚙  Generating map...    ← system message (italic amber, centered)
✦  Map ready.           ← after generation completes
```

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Map/MapGenerator.cs
git commit -m "fix: remove legacy Core.GameManager ref from MapGenerator; add ChatUI loading messages; use MapCamera"
```

---

## Task 8: Update DnD.Managers.GameManager to trigger map generation and improve messages

**Files:**
- Modify: `Assets/Scripts/Managers/GameManager.cs`

- [ ] **Step 1: Add `using DNDLLM.Map;` at the top of GameManager.cs**

After line 8 (`using DnD.Combat;`), add:
```csharp
using DNDLLM.Map;
```

- [ ] **Step 2: Update ShowMainMenu() to use themed symbols**

Replace `ShowMainMenu()` (lines 142–152) with:
```csharp
        private void ShowMainMenu()
        {
            if (ChatUI.Instance == null) return;
            ChatUI.Instance.AddSystemMessage("✦  WELCOME TO D&D LLM  ✦");
            ChatUI.Instance.AddSystemMessage("An adventure powered by AI.");
            ChatUI.Instance.AddSystemMessage("Type 'start' to begin — or describe the world you want to explore.");
        }
```

- [ ] **Step 3: Update StartCharacterCreation() messages**

Replace `StartCharacterCreation()` (lines 154–163) with:
```csharp
        private void StartCharacterCreation()
        {
            if (ChatUI.Instance == null) return;
            ChatUI.Instance.AddSystemMessage("✦  CHARACTER CREATION  ✦");
            ChatUI.Instance.AddSystemMessage("Describe your hero — class, background, personality.");
        }
```

- [ ] **Step 4: Update StartExploration() to trigger map generation**

Replace `StartExploration()` (lines 165–170) with:
```csharp
        private void StartExploration()
        {
            // Generate the map
            if (MapGenerator.Instance != null)
                MapGenerator.Instance.GenerateMap("dungeon");

            if (ChatUI.Instance == null) return;
            ChatUI.Instance.AddSystemMessage("✦  YOUR ADVENTURE BEGINS  ✦");
        }
```

- [ ] **Step 5: Verify compilation in Unity — no errors**

- [ ] **Step 6: Enter Play mode — full end-to-end check**

Expected sequence in the Chat panel:
1. `✦  WELCOME TO D&D LLM  ✦` (system, centered amber)
2. Type "start" → `✦  CHARACTER CREATION  ✦`
3. Describe a character → `✦  YOUR ADVENTURE BEGINS  ✦`
4. `⚙  Generating map...`
5. Map appears in the left panel via RenderTexture
6. `✦  Map ready.`

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Managers/GameManager.cs
git commit -m "feat: GameManager triggers map generation on Exploration; improve system message formatting"
```

---

## Verification Checklist

- [ ] Unity Console shows no compilation errors
- [ ] Game view shows two dark-brown panels side by side (not white boxes)
- [ ] Left panel header reads "✦  THE MAP  ✦" in gold
- [ ] Right panel header reads "✦  DUNGEON MASTER  ✦" in gold
- [ ] Map tiles display correctly in the left panel (not three horizontal strips)
- [ ] DM messages appear italic, gold-tinted, left-aligned
- [ ] Player messages appear lighter gold, right-aligned
- [ ] System messages appear centered, italic, amber, no background
- [ ] "⚙  Generating map..." and "✦  Map ready." appear during generation
- [ ] Input field placeholder reads "What do you do?" in muted amber
- [ ] Send button is gold with dark "▶" text
- [ ] No references to `DNDLLM.Core.GameManager` or `DNDLLM.UI.UIManager` remain in any script
