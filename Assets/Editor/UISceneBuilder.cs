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

        // Note: assigning a tag requires the tag to exist in TagManager.
        // We use a try/catch to avoid errors if "MapCamera" tag hasn't been created yet.
        try { mapCamGO.tag = "MapCamera"; }
        catch (UnityException) { /* Tag not registered; lookup uses GameObject.Find("MapCamera") instead */ }

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
