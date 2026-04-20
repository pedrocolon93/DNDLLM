// Assets/Editor/UISceneBuilder.cs
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;
using DnD.UI;
using DnD.Managers;

public static class UISceneBuilder
{
    [MenuItem("DnD/Rebuild UI Canvas")]
    public static void RebuildCanvas()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // ── Remove old Canvas ─────────────────────────────────────────
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
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

        var mapCam = mapCamGO.AddComponent<Camera>();
        mapCam.orthographic = true;
        mapCam.orthographicSize = 5f;
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = new Color32(0x1E, 0x15, 0x08, 0xFF);
        mapCam.transform.position = new Vector3(4.5f, 10f, 4.5f);
        mapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        mapCamGO.AddComponent<MapCameraController>();

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

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[UISceneBuilder] Canvas rebuilt. Press Ctrl+S to save the scene.");
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

        EditorSceneManager.MarkSceneDirty(scene);
    }

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

        // Step label — Image (bg) on outer GO, TextMeshProUGUI on child GO
        var stepLabelGO = MakeGO("StepLabel", panelGO.transform);
        stepLabelGO.AddComponent<LayoutElement>().preferredHeight = 26f;
        stepLabelGO.AddComponent<Image>().color = UITheme.BackgroundMid;
        var stepLabelTextGO = MakeGO("Text", stepLabelGO.transform);
        var slRT = stepLabelTextGO.GetComponent<RectTransform>();
        slRT.anchorMin = Vector2.zero; slRT.anchorMax = Vector2.one;
        slRT.offsetMin = Vector2.zero; slRT.offsetMax = Vector2.zero;
        var stepLabelTMP = stepLabelTextGO.AddComponent<TextMeshProUGUI>();
        stepLabelTMP.text      = "Step 1 of 5 — NAME";
        stepLabelTMP.fontSize  = 11f;
        stepLabelTMP.color     = UITheme.SystemText;
        stepLabelTMP.alignment = TextAlignmentOptions.Center;

        // Content container (fills remaining space)
        var contentGO = MakeGO("ContentContainer", panelGO.transform);
        contentGO.AddComponent<LayoutElement>().flexibleHeight = 1f;
        contentGO.AddComponent<Image>().color = Color.clear;

        // Helper: make a step panel inside contentGO
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
        var confirmRowGO = MakeGO("PortraitRow", stepPanels[5].transform);
        confirmRowGO.AddComponent<LayoutElement>().flexibleHeight = 1f;
        confirmRowGO.AddComponent<Image>().color = Color.clear;
        var cHLG = confirmRowGO.AddComponent<HorizontalLayoutGroup>();
        cHLG.spacing = 12f;
        cHLG.childForceExpandHeight = true;
        cHLG.childForceExpandWidth  = false;
        cHLG.childControlHeight = true;
        cHLG.childControlWidth  = true;

        // Portrait image — Image (bg) on outer GO, RawImage on child GO
        var portraitGO = MakeGO("PortraitImage", confirmRowGO.transform);
        portraitGO.AddComponent<LayoutElement>().preferredWidth = 100f;
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

        // ── Nav row ───────────────────────────────────────────────────────
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
        var navSpacer = MakeGO("Spacer", navRowGO.transform);
        navSpacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var nextBtn   = MakeNavButton("NextButton",   "Next >", navRowGO.transform, UITheme.GoldAccent, UITheme.BackgroundDeep, 80f);

        // ── Wire CharacterCreationPopup fields ───────────────────────────
        var so = new SerializedObject(popup);
        SetArrayProp(so, "stepPanels", stepPanels);
        SetProp(so, "nameInput", nameInput);
        SetProp(so, "raceGridContainer", raceGrid.GetComponent<RectTransform>());
        SetProp(so, "classGridContainer", classGrid.GetComponent<RectTransform>());
        SetProp(so, "appearanceInput", appearanceInput);
        SetProp(so, "backstoryInput", backstoryInput);
        SetProp(so, "portraitImage", portraitRaw);
        SetProp(so, "statsText", statsTMP);
        SetProp(so, "beginButton", beginBtn);
        SetProp(so, "nextButton", nextBtn);
        SetProp(so, "backButton", backBtn);
        SetProp(so, "cancelButton", cancelBtn);
        SetArrayProp(so, "stepBars", stepBars);
        SetProp(so, "stepLabel", stepLabelTMP);
        so.ApplyModifiedProperties();

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
        tmp.enableWordWrapping = false;
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
