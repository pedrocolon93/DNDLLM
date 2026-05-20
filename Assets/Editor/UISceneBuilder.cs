// Assets/Editor/UISceneBuilder.cs
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;
using DnD.UI;
using DnD.Managers;
using DNDLLM.Map;

public static class UISceneBuilder
{
    [MenuItem("DnD/Rebuild UI Canvas")]
    public static void RebuildCanvas()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // ── Remove old Canvas ─────────────────────────────────────────
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            Undo.DestroyObjectImmediate(c.gameObject);

        // ── EventSystem (required for any UI button to receive clicks) ─
        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

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

        AddHeader(mapPanel.transform, "THE MAP");

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

        AddHeader(chatPanel.transform, "DUNGEON MASTER");

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
        // RectMask2D clips to bounds without needing a visible Image (Mask+alpha=0 hides everything)
        viewportGO.AddComponent<RectMask2D>();
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
        inputGO.AddComponent<Image>().color = new Color32(0x38, 0x28, 0x10, 0xFF); // visibly lighter than panel
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
        phTMP.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        inputField.placeholder = phTMP;

        // Text
        var inputTextGO = MakeGO("Text", textAreaGO.transform);
        var inputTextRT = inputTextGO.GetComponent<RectTransform>();
        inputTextRT.anchorMin = Vector2.zero; inputTextRT.anchorMax = Vector2.one;
        inputTextRT.offsetMin = Vector2.zero; inputTextRT.offsetMax = Vector2.zero;
        var inputTMP = inputTextGO.AddComponent<TextMeshProUGUI>();
        inputTMP.color = UITheme.InputText;
        inputTMP.fontSize = UITheme.FontInput;
        inputTMP.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        inputField.textComponent = inputTMP;

        // Send button
        var btnGO = MakeGO("SendButton", inputRow.transform);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = UITheme.GoldAccent;
        btnGO.AddComponent<LayoutElement>().preferredWidth = 60;
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
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
        btnTMP.text = "Send";
        btnTMP.fontSize = 13;
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

        var mapCam = mapCamGO.AddComponent<Camera>();
        mapCam.orthographic = true;
        mapCam.orthographicSize = 5f;
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = new Color32(0x1E, 0x15, 0x08, 0xFF);
        mapCam.transform.position = new Vector3(4.5f, 10f, 4.5f);
        mapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        mapCamGO.AddComponent<MapCameraController>();

        // ── HUD: three top-right buttons — CHARACTER | EDIT MAP | ≡ MENU ────────
        // Helper: makes one 90×32 HUD button anchored to top-right
        Button MakeHudButton(string goName, string label, float rightOffset)
        {
            var go = MakeGO(goName, canvasGO.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(rightOffset, -8f);
            rt.sizeDelta        = new Vector2(90f, 32f);
            var img = go.AddComponent<Image>();
            img.color = new Color32(0x12, 0x0C, 0x03, 0xCC);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.highlightedColor = new Color32(0x28, 0x1E, 0x10, 0xCC);
            btn.colors = c;
            var txtGO = MakeGO("Text", go.transform);
            var txtRT = txtGO.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
            var tmp = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 11f;
            tmp.color = UITheme.GoldAccent;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.characterSpacing = 0.5f;
            return btn;
        }

        // rightOffset: -8 = MENU, -102 = EDIT MAP, -196 = CHARACTER
        // Leading glyphs must be ones LiberationSans actually contains, otherwise TMP
        // logs "character not found" warnings and renders □. Safe picks: Math Operators
        // (U+2200-22FF) and General Punctuation (U+2000-206F). Avoid Dingbats (U+2700-27BF)
        // and most of Geometric Shapes (U+25A0-25FF) — not in LiberationSans.
        var menuBtn      = MakeHudButton("MenuButton",      "≡  MENU",      -8f);
        var editMapBtn   = MakeHudButton("EditMapButton",   "•  EDIT MAP",  -102f);
        var characterBtn = MakeHudButton("CharacterButton", "†  CHARACTER", -196f);

        // ── Turn-order strip — top-center, anchored above the chat/map split ──
        // Single TMP_Text driven by GameManager via the TurnQueue.OnTurnChanged event.
        // Reads e.g. "▶ Aric → Lyra → Goblin" with the active entry styled gold/bold.
        var turnStripGO = MakeGO("TurnStrip", canvasGO.transform);
        var turnStripRT = turnStripGO.GetComponent<RectTransform>();
        turnStripRT.anchorMin        = new Vector2(0.5f, 1f);
        turnStripRT.anchorMax        = new Vector2(0.5f, 1f);
        turnStripRT.pivot            = new Vector2(0.5f, 1f);
        turnStripRT.anchoredPosition = new Vector2(0f, -8f);
        turnStripRT.sizeDelta        = new Vector2(540f, 28f);
        var turnStripTMP = turnStripGO.AddComponent<TextMeshProUGUI>();
        turnStripTMP.text       = "";
        turnStripTMP.fontSize   = 13f;
        turnStripTMP.color      = UITheme.SystemText;
        turnStripTMP.alignment  = TextAlignmentOptions.Center;
        turnStripTMP.characterSpacing = 1f;

        // Wire to GameManager if present
        var gameSystemGO = GameObject.Find("GameSystem");
        if (gameSystemGO != null)
        {
            var gm = gameSystemGO.GetComponent<DnD.Managers.GameManager>();
            if (gm != null)
            {
                var gmSO = new SerializedObject(gm);
                gmSO.FindProperty("menuButton").objectReferenceValue      = menuBtn;
                gmSO.FindProperty("editMapButton").objectReferenceValue   = editMapBtn;
                gmSO.FindProperty("characterButton").objectReferenceValue = characterBtn;
                var turnStripProp = gmSO.FindProperty("turnStripText");
                if (turnStripProp != null) turnStripProp.objectReferenceValue = turnStripTMP;
                gmSO.ApplyModifiedProperties();
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[UISceneBuilder] Canvas rebuilt. Press Ctrl+S to save the scene.");
    }

    // Names of root GameObjects that the builders create. CleanupScene destroys any
    // matching root object so duplicates and script-orphaned canvases get swept away.
    // GameSystem is intentionally excluded — it holds the LLMService API key and other
    // serialized inspector values; SetupGameManager idempotently attaches missing components.
    private static readonly string[] BuilderRootObjectNames =
    {
        "Canvas",
        "MapCamera",
        "MapCharacter",
        "EventSystem",
        "TitleScreenCanvas",
        "AdventurePromptCanvas",
        "CharacterPopupCanvas",
        "EditMapCanvas",
        "CharacterScreenCanvas",
        "InGameMenuCanvas",
    };

    [MenuItem("DnD/Cleanup Scene")]
    public static void CleanupScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        int removed = 0;
        foreach (var go in scene.GetRootGameObjects())
        {
            foreach (var n in BuilderRootObjectNames)
            {
                if (go.name == n)
                {
                    Undo.DestroyObjectImmediate(go);
                    removed++;
                    break;
                }
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[UISceneBuilder] CleanupScene removed {removed} builder-owned root GameObjects.");
    }

    [MenuItem("DnD/Setup Scene (All Steps)")]
    public static void SetupSceneAll()
    {
        CleanupScene();
        RebuildCanvas();
        SetupGameManager();
        BuildTitleScreen();
        BuildAdventurePromptPopup();
        BuildCharacterPopup();
        BuildInGameMenuPanel();
        BuildEditMapPanel();
        BuildCharacterScreen();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[UISceneBuilder] Full scene setup complete. Press Ctrl+S to save.");
    }

    [MenuItem("DnD/Setup Game Manager")]
    public static void SetupGameManager()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // Attach GameManager to the existing "GameSystem" object, or create one
        var gameSystemGO = GameObject.Find("GameSystem");
        if (gameSystemGO == null)
        {
            gameSystemGO = new GameObject("GameSystem");
            Undo.RegisterCreatedObjectUndo(gameSystemGO, "Create GameSystem");
        }

        if (gameSystemGO.GetComponent<GameManager>() == null)
        {
            Undo.AddComponent<GameManager>(gameSystemGO);
            Debug.Log("[UISceneBuilder] DnD.Managers.GameManager added to GameSystem. Press Ctrl+S to save.");
        }
        else
        {
            Debug.Log("[UISceneBuilder] GameManager already present on GameSystem.");
        }

        if (gameSystemGO.GetComponent<DNDLLM.Services.LLMService>() == null)
        {
            Undo.AddComponent<DNDLLM.Services.LLMService>(gameSystemGO);
            Debug.Log("[UISceneBuilder] LLMService added to GameSystem — paste your API key into its Inspector.");
        }

        if (gameSystemGO.GetComponent<DNDLLM.Services.TTSService>() == null)
        {
            Undo.AddComponent<DNDLLM.Services.TTSService>(gameSystemGO);
            Debug.Log("[UISceneBuilder] TTSService added to GameSystem.");
        }

        if (gameSystemGO.GetComponent<DNDLLM.Map.MapGenerator>() == null)
        {
            Undo.AddComponent<DNDLLM.Map.MapGenerator>(gameSystemGO);
            Debug.Log("[UISceneBuilder] MapGenerator added to GameSystem.");
        }

        // MapCharacterController lives on its own GameObject because Initialize()
        // moves the transform to the player's grid cell — putting it on GameSystem
        // would drag every manager (GameManager, LLMService, MapGenerator) along with it.
        if (Object.FindAnyObjectByType<DNDLLM.Map.MapCharacterController>() == null)
        {
            var charGO = new GameObject("MapCharacter");
            Undo.RegisterCreatedObjectUndo(charGO, "Create MapCharacter");
            charGO.AddComponent<SpriteRenderer>();
            charGO.AddComponent<DNDLLM.Map.MapCharacterController>();
            Debug.Log("[UISceneBuilder] MapCharacter GameObject created with MapCharacterController.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    [MenuItem("DnD/Build Title Screen")]
    public static void BuildTitleScreen()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // Remove old TitleScreen canvas if present
        foreach (var c in Object.FindObjectsByType<DnD.UI.TitleScreen>(FindObjectsInactive.Exclude))
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
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta        = new Vector2(440f, 0f);
        var panelVLG = panelGO.AddComponent<VerticalLayoutGroup>();
        panelVLG.spacing              = 8f;
        panelVLG.padding              = new RectOffset(0, 0, 0, 0);
        panelVLG.childForceExpandWidth  = true;
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
        var deleteButtons     = new Button[3];

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

            // Portrait thumbnail — Image (bg) on outer GO, RawImage on child GO
            var portraitGO = MakeGO("Portrait", rowGO.transform);
            portraitGO.AddComponent<LayoutElement>().preferredWidth = 40f;
            portraitGO.AddComponent<Image>().color = UITheme.BackgroundDeep;
            var rawImgGO = MakeGO("RawImage", portraitGO.transform);
            var rawImgRT = rawImgGO.GetComponent<RectTransform>();
            rawImgRT.anchorMin = Vector2.zero; rawImgRT.anchorMax = Vector2.one;
            rawImgRT.offsetMin = Vector2.zero; rawImgRT.offsetMax = Vector2.zero;
            var rawImg = rawImgGO.AddComponent<RawImage>();
            rawImg.color = Color.clear;
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

            slotNameTexts[i]     = AddInfoText(infoGO.transform, "Name",     UITheme.DmText,          14f);
            slotSubTexts[i]      = AddInfoText(infoGO.transform, "Sub",      UITheme.SystemText,       11f);
            slotCampaignTexts[i] = AddInfoText(infoGO.transform, "Campaign", UITheme.SystemText,       10f);
            slotDateTexts[i]     = AddInfoText(infoGO.transform, "Date",     UITheme.PlaceholderText,  10f);

            // Chevron
            var chevronGO = MakeGO("Chevron", rowGO.transform);
            chevronGO.AddComponent<LayoutElement>().preferredWidth = 20f;
            var chevTMP = chevronGO.AddComponent<TextMeshProUGUI>();
            chevTMP.text      = "›";
            chevTMP.fontSize  = 20f;
            chevTMP.color     = UITheme.GoldAccent;
            chevTMP.alignment = TextAlignmentOptions.MidlineRight;

            // Delete button — small red "X" on the right
            var delGO = MakeGO("DeleteButton", rowGO.transform);
            var delLE = delGO.AddComponent<LayoutElement>();
            delLE.preferredWidth  = 24f;
            delLE.preferredHeight = 24f;
            delLE.flexibleHeight  = 0f;
            var delImg = delGO.AddComponent<Image>();
            delImg.color = new Color32(0x8B, 0x00, 0x00, 0xCC);
            var delBtn = delGO.AddComponent<Button>();
            delBtn.targetGraphic = delImg;
            var delColors = delBtn.colors;
            delColors.highlightedColor = new Color32(0xCC, 0x00, 0x00, 0xFF);
            delColors.pressedColor     = new Color32(0x55, 0x00, 0x00, 0xFF);
            delBtn.colors = delColors;
            var delTextGO = MakeGO("Text", delGO.transform);
            var delTextRT = delTextGO.GetComponent<RectTransform>();
            delTextRT.anchorMin = Vector2.zero; delTextRT.anchorMax = Vector2.one;
            delTextRT.offsetMin = Vector2.zero; delTextRT.offsetMax = Vector2.zero;
            var delTMP = delTextGO.AddComponent<TextMeshProUGUI>();
            delTMP.text      = "X";
            delTMP.fontSize  = 12f;
            delTMP.color     = Color.white;
            delTMP.alignment = TextAlignmentOptions.Center;
            deleteButtons[i] = delBtn;

            slotButtons[i] = rowBtn;
        }

        // ── Wire TitleScreen fields ──────────────────────────────────────
        var so = new SerializedObject(titleScreen);
        SetProp(so, "newGameButton", ngBtn);
        SetArrayProp(so, "slotButtons",       slotButtons);
        SetArrayProp(so, "slotPortraits",     slotPortraits);
        SetArrayProp(so, "slotNameTexts",     slotNameTexts);
        SetArrayProp(so, "slotSubTexts",      slotSubTexts);
        SetArrayProp(so, "slotCampaignTexts", slotCampaignTexts);
        SetArrayProp(so, "slotDateTexts",     slotDateTexts);
        SetArrayProp(so, "deleteButtons",     deleteButtons);
        so.ApplyModifiedProperties();

        // Wire to GameManager if present
        var gmGO = GameObject.Find("GameSystem");
        if (gmGO != null)
        {
            var gm = gmGO.GetComponent<DnD.Managers.GameManager>();
            if (gm != null)
            {
                var gmSO = new SerializedObject(gm);
                gmSO.FindProperty("titleScreen").objectReferenceValue = titleScreen;
                gmSO.ApplyModifiedProperties();
            }
        }

        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[UISceneBuilder] Title screen built. Press Ctrl+S to save.");
    }

    [MenuItem("DnD/Build Character Popup")]
    public static void BuildCharacterPopup()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // Remove old popup canvas
        foreach (var p in Object.FindObjectsByType<DnD.UI.CharacterCreationPopup>(FindObjectsInactive.Exclude))
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
        var headerLE = headerGO.AddComponent<LayoutElement>();
        headerLE.minHeight = 44f; headerLE.preferredHeight = 44f; headerLE.flexibleHeight = 0f;
        headerGO.AddComponent<Image>().color = new Color32(0x12, 0x0C, 0x03, 0xFF);
        var headerTextGO = MakeGO("Text", headerGO.transform);
        var hRT = headerTextGO.GetComponent<RectTransform>();
        hRT.anchorMin = Vector2.zero; hRT.anchorMax = Vector2.one;
        hRT.offsetMin = Vector2.zero; hRT.offsetMax = Vector2.zero;
        var hTMP = headerTextGO.AddComponent<TextMeshProUGUI>();
        hTMP.text = "CREATE YOUR HERO";
        hTMP.fontSize = UITheme.FontHeader;
        hTMP.color = UITheme.GoldAccent;
        hTMP.alignment = TextAlignmentOptions.Center;
        hTMP.characterSpacing = 1.5f;

        // Step indicator row (5 thin bars) — hidden on mode-select/quick-create panels
        var barRowGO = MakeGO("StepIndicator", panelGO.transform);
        var barRowLE = barRowGO.AddComponent<LayoutElement>();
        barRowLE.minHeight = 10f; barRowLE.preferredHeight = 10f; barRowLE.flexibleHeight = 0f;
        barRowGO.AddComponent<Image>().color = UITheme.BackgroundMid;
        var barHLG = barRowGO.AddComponent<HorizontalLayoutGroup>();
        barHLG.padding  = new RectOffset(16, 16, 2, 2);
        barHLG.spacing  = 6f;
        barHLG.childForceExpandHeight = true;
        barHLG.childForceExpandWidth  = true;
        barHLG.childControlHeight = true;
        barHLG.childControlWidth  = true;
        var stepBars = new Image[6]; // 6 steps (Name, Race, Class, Appearance, Backstory, Abilities)
        for (int i = 0; i < 6; i++)
        {
            var barGO = MakeGO($"Bar{i}", barRowGO.transform);
            stepBars[i] = barGO.AddComponent<Image>();
            stepBars[i].color = new Color32(0x4A, 0x38, 0x20, 0xFF);
        }

        // Step label — Image (bg) on outer GO, TextMeshProUGUI on child GO
        var stepLabelGO = MakeGO("StepLabel", panelGO.transform);
        var stepLabelLE = stepLabelGO.AddComponent<LayoutElement>();
        stepLabelLE.minHeight = 24f; stepLabelLE.preferredHeight = 24f; stepLabelLE.flexibleHeight = 0f;
        stepLabelGO.AddComponent<Image>().color = UITheme.BackgroundMid;
        var stepLabelTextGO = MakeGO("Text", stepLabelGO.transform);
        var slRT = stepLabelTextGO.GetComponent<RectTransform>();
        slRT.anchorMin = Vector2.zero; slRT.anchorMax = Vector2.one;
        slRT.offsetMin = Vector2.zero; slRT.offsetMax = Vector2.zero;
        var stepLabelTMP = stepLabelTextGO.AddComponent<TextMeshProUGUI>();
        stepLabelTMP.text      = "";
        stepLabelTMP.fontSize  = 11f;
        stepLabelTMP.color     = UITheme.SystemText;
        stepLabelTMP.alignment = TextAlignmentOptions.Center;

        // Content container (fills remaining space)
        var contentGO = MakeGO("ContentContainer", panelGO.transform);
        contentGO.AddComponent<LayoutElement>().flexibleHeight = 1f;
        contentGO.AddComponent<Image>().color = Color.clear;

        // Helper: make a step panel inside contentGO (stretched, invisible bg)
        System.Func<string, GameObject> makeStepPanel = (pname) =>
        {
            var go = MakeGO(pname, contentGO.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.AddComponent<Image>().color = Color.clear;
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(24, 24, 20, 12);
            vlg.spacing = 14f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth  = true;
            vlg.childControlHeight = true;
            return go;
        };

        // ── Step panels ─────────────────────────────────────────────────
        // Indices: 0=Mode 1=Quick 2=Name 3=Race 4=Class 5=Appearance 6=Backstory 7=AbilityScores 8=Confirm
        var stepPanels = new GameObject[9];

        // ── Panel 0: Mode Select ─────────────────────────────────────────
        stepPanels[0] = makeStepPanel("ModeSelectPanel");
        AddPromptLabel(stepPanels[0].transform, "How will you create your hero?");

        // Two large mode buttons side by side
        var modeRowGO = MakeGO("ModeRow", stepPanels[0].transform);
        modeRowGO.AddComponent<LayoutElement>().flexibleHeight = 1f;
        modeRowGO.AddComponent<Image>().color = Color.clear;
        var modeHLG = modeRowGO.AddComponent<HorizontalLayoutGroup>();
        modeHLG.spacing = 12f;
        modeHLG.childForceExpandHeight = true;
        modeHLG.childForceExpandWidth  = true;
        modeHLG.childControlHeight = true;
        modeHLG.childControlWidth  = true;

        Button modeQuickBtn = MakeModeButton("QuickCreate", modeRowGO.transform,
            "QUICK CREATE", "Describe your hero in your own words — AI fills in the details",
            UITheme.GoldAccent, UITheme.BackgroundDeep);

        Button modeStepBtn = MakeModeButton("StepByStep", modeRowGO.transform,
            "STEP BY STEP", "Choose your name, race, class, and background one at a time",
            UITheme.BackgroundMid, UITheme.DmText);

        // ── Panel 1: Quick Create ────────────────────────────────────────
        stepPanels[1] = makeStepPanel("QuickCreatePanel");
        AddPromptLabel(stepPanels[1].transform, "Describe your character:");
        var quickDescInput = MakeInputField(stepPanels[1].transform,
            "e.g. 'A tall elven wizard named Lyra who grew up in a forest village and specializes in fire magic...'",
            true);
        // Status text for "Analyzing..." feedback
        var quickStatusGO = MakeGO("QuickStatusText", stepPanels[1].transform);
        var quickStatusLE = quickStatusGO.AddComponent<LayoutElement>();
        quickStatusLE.preferredHeight = 20f; quickStatusLE.flexibleHeight = 0f;
        var quickStatusTMP = quickStatusGO.AddComponent<TextMeshProUGUI>();
        quickStatusTMP.fontSize  = 11f;
        quickStatusTMP.color     = UITheme.SystemText;
        quickStatusTMP.alignment = TextAlignmentOptions.Center;
        quickStatusTMP.text      = "";

        // ── Panel 2: Name ────────────────────────────────────────────────
        stepPanels[2] = makeStepPanel("NamePanel");
        AddPromptLabel(stepPanels[2].transform, "What is your hero called?");
        var nameInput = MakeInputField(stepPanels[2].transform, "Adventurer", false);

        // ── Panel 3: Race ────────────────────────────────────────────────
        stepPanels[3] = makeStepPanel("RacePanel");
        AddPromptLabel(stepPanels[3].transform, "Choose your race:");
        var raceGrid = MakeGO("RaceGrid", stepPanels[3].transform);
        raceGrid.AddComponent<Image>().color = Color.clear;
        raceGrid.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var raceGLG = raceGrid.AddComponent<GridLayoutGroup>();
        raceGLG.cellSize    = new Vector2(118f, 38f);
        raceGLG.spacing     = new Vector2(6f, 6f);
        raceGLG.constraint  = GridLayoutGroup.Constraint.FixedColumnCount;
        raceGLG.constraintCount = 3;

        // ── Panel 4: Class ───────────────────────────────────────────────
        stepPanels[4] = makeStepPanel("ClassPanel");
        AddPromptLabel(stepPanels[4].transform, "Choose your class:");
        var classGrid = MakeGO("ClassGrid", stepPanels[4].transform);
        classGrid.AddComponent<Image>().color = Color.clear;
        classGrid.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var classGLG = classGrid.AddComponent<GridLayoutGroup>();
        classGLG.cellSize    = new Vector2(88f, 38f);
        classGLG.spacing     = new Vector2(6f, 6f);
        classGLG.constraint  = GridLayoutGroup.Constraint.FixedColumnCount;
        classGLG.constraintCount = 4;

        // ── Panel 5: Appearance ──────────────────────────────────────────
        stepPanels[5] = makeStepPanel("AppearancePanel");
        AddPromptLabel(stepPanels[5].transform, "Describe your hero's appearance:");
        var appearanceInput = MakeInputField(stepPanels[5].transform, "Tall, scarred warrior with dark hair...", true);
        stepPanels[5].GetComponent<VerticalLayoutGroup>().childForceExpandHeight = true;

        // ── Panel 6: Backstory ───────────────────────────────────────────
        stepPanels[6] = makeStepPanel("BackstoryPanel");
        AddPromptLabel(stepPanels[6].transform, "What brought you to this adventure?");
        var backstoryInput = MakeInputField(stepPanels[6].transform, "My village was destroyed...", true);
        stepPanels[6].GetComponent<VerticalLayoutGroup>().childForceExpandHeight = true;

        // ── Panel 7: Ability Scores (point-buy) ──────────────────────────
        stepPanels[7] = makeStepPanel("AbilityScoresPanel");
        stepPanels[7].GetComponent<VerticalLayoutGroup>().spacing = 4f;

        // Header row: remaining points + AI suggest button
        var abilityHeaderGO = MakeGO("AbilityHeader", stepPanels[7].transform);
        abilityHeaderGO.AddComponent<LayoutElement>().preferredHeight = 30f;
        abilityHeaderGO.AddComponent<Image>().color = Color.clear;
        var aHHlg = abilityHeaderGO.AddComponent<HorizontalLayoutGroup>();
        aHHlg.childForceExpandHeight = true; aHHlg.childForceExpandWidth = false;
        aHHlg.childControlHeight = true; aHHlg.childControlWidth = true; aHHlg.spacing = 6f;

        var abilityPtsGO = MakeGO("PointsLabel", abilityHeaderGO.transform);
        abilityPtsGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var abilityPtsTMP = abilityPtsGO.AddComponent<TextMeshProUGUI>();
        abilityPtsTMP.fontSize  = 11f;
        abilityPtsTMP.color     = UITheme.SystemText;
        abilityPtsTMP.alignment = TextAlignmentOptions.MidlineLeft;
        abilityPtsTMP.text      = $"Points remaining: 27 / 27";

        var suggestBtnGO = MakeGO("SuggestButton", abilityHeaderGO.transform);
        suggestBtnGO.AddComponent<LayoutElement>().preferredWidth = 100f;
        var suggestImg = suggestBtnGO.AddComponent<Image>();
        suggestImg.color = new Color32(0x28, 0x1E, 0x10, 0xFF);
        var suggestBtn = suggestBtnGO.AddComponent<Button>();
        suggestBtn.targetGraphic = suggestImg;
        var suggestTextGO = MakeGO("Text", suggestBtnGO.transform);
        var suggestRT = suggestTextGO.GetComponent<RectTransform>();
        suggestRT.anchorMin = Vector2.zero; suggestRT.anchorMax = Vector2.one;
        suggestRT.offsetMin = Vector2.zero; suggestRT.offsetMax = Vector2.zero;
        var suggestTMP = suggestTextGO.AddComponent<TextMeshProUGUI>();
        suggestTMP.text = "› AI Suggest";
        suggestTMP.fontSize = 10f;
        suggestTMP.color = UITheme.GoldAccent;
        suggestTMP.alignment = TextAlignmentOptions.Center;

        // Status text (AI feedback)
        var abilityStatusGO = MakeGO("AbilityStatus", stepPanels[7].transform);
        var abilityStatusLE = abilityStatusGO.AddComponent<LayoutElement>();
        abilityStatusLE.preferredHeight = 16f; abilityStatusLE.flexibleHeight = 0f;
        var abilityStatusTMP = abilityStatusGO.AddComponent<TextMeshProUGUI>();
        abilityStatusTMP.fontSize  = 9f;
        abilityStatusTMP.color     = UITheme.SystemText;
        abilityStatusTMP.alignment = TextAlignmentOptions.Center;
        abilityStatusTMP.text      = "";

        // Six ability rows
        string[] abilityNames = { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
        var abilityValueLabels = new TextMeshProUGUI[6];
        var abilityModLabels   = new TextMeshProUGUI[6];

        for (int ai = 0; ai < 6; ai++)
        {
            int captureAi = ai;
            var rowGO = MakeGO($"Ability_{abilityNames[ai]}", stepPanels[7].transform);
            var rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 32f; rowLE.flexibleHeight = 0f;
            rowGO.AddComponent<Image>().color = new Color32(0x22, 0x18, 0x0A, 0xFF);
            var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowHLG.padding = new RectOffset(8, 8, 4, 4);
            rowHLG.spacing = 6f;
            rowHLG.childForceExpandHeight = true;
            rowHLG.childForceExpandWidth  = false;
            rowHLG.childControlHeight = true;
            rowHLG.childControlWidth  = true;

            // Ability name label
            var nameGO = MakeGO("Name", rowGO.transform);
            nameGO.AddComponent<LayoutElement>().preferredWidth = 36f;
            var nameTMP = nameGO.AddComponent<TextMeshProUGUI>();
            nameTMP.text = abilityNames[ai]; nameTMP.fontSize = 12f;
            nameTMP.color = UITheme.GoldAccent; nameTMP.alignment = TextAlignmentOptions.MidlineLeft;
            nameTMP.fontStyle = FontStyles.Bold;

            // Minus button
            var minusBtnGO = MakeGO("MinusBtn", rowGO.transform);
            minusBtnGO.AddComponent<LayoutElement>().preferredWidth = 24f;
            var minusBtnImg = minusBtnGO.AddComponent<Image>();
            minusBtnImg.color = new Color32(0x38, 0x28, 0x10, 0xFF);
            var minusBtn = minusBtnGO.AddComponent<Button>();
            minusBtn.targetGraphic = minusBtnImg;
            var minusTextGO = MakeGO("Text", minusBtnGO.transform);
            var minusRT = minusTextGO.GetComponent<RectTransform>();
            minusRT.anchorMin = Vector2.zero; minusRT.anchorMax = Vector2.one;
            minusRT.offsetMin = Vector2.zero; minusRT.offsetMax = Vector2.zero;
            var minusTMP = minusTextGO.AddComponent<TextMeshProUGUI>();
            minusTMP.text = "−"; minusTMP.fontSize = 14f;
            minusTMP.color = UITheme.SystemText; minusTMP.alignment = TextAlignmentOptions.Center;

            // Score value label
            var valGO = MakeGO("Value", rowGO.transform);
            valGO.AddComponent<LayoutElement>().preferredWidth = 28f;
            var valTMP = valGO.AddComponent<TextMeshProUGUI>();
            valTMP.text = "8"; valTMP.fontSize = 14f; valTMP.fontStyle = FontStyles.Bold;
            valTMP.color = UITheme.DmText; valTMP.alignment = TextAlignmentOptions.Center;
            abilityValueLabels[ai] = valTMP;

            // Plus button
            var plusBtnGO = MakeGO("PlusBtn", rowGO.transform);
            plusBtnGO.AddComponent<LayoutElement>().preferredWidth = 24f;
            var plusBtnImg = plusBtnGO.AddComponent<Image>();
            plusBtnImg.color = new Color32(0x38, 0x28, 0x10, 0xFF);
            var plusBtn = plusBtnGO.AddComponent<Button>();
            plusBtn.targetGraphic = plusBtnImg;
            var plusTextGO = MakeGO("Text", plusBtnGO.transform);
            var plusRT = plusTextGO.GetComponent<RectTransform>();
            plusRT.anchorMin = Vector2.zero; plusRT.anchorMax = Vector2.one;
            plusRT.offsetMin = Vector2.zero; plusRT.offsetMax = Vector2.zero;
            var plusTMP = plusTextGO.AddComponent<TextMeshProUGUI>();
            plusTMP.text = "+"; plusTMP.fontSize = 14f;
            plusTMP.color = UITheme.GoldAccent; plusTMP.alignment = TextAlignmentOptions.Center;

            // Modifier label (e.g. "+2")
            var modGO = MakeGO("Mod", rowGO.transform);
            modGO.AddComponent<LayoutElement>().preferredWidth = 32f;
            var modTMP = modGO.AddComponent<TextMeshProUGUI>();
            modTMP.text = "−1"; modTMP.fontSize = 11f;
            modTMP.color = UITheme.PlaceholderText; modTMP.alignment = TextAlignmentOptions.MidlineLeft;
            abilityModLabels[ai] = modTMP;

            // Wire buttons to popup (late-bind via popup reference below)
            int abilityIdx = captureAi;
            // Buttons are wired after popup serialization below via a helper approach
            // Store refs so we can wire them when we have the popup reference
        }

        // ── Panel 8: Confirm ─────────────────────────────────────────────
        stepPanels[8] = makeStepPanel("ConfirmPanel");
        var confirmRowGO = MakeGO("PortraitRow", stepPanels[8].transform);
        confirmRowGO.AddComponent<LayoutElement>().preferredHeight = 120f;
        confirmRowGO.AddComponent<Image>().color = Color.clear;
        var cHLG = confirmRowGO.AddComponent<HorizontalLayoutGroup>();
        cHLG.spacing = 16f;
        cHLG.padding = new RectOffset(0, 0, 0, 0);
        cHLG.childForceExpandHeight = false;  // let fixed-height portrait keep its 110px
        cHLG.childForceExpandWidth  = false;
        cHLG.childControlHeight = true;
        cHLG.childControlWidth  = true;

        // Portrait image — fixed 110×110 square, no AspectRatioFitter (was causing oversized rendering)
        var portraitGO = MakeGO("PortraitImage", confirmRowGO.transform);
        var portraitLE = portraitGO.AddComponent<LayoutElement>();
        portraitLE.preferredWidth  = 110f;
        portraitLE.preferredHeight = 110f;
        portraitLE.minWidth        = 110f;
        portraitLE.minHeight       = 110f;
        portraitLE.flexibleWidth   = 0f;
        portraitLE.flexibleHeight  = 0f;
        portraitGO.AddComponent<Image>().color = UITheme.BackgroundMid;
        var portraitRawGO = MakeGO("RawImage", portraitGO.transform);
        var portraitRawRT = portraitRawGO.GetComponent<RectTransform>();
        portraitRawRT.anchorMin = Vector2.zero; portraitRawRT.anchorMax = Vector2.one;
        portraitRawRT.offsetMin = Vector2.zero; portraitRawRT.offsetMax = Vector2.zero;
        var portraitRaw = portraitRawGO.AddComponent<RawImage>();
        portraitRaw.color = Color.clear;

        // Stats text
        var statsGO = MakeGO("StatsText", confirmRowGO.transform);
        statsGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var statsTMP = statsGO.AddComponent<TextMeshProUGUI>();
        statsTMP.color    = UITheme.DmText;
        statsTMP.fontSize = 14f;
        statsTMP.alignment = TextAlignmentOptions.TopLeft;

        // Begin button
        var beginGO = MakeGO("BeginButton", stepPanels[8].transform);
        var beginLE = beginGO.AddComponent<LayoutElement>();
        beginLE.minHeight = 44f; beginLE.preferredHeight = 44f; beginLE.flexibleHeight = 0f;
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

        // ── Nav row (fixed height, no expansion) ─────────────────────────
        var navRowGO = MakeGO("NavRow", panelGO.transform);
        var navRowLE = navRowGO.AddComponent<LayoutElement>();
        navRowLE.minHeight = 48f; navRowLE.preferredHeight = 48f; navRowLE.flexibleHeight = 0f;
        navRowGO.AddComponent<Image>().color = UITheme.BackgroundMid;
        var navHLG = navRowGO.AddComponent<HorizontalLayoutGroup>();
        navHLG.padding  = new RectOffset(12, 12, 8, 8);
        navHLG.spacing  = 8f;
        navHLG.childForceExpandHeight = true;
        navHLG.childForceExpandWidth  = false;
        navHLG.childControlHeight = true;
        navHLG.childControlWidth  = true;

        var cancelBtn = MakeNavButton("CancelButton", "Cancel", navRowGO.transform, UITheme.BackgroundDeep, UITheme.SystemText, 80f);
        var backBtn   = MakeNavButton("BackButton",   "< Back", navRowGO.transform, UITheme.BackgroundDeep, UITheme.DmText, 80f);
        var navSpacer = MakeGO("Spacer", navRowGO.transform);
        navSpacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var nextBtn   = MakeNavButton("NextButton",   "Next >", navRowGO.transform, UITheme.GoldAccent, UITheme.BackgroundDeep, 80f);
        var nextBtnLabel = nextBtn.GetComponentInChildren<TextMeshProUGUI>();

        // ── Wire CharacterCreationPopup fields ───────────────────────────
        var so = new SerializedObject(popup);
        SetArrayProp(so, "stepPanels", stepPanels);
        SetProp(so, "modeQuickButton",        modeQuickBtn);
        SetProp(so, "modeStepButton",         modeStepBtn);
        SetProp(so, "quickDescriptionInput",  quickDescInput);
        SetProp(so, "quickStatusText",        quickStatusTMP);
        SetProp(so, "nameInput",              nameInput);
        SetProp(so, "raceGridContainer",      raceGrid.GetComponent<RectTransform>());
        SetProp(so, "classGridContainer",     classGrid.GetComponent<RectTransform>());
        SetProp(so, "appearanceInput",        appearanceInput);
        SetProp(so, "backstoryInput",         backstoryInput);
        SetProp(so, "abilityPointsRemaining", abilityPtsTMP);
        SetProp(so, "abilityStatusText",      abilityStatusTMP);
        SetProp(so, "abilitySuggestButton",   suggestBtn);
        SetArrayProp(so, "abilityValueLabels", abilityValueLabels);
        SetArrayProp(so, "abilityModLabels",   abilityModLabels);
        SetProp(so, "portraitImage",          portraitRaw);
        SetProp(so, "statsText",              statsTMP);
        SetProp(so, "beginButton",            beginBtn);
        SetProp(so, "nextButton",             nextBtn);
        SetProp(so, "backButton",             backBtn);
        SetProp(so, "cancelButton",           cancelBtn);
        SetProp(so, "nextButtonLabel",        nextBtnLabel);
        SetProp(so, "stepIndicatorRow",       barRowGO);
        SetArrayProp(so, "stepBars",          stepBars);
        SetProp(so, "stepLabel",              stepLabelTMP);
        so.ApplyModifiedProperties();

        // Wire +/- buttons to popup after serialization (requires popup MonoBehaviour instance)
        var popupComp = popup as DnD.UI.CharacterCreationPopup;
        if (popupComp != null)
        {
            var allMinusBtns = stepPanels[7].GetComponentsInChildren<Button>();
            // The buttons in each row are: minus at index 0, plus at index 1
            // Each row has 2 buttons, 6 rows = 12 total
            int rowButtons = 0;
            foreach (var btn in allMinusBtns)
            {
                if (btn.name == "MinusBtn" || btn.name == "PlusBtn")
                {
                    int abilityIdx = rowButtons / 2;
                    bool isPlus    = btn.name == "PlusBtn";
                    int delta = isPlus ? 1 : -1;
                    if (abilityIdx < 6)
                        btn.onClick.AddListener(() => popupComp.AdjustAbilityScore(abilityIdx, delta));
                    rowButtons++;
                }
            }
        }

        // Wire to GameManager if present
        var gmGO = GameObject.Find("GameSystem");
        if (gmGO != null)
        {
            var gm = gmGO.GetComponent<DnD.Managers.GameManager>();
            if (gm != null)
            {
                var gmSO = new SerializedObject(gm);
                gmSO.FindProperty("characterPopup").objectReferenceValue = popup;
                gmSO.ApplyModifiedProperties();
            }
        }

        // Deactivate all step panels except 0
        for (int i = 1; i < stepPanels.Length; i++)
            stepPanels[i].SetActive(false);

        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[UISceneBuilder] Character popup built. Press Ctrl+S to save.");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static void SetProp(SerializedObject so, string propName, UnityEngine.Object value)
    {
        var prop = so.FindProperty(propName);
        if (prop == null) { Debug.LogError($"[UISceneBuilder] Property '{propName}' not found on {so.targetObject.GetType().Name}."); return; }
        prop.objectReferenceValue = value;
    }

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
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        return tmp;
    }

    private static void SetArrayProp<T>(SerializedObject so, string propName, T[] values)
        where T : UnityEngine.Object
    {
        var prop = so.FindProperty(propName);
        if (prop == null) { Debug.LogError($"[UISceneBuilder] SerializedProperty '{propName}' not found — check field name."); return; }
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static GameObject MakeGO(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static GameObject MakePanel(string name, Transform parent, Color32 color)
    {
        var go = MakeGO(name, parent);
        go.AddComponent<Image>().color = color;
        return go;
    }

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
            field.lineType = TMP_InputField.LineType.MultiLineNewline;

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
        phTMP.textWrappingMode = multiline ? TMPro.TextWrappingModes.Normal : TMPro.TextWrappingModes.NoWrap;
        field.placeholder = phTMP;

        var txtGO = MakeGO("Text", textAreaGO.transform);
        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
        var txtTMP = txtGO.AddComponent<TextMeshProUGUI>();
        txtTMP.color    = UITheme.InputText;
        txtTMP.fontSize = UITheme.FontInput;
        txtTMP.textWrappingMode = multiline ? TMPro.TextWrappingModes.Normal : TMPro.TextWrappingModes.NoWrap;
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

    /// <summary>GameObject overload for stepIndicatorRow wiring.</summary>
    private static void SetProp(SerializedObject so, string propName, GameObject value)
    {
        var prop = so.FindProperty(propName);
        if (prop == null) { Debug.LogError($"[UISceneBuilder] Property '{propName}' not found on {so.targetObject.GetType().Name}."); return; }
        prop.objectReferenceValue = value;
    }

    /// <summary>Large two-part button used in the mode-select panel.</summary>
    private static Button MakeModeButton(string goName, Transform parent,
        string title, string subtitle, Color32 bgColor, Color32 textColor)
    {
        var go = MakeGO(goName, parent);
        go.AddComponent<Image>().color = bgColor;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        var colors = btn.colors;
        colors.highlightedColor = new Color32(
            (byte)Mathf.Clamp(bgColor.r + 20, 0, 255),
            (byte)Mathf.Clamp(bgColor.g + 20, 0, 255),
            (byte)Mathf.Clamp(bgColor.b + 20, 0, 255), 0xFF);
        btn.colors = colors;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 16, 16);
        vlg.spacing = 8f;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;

        var titleGO = MakeGO("Title", go.transform);
        titleGO.AddComponent<LayoutElement>().preferredHeight = 22f;
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = title; titleTMP.fontSize = 14f; titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.color = textColor; titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.characterSpacing = 1f;

        var subGO = MakeGO("Subtitle", go.transform);
        subGO.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var subTMP = subGO.AddComponent<TextMeshProUGUI>();
        subTMP.text = subtitle; subTMP.fontSize = 10f;
        subTMP.color = new Color(textColor.r / 255f, textColor.g / 255f, textColor.b / 255f, 0.75f);
        subTMP.alignment = TextAlignmentOptions.Center;
        subTMP.textWrappingMode = TMPro.TextWrappingModes.Normal;
        return btn;
    }

    [MenuItem("DnD/Build Adventure Prompt Popup")]
    public static void BuildAdventurePromptPopup()
    {
        var scene = EditorSceneManager.GetActiveScene();

        foreach (var p in Object.FindObjectsByType<DnD.UI.AdventurePromptPopup>(FindObjectsInactive.Exclude))
            Undo.DestroyObjectImmediate(p.gameObject);

        // Canvas (sortingOrder=17 — above EditMap=16, below TitleScreen=20)
        var canvasGO = new GameObject("AdventurePromptCanvas");
        Undo.RegisterCreatedObjectUndo(canvasGO, "Build AdventurePromptPopup");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 17;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var popup = canvasGO.AddComponent<DnD.UI.AdventurePromptPopup>();

        // Dark overlay (also catches background clicks so they don't leak through)
        var overlayGO = MakeGO("Overlay", canvasGO.transform);
        var overlayRT = overlayGO.GetComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero; overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero; overlayRT.offsetMax = Vector2.zero;
        overlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        // Center panel (520 × 340)
        var panelGO = MakeGO("Panel", overlayGO.transform);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(520, 340);
        panelRT.anchoredPosition = Vector2.zero;
        panelGO.AddComponent<Image>().color = UITheme.BackgroundDeep;

        var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(20, 20, 20, 20);
        vlg.spacing                = 10;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;

        // Title
        MenuMakeLabel(panelGO.transform, "DESCRIBE YOUR ADVENTURE", 18f, UITheme.GoldAccent,
            TextAlignmentOptions.Center, 30);
        MenuMakeDivider(panelGO.transform, UITheme.GoldAccent, 2);

        // Subtitle / instruction
        MenuMakeLabel(panelGO.transform,
            "What kind of world do you want to explore?", 12f, UITheme.SystemText,
            TextAlignmentOptions.Center, 20);

        // Multiline input
        var input = MakeInputField(panelGO.transform,
            "A haunted forest where a forgotten god stirs...", true);
        input.GetComponent<LayoutElement>().minHeight = 100;

        // Size selector row (Small / Medium / Large) + readout
        var sizeRowGO = MakeGO("SizeRow", panelGO.transform);
        sizeRowGO.AddComponent<LayoutElement>().preferredHeight = 34;
        var sizeHLG = sizeRowGO.AddComponent<HorizontalLayoutGroup>();
        sizeHLG.spacing                = 8;
        sizeHLG.childForceExpandWidth  = true;
        sizeHLG.childForceExpandHeight = true;
        sizeHLG.childControlWidth      = true;
        sizeHLG.childControlHeight     = true;

        var smallBtn  = MenuMakeButton(sizeRowGO.transform, "SMALL · 5×5",  UITheme.SystemText, 34);
        var mediumBtn = MenuMakeButton(sizeRowGO.transform, "MEDIUM · 7×7", UITheme.SystemText, 34);
        var largeBtn  = MenuMakeButton(sizeRowGO.transform, "LARGE · 9×9",  UITheme.SystemText, 34);

        var sizeLabelGO = MakeGO("SizeLabel", panelGO.transform);
        sizeLabelGO.AddComponent<LayoutElement>().preferredHeight = 16;
        var sizeLabel = sizeLabelGO.AddComponent<TextMeshProUGUI>();
        sizeLabel.text      = "Medium (7×7)";
        sizeLabel.fontSize  = 11f;
        sizeLabel.color     = UITheme.SystemText;
        sizeLabel.alignment = TextAlignmentOptions.Center;

        // Button row
        var rowGO = MakeGO("ButtonRow", panelGO.transform);
        rowGO.AddComponent<LayoutElement>().preferredHeight = 40;
        var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
        rowHLG.spacing                = 12;
        rowHLG.childForceExpandWidth  = true;
        rowHLG.childForceExpandHeight = true;
        rowHLG.childControlWidth      = true;
        rowHLG.childControlHeight     = true;

        var cancelBtn = MenuMakeButton(rowGO.transform, "CANCEL",         UITheme.SystemText, 40);
        var beginBtn  = MenuMakeButton(rowGO.transform, "BEGIN ADVENTURE", UITheme.GoldAccent,  40);

        // Wire serialized fields
        var so = new SerializedObject(popup);
        SetProp(so, "promptInput",   input);
        SetProp(so, "beginButton",   beginBtn);
        SetProp(so, "cancelButton",  cancelBtn);
        SetProp(so, "smallButton",   smallBtn);
        SetProp(so, "mediumButton",  mediumBtn);
        SetProp(so, "largeButton",   largeBtn);
        SetProp(so, "sizeLabel",     sizeLabel);
        so.ApplyModifiedProperties();

        // Resize the popup panel to fit the new size row
        panelRT.sizeDelta = new Vector2(520, 400);

        // Wire onto GameManager
        var gm = Object.FindAnyObjectByType<DnD.Managers.GameManager>();
        if (gm != null)
        {
            var gmSO = new SerializedObject(gm);
            var prop = gmSO.FindProperty("adventurePromptPopup");
            if (prop != null) { prop.objectReferenceValue = popup; gmSO.ApplyModifiedProperties(); }
        }

        canvasGO.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[UISceneBuilder] AdventurePromptPopup built. Press Ctrl+S to save.");
    }

    [MenuItem("DnD/Build Edit Map Panel")]
    public static void BuildEditMapPanel()
    {
        var scene = EditorSceneManager.GetActiveScene();

        foreach (var p in Object.FindObjectsByType<DnD.UI.EditMapPanel>(FindObjectsInactive.Exclude))
            Undo.DestroyObjectImmediate(p.gameObject);

        // Canvas (sortingOrder=16 — above InGameMenu=15)
        var canvasGO = new GameObject("EditMapCanvas");
        Undo.RegisterCreatedObjectUndo(canvasGO, "Build EditMapPanel");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 16;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var editPanel = canvasGO.AddComponent<DnD.UI.EditMapPanel>();

        // Dark overlay
        var overlayGO = MakeGO("Overlay", canvasGO.transform);
        var overlayRT = overlayGO.GetComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero; overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero; overlayRT.offsetMax = Vector2.zero;
        overlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);

        // Center panel (700 × 520)
        var panelGO = MakeGO("Panel", overlayGO.transform);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(720f, 540f);
        panelRT.anchoredPosition = Vector2.zero;
        panelGO.AddComponent<Image>().color = UITheme.BackgroundMid;
        var panelVLG = panelGO.AddComponent<VerticalLayoutGroup>();
        panelVLG.padding = new RectOffset(0, 0, 0, 0);
        panelVLG.spacing = 0f;
        panelVLG.childForceExpandWidth = true; panelVLG.childForceExpandHeight = false;
        panelVLG.childControlWidth = true; panelVLG.childControlHeight = true;

        // Header bar
        var headerGO = MakeGO("Header", panelGO.transform);
        headerGO.AddComponent<LayoutElement>().preferredHeight = 40f;
        headerGO.AddComponent<Image>().color = new Color32(0x12, 0x0C, 0x03, 0xFF);
        var hRow = headerGO.AddComponent<HorizontalLayoutGroup>();
        hRow.padding = new RectOffset(16, 8, 0, 0);
        hRow.childForceExpandHeight = true; hRow.childForceExpandWidth = false;
        hRow.childControlHeight = true; hRow.childControlWidth = true;
        var hTitleGO = MakeGO("Title", headerGO.transform);
        hTitleGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var hTMP = hTitleGO.AddComponent<TextMeshProUGUI>();
        hTMP.text = "EDIT MAP"; hTMP.fontSize = 16f;
        hTMP.color = UITheme.GoldAccent; hTMP.alignment = TextAlignmentOptions.MidlineLeft;
        hTMP.characterSpacing = 2f;
        var closeHdrBtn = MenuMakeButton(headerGO.transform, "×  Close", UITheme.SystemText, 28);
        closeHdrBtn.GetComponent<LayoutElement>().preferredWidth = 80f;
        closeHdrBtn.onClick.AddListener(() => editPanel.Close());

        // Body: horizontal split (grid | editor)
        var bodyGO = MakeGO("Body", panelGO.transform);
        bodyGO.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var bodyHLG = bodyGO.AddComponent<HorizontalLayoutGroup>();
        bodyHLG.padding = new RectOffset(8, 8, 8, 8);
        bodyHLG.spacing = 8f;
        bodyHLG.childForceExpandHeight = true; bodyHLG.childForceExpandWidth = false;
        bodyHLG.childControlHeight = true; bodyHLG.childControlWidth = true;

        // ── Left: tile grid scroll view ──────────────────────────────────────
        var gridScrollGO = MakeGO("GridScroll", bodyGO.transform);
        gridScrollGO.AddComponent<LayoutElement>().flexibleWidth = 1.1f;
        gridScrollGO.AddComponent<Image>().color = new Color32(0x18, 0x12, 0x08, 0xFF);
        var gridScroll = gridScrollGO.AddComponent<ScrollRect>();
        gridScroll.horizontal = false; gridScroll.scrollSensitivity = 20;

        var gridViewportGO = MakeGO("Viewport", gridScrollGO.transform);
        var gridViewportRT = gridViewportGO.GetComponent<RectTransform>();
        gridViewportRT.anchorMin = Vector2.zero; gridViewportRT.anchorMax = Vector2.one;
        gridViewportRT.offsetMin = Vector2.zero; gridViewportRT.offsetMax = Vector2.zero;
        gridViewportGO.AddComponent<RectMask2D>();
        gridScroll.viewport = gridViewportRT;

        var gridContentGO = MakeGO("GridContent", gridViewportGO.transform);
        var gridContentRT = gridContentGO.GetComponent<RectTransform>();
        gridContentRT.anchorMin = new Vector2(0, 1); gridContentRT.anchorMax = new Vector2(1, 1);
        gridContentRT.pivot     = new Vector2(0.5f, 1f);
        gridContentRT.offsetMin = Vector2.zero; gridContentRT.offsetMax = Vector2.zero;
        var gridLayout = gridContentGO.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize      = new Vector2(60f, 60f);
        gridLayout.spacing       = new Vector2(4f, 4f);
        gridLayout.padding       = new RectOffset(8, 8, 8, 8);
        gridLayout.constraint    = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 7; // matches default map width
        var gridCSF = gridContentGO.AddComponent<ContentSizeFitter>();
        gridCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        gridScroll.content = gridContentRT;

        // ── Right: tile editor panel ─────────────────────────────────────────
        var editorGO = MakeGO("TileEditor", bodyGO.transform);
        var editorLE = editorGO.AddComponent<LayoutElement>();
        editorLE.preferredWidth = 240f; editorLE.flexibleWidth = 0f;
        editorGO.AddComponent<Image>().color = new Color32(0x1C, 0x14, 0x08, 0xFF);
        var editorVLG = editorGO.AddComponent<VerticalLayoutGroup>();
        editorVLG.padding = new RectOffset(10, 10, 10, 10);
        editorVLG.spacing = 8f;
        editorVLG.childForceExpandWidth = true; editorVLG.childForceExpandHeight = false;
        editorVLG.childControlWidth = true; editorVLG.childControlHeight = true;

        // Selected tile label
        var selLabelGO = MakeGO("SelectedLabel", editorGO.transform);
        selLabelGO.AddComponent<LayoutElement>().preferredHeight = 22f;
        var selLabelTMP = selLabelGO.AddComponent<TextMeshProUGUI>();
        selLabelTMP.text = "Select a tile to edit it";
        selLabelTMP.fontSize = 11f; selLabelTMP.color = UITheme.GoldAccent;
        selLabelTMP.alignment = TextAlignmentOptions.TopLeft;

        // Tile preview thumbnail
        var previewGO = MakeGO("TilePreview", editorGO.transform);
        var previewLE = previewGO.AddComponent<LayoutElement>();
        previewLE.preferredHeight = 80f; previewLE.preferredWidth = 80f;
        previewLE.flexibleWidth = 0f;
        previewGO.AddComponent<Image>().color = new Color32(0x28, 0x1E, 0x10, 0xFF);
        var previewRawGO = MakeGO("RawImage", previewGO.transform);
        var previewRawRT = previewRawGO.GetComponent<RectTransform>();
        previewRawRT.anchorMin = Vector2.zero; previewRawRT.anchorMax = Vector2.one;
        previewRawRT.offsetMin = new Vector2(4,4); previewRawRT.offsetMax = new Vector2(-4,-4);
        var previewRaw = previewRawGO.AddComponent<RawImage>();
        previewRaw.color = new Color32(0x4A, 0x38, 0x20, 0xFF);

        // TileType label
        var typeRowGO = MakeGO("TypeRow", editorGO.transform);
        typeRowGO.AddComponent<LayoutElement>().preferredHeight = 20f;
        var typeLabelTMP = typeRowGO.AddComponent<TextMeshProUGUI>();
        typeLabelTMP.text = "TILE TYPE"; typeLabelTMP.fontSize = 9f;
        typeLabelTMP.color = UITheme.SystemText; typeLabelTMP.fontStyle = FontStyles.Bold;
        typeLabelTMP.alignment = TextAlignmentOptions.MidlineLeft;

        // TileType dropdown
        var dropGO = MakeGO("TileTypeDropdown", editorGO.transform);
        dropGO.AddComponent<LayoutElement>().preferredHeight = 28f;
        dropGO.AddComponent<Image>().color = new Color32(0x38, 0x28, 0x10, 0xFF);
        var drop = dropGO.AddComponent<TMP_Dropdown>();
        drop.options.Clear();
        foreach (TileType tt in System.Enum.GetValues(typeof(TileType)))
            drop.options.Add(new TMP_Dropdown.OptionData(tt.ToString()));
        // Template child required by TMP_Dropdown
        var dropTemplate = MakeGO("Template", dropGO.transform);
        var dropTemplateRT = dropTemplate.GetComponent<RectTransform>();
        dropTemplateRT.anchorMin = new Vector2(0,0); dropTemplateRT.anchorMax = new Vector2(1,0);
        dropTemplateRT.pivot     = new Vector2(0.5f,1f);
        dropTemplateRT.sizeDelta = new Vector2(0, 150f);
        dropTemplate.AddComponent<Image>().color = new Color32(0x28, 0x1E, 0x10, 0xFF);
        var dropScrollRect = dropTemplate.AddComponent<ScrollRect>();
        var dropViewport = MakeGO("Viewport", dropTemplate.transform);
        dropViewport.AddComponent<RectMask2D>();
        var dropVpRT = dropViewport.GetComponent<RectTransform>();
        dropVpRT.anchorMin = Vector2.zero; dropVpRT.anchorMax = Vector2.one;
        dropVpRT.offsetMin = new Vector2(0,0); dropVpRT.offsetMax = new Vector2(0,-28);
        var dropContent = MakeGO("Content", dropViewport.transform);
        var dropContentRT = dropContent.GetComponent<RectTransform>();
        dropContentRT.anchorMin = new Vector2(0,1); dropContentRT.anchorMax = new Vector2(1,1);
        dropContentRT.pivot     = new Vector2(0.5f,1f);
        dropContentRT.sizeDelta = new Vector2(0, 28f);
        dropScrollRect.viewport = dropVpRT;
        dropScrollRect.content  = dropContentRT;
        var dropItem = MakeGO("Item", dropContent.transform);
        var dropItemBtn = dropItem.AddComponent<Button>();
        dropItem.AddComponent<Image>().color = Color.clear;
        var dropItemToggle = dropItem.AddComponent<Toggle>();
        var dropItemLabel = MakeGO("Item Label", dropItem.transform);
        var dropItemLabelRT = dropItemLabel.GetComponent<RectTransform>();
        dropItemLabelRT.anchorMin = Vector2.zero; dropItemLabelRT.anchorMax = Vector2.one;
        dropItemLabelRT.offsetMin = new Vector2(10,0); dropItemLabelRT.offsetMax = new Vector2(-10,0);
        var dropItemTMP = dropItemLabel.AddComponent<TextMeshProUGUI>();
        dropItemTMP.color = UITheme.DmText; dropItemTMP.fontSize = 11f;
        drop.itemText = dropItemTMP;
        drop.template = dropTemplateRT;
        dropTemplate.SetActive(false);
        // Caption text
        var captionGO = MakeGO("Label", dropGO.transform);
        var captionRT = captionGO.GetComponent<RectTransform>();
        captionRT.anchorMin = new Vector2(0,0); captionRT.anchorMax = new Vector2(1,1);
        captionRT.offsetMin = new Vector2(8,0); captionRT.offsetMax = new Vector2(-24,0);
        var captionTMP = captionGO.AddComponent<TextMeshProUGUI>();
        captionTMP.color = UITheme.DmText; captionTMP.fontSize = 11f;
        captionTMP.alignment = TextAlignmentOptions.MidlineLeft;
        drop.captionText = captionTMP;
        drop.value = 0; drop.RefreshShownValue();

        // Description label
        var descLabelGO = MakeGO("DescLabel", editorGO.transform);
        descLabelGO.AddComponent<LayoutElement>().preferredHeight = 18f;
        var descLabelTMP = descLabelGO.AddComponent<TextMeshProUGUI>();
        descLabelTMP.text = "DESCRIPTION"; descLabelTMP.fontSize = 9f;
        descLabelTMP.color = UITheme.SystemText; descLabelTMP.fontStyle = FontStyles.Bold;
        descLabelTMP.alignment = TextAlignmentOptions.MidlineLeft;

        // Description input
        var descInput = MakeInputField(editorGO.transform, "Write a custom tile description...", true);
        var descInputLE = descInput.GetComponent<LayoutElement>();
        if (descInputLE == null) descInputLE = descInput.gameObject.AddComponent<LayoutElement>();
        descInputLE.preferredHeight = 80f; descInputLE.flexibleHeight = 0f;

        // Apply Changes button
        var applyBtn = MenuMakeButton(editorGO.transform, "APPLY CHANGES", UITheme.SystemText, 30);
        applyBtn.onClick.AddListener(() => editPanel.ApplyCurrentTileEdits());

        // Regenerate Artwork button
        var regenBtn = MenuMakeButton(editorGO.transform, "REGENERATE ARTWORK", UITheme.GoldAccent, 30);
        regenBtn.onClick.AddListener(() => editPanel.RegenerateSelectedTile());

        // Divider + Save button
        MenuMakeDivider(editorGO.transform, UITheme.SystemText, 1);
        var saveEditorBtn = MenuMakeButton(editorGO.transform, "SAVE GAME", UITheme.GoldAccent, 30);

        // Wire EditMapPanel serialized fields
        var editSO = new SerializedObject(editPanel);
        SetProp(editSO, "tileGridContainer", gridContentGO.GetComponent<RectTransform>());
        SetProp(editSO, "selectedTileLabel", selLabelTMP);
        SetProp(editSO, "tileTypeDropdown",  drop);
        SetProp(editSO, "descriptionInput",  descInput);
        SetProp(editSO, "selectedTilePreview", previewRaw);
        SetProp(editSO, "regenerateButton",  regenBtn);
        SetProp(editSO, "applyButton",       applyBtn);
        SetProp(editSO, "saveButton",        saveEditorBtn);
        SetProp(editSO, "closeButton",       closeHdrBtn);
        editSO.ApplyModifiedProperties();

        // Wire Save button callback after panel has fields
        saveEditorBtn.onClick.AddListener(() => editPanel.OnSaveRequested?.Invoke());

        // Wire to GameManager
        var gm = Object.FindAnyObjectByType<DnD.Managers.GameManager>();
        if (gm != null)
        {
            var gmSO = new SerializedObject(gm);
            var prop = gmSO.FindProperty("editMapPanel");
            if (prop != null) { prop.objectReferenceValue = editPanel; gmSO.ApplyModifiedProperties(); }
        }

        canvasGO.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[UISceneBuilder] EditMapPanel built. Press Ctrl+S to save.");
    }

    [MenuItem("DnD/Build Character Screen")]
    public static void BuildCharacterScreen()
    {
        var scene = EditorSceneManager.GetActiveScene();

        foreach (var p in Object.FindObjectsByType<DnD.UI.CharacterScreenPanel>(FindObjectsInactive.Exclude))
            Undo.DestroyObjectImmediate(p.gameObject);

        // Canvas (sortingOrder=12)
        var canvasGO = new GameObject("CharacterScreenCanvas");
        Undo.RegisterCreatedObjectUndo(canvasGO, "Build CharacterScreen");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 12;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var charPanel = canvasGO.AddComponent<DnD.UI.CharacterScreenPanel>();

        // Dark overlay
        var overlayGO = MakeGO("Overlay", canvasGO.transform);
        var overlayRT = overlayGO.GetComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero; overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero; overlayRT.offsetMax = Vector2.zero;
        overlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);

        // Center panel (560 × 520)
        var panelGO = MakeGO("Panel", overlayGO.transform);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(560f, 540f);
        panelRT.anchoredPosition = Vector2.zero;
        panelGO.AddComponent<Image>().color = UITheme.BackgroundMid;
        var panelVLG = panelGO.AddComponent<VerticalLayoutGroup>();
        panelVLG.padding = new RectOffset(0, 0, 0, 0);
        panelVLG.spacing = 0f;
        panelVLG.childForceExpandWidth = true; panelVLG.childForceExpandHeight = false;
        panelVLG.childControlWidth = true; panelVLG.childControlHeight = true;

        // Header
        var headerGO = MakeGO("Header", panelGO.transform);
        headerGO.AddComponent<LayoutElement>().preferredHeight = 40f;
        headerGO.AddComponent<Image>().color = new Color32(0x12, 0x0C, 0x03, 0xFF);
        var hRow = headerGO.AddComponent<HorizontalLayoutGroup>();
        hRow.padding = new RectOffset(16, 8, 0, 0);
        hRow.childForceExpandHeight = true; hRow.childForceExpandWidth = false;
        hRow.childControlHeight = true; hRow.childControlWidth = true;
        var hTitleGO = MakeGO("Title", headerGO.transform);
        hTitleGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var hTMP = hTitleGO.AddComponent<TextMeshProUGUI>();
        hTMP.text = "CHARACTER SHEET"; hTMP.fontSize = 16f;
        hTMP.color = UITheme.GoldAccent; hTMP.alignment = TextAlignmentOptions.MidlineLeft;
        hTMP.characterSpacing = 2f;
        var closeHdrBtn = MenuMakeButton(headerGO.transform, "×  Close", UITheme.SystemText, 28);
        closeHdrBtn.GetComponent<LayoutElement>().preferredWidth = 80f;
        closeHdrBtn.onClick.AddListener(() => charPanel.Close());

        // Body
        var bodyGO = MakeGO("Body", panelGO.transform);
        bodyGO.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var bodyVLG = bodyGO.AddComponent<VerticalLayoutGroup>();
        bodyVLG.padding = new RectOffset(16, 16, 12, 12);
        bodyVLG.spacing = 10f;
        bodyVLG.childForceExpandWidth = true; bodyVLG.childForceExpandHeight = false;
        bodyVLG.childControlWidth = true; bodyVLG.childControlHeight = true;

        // ── Portrait + identity row ──────────────────────────────────────────
        var idRowGO = MakeGO("IdentityRow", bodyGO.transform);
        idRowGO.AddComponent<LayoutElement>().preferredHeight = 130f;
        idRowGO.AddComponent<Image>().color = Color.clear;
        var idHLG = idRowGO.AddComponent<HorizontalLayoutGroup>();
        idHLG.spacing = 16f;
        idHLG.childForceExpandHeight = true; idHLG.childForceExpandWidth = false;
        idHLG.childControlHeight = true; idHLG.childControlWidth = true;

        // Portrait (fixed 120×120)
        var portraitGO = MakeGO("Portrait", idRowGO.transform);
        var portraitLE = portraitGO.AddComponent<LayoutElement>();
        portraitLE.preferredWidth = 120f; portraitLE.minWidth = 120f; portraitLE.flexibleWidth = 0f;
        portraitGO.AddComponent<Image>().color = new Color32(0x28, 0x1E, 0x10, 0xFF);
        var portraitRawGO = MakeGO("RawImage", portraitGO.transform);
        var portraitRawRT = portraitRawGO.GetComponent<RectTransform>();
        portraitRawRT.anchorMin = Vector2.zero; portraitRawRT.anchorMax = Vector2.one;
        portraitRawRT.offsetMin = Vector2.zero; portraitRawRT.offsetMax = Vector2.zero;
        var portraitRaw = portraitRawGO.AddComponent<RawImage>();
        portraitRaw.color = new Color32(0x4A, 0x38, 0x20, 0xFF);

        // Identity text column
        var idColGO = MakeGO("IdentityCol", idRowGO.transform);
        idColGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var idColVLG = idColGO.AddComponent<VerticalLayoutGroup>();
        idColVLG.childForceExpandWidth = true; idColVLG.childForceExpandHeight = false;
        idColVLG.childControlWidth = true; idColVLG.childControlHeight = true;
        idColVLG.spacing = 4f;

        var nameTMP    = AddInfoText(idColGO.transform, "Name",      UITheme.GoldAccent,  20f);
        var raceClsTMP = AddInfoText(idColGO.transform, "RaceClass", UITheme.DmText,      13f);
        var levelTMP   = AddInfoText(idColGO.transform, "Level",     UITheme.SystemText,  12f);
        var hpTMP      = AddInfoText(idColGO.transform, "HP",        UITheme.SystemText,  12f);
        var acTMP      = AddInfoText(idColGO.transform, "AC",        UITheme.SystemText,  12f);

        // ── Ability scores row ───────────────────────────────────────────────
        MenuMakeDivider(bodyGO.transform, UITheme.GoldAccent, 1);
        var abRowGO = MakeGO("AbilityRow", bodyGO.transform);
        abRowGO.AddComponent<LayoutElement>().preferredHeight = 70f;
        abRowGO.AddComponent<Image>().color = new Color32(0x1C, 0x14, 0x08, 0xFF);
        var abHLG = abRowGO.AddComponent<HorizontalLayoutGroup>();
        abHLG.padding = new RectOffset(8, 8, 4, 4);
        abHLG.spacing = 0f;
        abHLG.childForceExpandHeight = true; abHLG.childForceExpandWidth = true;
        abHLG.childControlHeight = true; abHLG.childControlWidth = true;

        var abilityLabels = new TextMeshProUGUI[6];
        string[] abNames = { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
        for (int i = 0; i < 6; i++)
        {
            // Background cell
            var abGO = MakeGO(abNames[i], abRowGO.transform);
            abGO.AddComponent<Image>().color = Color.clear;

            // Text in a child GO so Image and TextMeshProUGUI don't share the same GameObject
            var abTextGO = MakeGO("Text", abGO.transform);
            var abTextRT = abTextGO.GetComponent<RectTransform>();
            abTextRT.anchorMin = Vector2.zero; abTextRT.anchorMax = Vector2.one;
            abTextRT.offsetMin = Vector2.zero; abTextRT.offsetMax = Vector2.zero;
            var abTMP = abTextGO.AddComponent<TextMeshProUGUI>();
            abTMP.text             = $"<b>{abNames[i]}</b>\n10\n<size=80%>(+0)</size>";
            abTMP.fontSize         = 12f;
            abTMP.color            = UITheme.DmText;
            abTMP.alignment        = TextAlignmentOptions.Center;
            abTMP.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            abilityLabels[i] = abTMP;
        }

        // ── Appearance + backstory (scrollable) ──────────────────────────────
        MenuMakeDivider(bodyGO.transform, UITheme.GoldAccent, 1);

        var loreScrollGO = MakeGO("LoreScroll", bodyGO.transform);
        loreScrollGO.AddComponent<LayoutElement>().flexibleHeight = 1f;
        loreScrollGO.AddComponent<Image>().color = Color.clear;
        var loreScroll = loreScrollGO.AddComponent<ScrollRect>();
        loreScroll.horizontal = false; loreScroll.scrollSensitivity = 20;

        var loreVpGO = MakeGO("Viewport", loreScrollGO.transform);
        var loreVpRT = loreVpGO.GetComponent<RectTransform>();
        loreVpRT.anchorMin = Vector2.zero; loreVpRT.anchorMax = Vector2.one;
        loreVpRT.offsetMin = Vector2.zero; loreVpRT.offsetMax = Vector2.zero;
        loreVpGO.AddComponent<RectMask2D>();
        loreScroll.viewport = loreVpRT;

        var loreContentGO = MakeGO("Content", loreVpGO.transform);
        var loreContentRT = loreContentGO.GetComponent<RectTransform>();
        loreContentRT.anchorMin = new Vector2(0,1); loreContentRT.anchorMax = new Vector2(1,1);
        loreContentRT.pivot     = new Vector2(0.5f,1f);
        loreContentRT.offsetMin = Vector2.zero; loreContentRT.offsetMax = Vector2.zero;
        var loreVLG = loreContentGO.AddComponent<VerticalLayoutGroup>();
        loreVLG.childForceExpandWidth = true; loreVLG.childForceExpandHeight = false;
        loreVLG.childControlWidth = true; loreVLG.childControlHeight = true;
        loreVLG.spacing = 6f;
        loreContentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        loreScroll.content = loreContentRT;

        var appearLabelGO = MakeGO("AppearanceLabel", loreContentGO.transform);
        var appearLabelLE = appearLabelGO.AddComponent<LayoutElement>();
        appearLabelLE.minHeight = 14f;
        var appearLabelTMP = appearLabelGO.AddComponent<TextMeshProUGUI>();
        appearLabelTMP.text = "APPEARANCE"; appearLabelTMP.fontSize = 9f;
        appearLabelTMP.color = UITheme.GoldAccent; appearLabelTMP.fontStyle = FontStyles.Bold;
        appearLabelTMP.alignment = TextAlignmentOptions.TopLeft;

        var appearTextGO = MakeGO("AppearanceText", loreContentGO.transform);
        var appearTextLE = appearTextGO.AddComponent<LayoutElement>();
        appearTextLE.minHeight = 20f;
        var appearTextTMP = appearTextGO.AddComponent<TextMeshProUGUI>();
        appearTextTMP.fontSize = 11f; appearTextTMP.color = UITheme.SystemText;
        appearTextTMP.alignment = TextAlignmentOptions.TopLeft;
        appearTextTMP.textWrappingMode = TMPro.TextWrappingModes.Normal;

        var backstoryLabelGO = MakeGO("BackstoryLabel", loreContentGO.transform);
        backstoryLabelGO.AddComponent<LayoutElement>().minHeight = 14f;
        var backstoryLabelTMP = backstoryLabelGO.AddComponent<TextMeshProUGUI>();
        backstoryLabelTMP.text = "BACKSTORY"; backstoryLabelTMP.fontSize = 9f;
        backstoryLabelTMP.color = UITheme.GoldAccent; backstoryLabelTMP.fontStyle = FontStyles.Bold;
        backstoryLabelTMP.alignment = TextAlignmentOptions.TopLeft;

        var backstoryTextGO = MakeGO("BackstoryText", loreContentGO.transform);
        backstoryTextGO.AddComponent<LayoutElement>().minHeight = 20f;
        var backstoryTextTMP = backstoryTextGO.AddComponent<TextMeshProUGUI>();
        backstoryTextTMP.fontSize = 11f; backstoryTextTMP.color = UITheme.DmText;
        backstoryTextTMP.alignment = TextAlignmentOptions.TopLeft;
        backstoryTextTMP.textWrappingMode = TMPro.TextWrappingModes.Normal;

        // Wire CharacterScreenPanel fields
        var charSO = new SerializedObject(charPanel);
        SetProp(charSO, "portraitImage",   portraitRaw);
        SetProp(charSO, "nameText",        nameTMP);
        SetProp(charSO, "raceClassText",   raceClsTMP);
        SetProp(charSO, "levelText",       levelTMP);
        SetProp(charSO, "hpText",          hpTMP);
        SetProp(charSO, "acText",          acTMP);
        SetArrayProp(charSO, "abilityLabels", abilityLabels);
        SetProp(charSO, "appearanceText",  appearTextTMP);
        SetProp(charSO, "backstoryText",   backstoryTextTMP);
        SetProp(charSO, "closeButton",     closeHdrBtn);
        charSO.ApplyModifiedProperties();

        // Wire to GameManager
        var gm = Object.FindAnyObjectByType<DnD.Managers.GameManager>();
        if (gm != null)
        {
            var gmSO = new SerializedObject(gm);
            var prop = gmSO.FindProperty("characterScreenPanel");
            if (prop != null) { prop.objectReferenceValue = charPanel; gmSO.ApplyModifiedProperties(); }
        }

        canvasGO.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[UISceneBuilder] CharacterScreen built. Press Ctrl+S to save.");
    }

    [MenuItem("DnD/Build In-Game Menu Panel")]
    public static void BuildInGameMenuPanel()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // Remove old panel
        foreach (var p in Object.FindObjectsByType<DnD.UI.InGameMenuPanel>(FindObjectsInactive.Exclude))
            Undo.DestroyObjectImmediate(p.gameObject);

        // ── Canvas (sortingOrder=15, above HUD but below TitleScreen=20) ─
        var canvasGO = new GameObject("InGameMenuCanvas");
        Undo.RegisterCreatedObjectUndo(canvasGO, "Build InGameMenu");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // Attach InGameMenuPanel component to canvas root (starts hidden)
        var menuPanel = canvasGO.AddComponent<DnD.UI.InGameMenuPanel>();

        // ── Dark overlay ──────────────────────────────────────────────────
        var overlayGO = MakeGO("Overlay", canvasGO.transform);
        var overlayRT = overlayGO.GetComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero; overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero; overlayRT.offsetMax = Vector2.zero;
        overlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        // ── Center panel (380 × 480) ──────────────────────────────────────
        var panelGO = MakeGO("Panel", overlayGO.transform);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(380, 480);
        panelRT.anchoredPosition = Vector2.zero;
        panelGO.AddComponent<Image>().color = UITheme.BackgroundMid;

        var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(16, 16, 16, 16);
        vlg.spacing                = 8;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;

        // Header
        MenuMakeLabel(panelGO.transform, "GAME MENU", 18f, UITheme.GoldAccent,
            TextAlignmentOptions.Center, 36);
        MenuMakeDivider(panelGO.transform, UITheme.GoldAccent, 2);

        // Save button
        var saveBtn = MenuMakeButton(panelGO.transform, "SAVE GAME", UITheme.GoldAccent);
        saveBtn.onClick.AddListener(() => menuPanel.OnSave?.Invoke());

        // Load / Main Menu button
        var loadBtn = MenuMakeButton(panelGO.transform, "LOAD / MAIN MENU", UITheme.SystemText);
        loadBtn.onClick.AddListener(() => menuPanel.OnLoad?.Invoke());

        MenuMakeDivider(panelGO.transform, UITheme.SystemText, 1);

        // Controls container — hosts the DM voice toggles built at runtime by
        // InGameMenuPanel.RebuildTTSRows. (The per-tile regenerate rows used to live
        // here too; tile regeneration is now reachable from the EditMapPanel only.)
        var listGO = MakeGO("ControlsContainer", panelGO.transform);
        var listVLG = listGO.AddComponent<VerticalLayoutGroup>();
        listVLG.spacing                = 2;
        listVLG.childForceExpandWidth  = true;
        listVLG.childForceExpandHeight = false;
        listVLG.childControlWidth      = true;
        listVLG.childControlHeight     = true;
        listGO.AddComponent<LayoutElement>().flexibleHeight = 1;

        MenuMakeDivider(panelGO.transform, UITheme.SystemText, 1);

        // Resume button
        var resumeBtn = MenuMakeButton(panelGO.transform, "RESUME ADVENTURE", UITheme.DmText);
        resumeBtn.onClick.AddListener(() => menuPanel.Close());

        // Wire button + container references onto the panel. The InGameMenuPanel re-binds
        // the click listeners in OnEnable each time the menu opens, since editor-time
        // AddListener calls are non-persistent and would otherwise be lost on scene save.
        var menuSO = new SerializedObject(menuPanel);
        menuSO.FindProperty("controlsContainer").objectReferenceValue = listGO;
        menuSO.FindProperty("saveButton").objectReferenceValue        = saveBtn;
        menuSO.FindProperty("loadButton").objectReferenceValue        = loadBtn;
        menuSO.FindProperty("resumeButton").objectReferenceValue      = resumeBtn;
        menuSO.ApplyModifiedProperties();

        // Wire inGameMenuPanel on GameManager
        var gm = Object.FindAnyObjectByType<DnD.Managers.GameManager>();
        if (gm != null)
        {
            var gmSO = new SerializedObject(gm);
            var prop = gmSO.FindProperty("inGameMenuPanel");
            if (prop != null) { prop.objectReferenceValue = menuPanel; gmSO.ApplyModifiedProperties(); }
        }

        canvasGO.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[UISceneBuilder] InGameMenuPanel built. Press Ctrl+S to save.");
    }

    private static Button MenuMakeButton(Transform parent, string label, Color textColor, float height = 36)
    {
        var go = MakeGO(label.Replace(" ", "") + "Btn", parent);
        go.AddComponent<LayoutElement>().preferredHeight = height;
        var img = go.AddComponent<Image>();
        img.color = new Color32(0x28, 0x1E, 0x10, 0xFF);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color32(0x3C, 0x2A, 0x14, 0xFF);
        btn.colors = colors;
        var txtGO = MakeGO("Text", go.transform);
        var rt = txtGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var tmp = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 12f;
        tmp.color     = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    private static void MenuMakeDivider(Transform parent, Color color, float height)
    {
        var go = MakeGO("Divider", parent);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth   = 1;
        go.AddComponent<Image>().color = color;
    }

    private static void MenuMakeLabel(Transform parent, string text, float fontSize, Color color,
        TextAlignmentOptions align, float height = 24)
    {
        var go = MakeGO("Label_" + text.Replace(" ",""), parent);
        go.AddComponent<LayoutElement>().preferredHeight = height;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.color     = color;
        tmp.alignment = align;
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
