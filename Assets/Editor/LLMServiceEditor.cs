using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using DNDLLM.Services;

namespace DNDLLM.EditorTools
{
    [CustomEditor(typeof(LLMService))]
    public class LLMServiceEditor : Editor
    {
        private static readonly Dictionary<string, string[]> _modelCache = new();
        private static readonly HashSet<string> _inFlight = new();
        private static string _lastError;

        private SerializedProperty providerProp;
        private SerializedProperty useMockProp;
        private SerializedProperty useDebugSpritesProp;
        private SerializedProperty apiKeyProp;
        private SerializedProperty modelProp;
        private SerializedProperty multimodalModelProp;
        private SerializedProperty imageModelProp;
        private SerializedProperty lmStudioBaseUrlProp;
        private SerializedProperty lmStudioApiKeyProp;
        private SerializedProperty lmStudioModelProp;
        private SerializedProperty useCacheProp;
        private SerializedProperty evalModelProp;

        private void OnEnable()
        {
            providerProp        = serializedObject.FindProperty("provider");
            useMockProp         = serializedObject.FindProperty("useMock");
            useDebugSpritesProp = serializedObject.FindProperty("useDebugSprites");
            apiKeyProp          = serializedObject.FindProperty("apiKey");
            modelProp           = serializedObject.FindProperty("model");
            multimodalModelProp = serializedObject.FindProperty("multimodalModel");
            imageModelProp      = serializedObject.FindProperty("imageModel");
            lmStudioBaseUrlProp = serializedObject.FindProperty("lmStudioBaseUrl");
            lmStudioApiKeyProp  = serializedObject.FindProperty("lmStudioApiKey");
            lmStudioModelProp   = serializedObject.FindProperty("lmStudioModel");
            useCacheProp        = serializedObject.FindProperty("useCache");
            evalModelProp       = serializedObject.FindProperty("evalModel");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Provider", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(providerProp);
            EditorGUILayout.PropertyField(useMockProp);
            EditorGUILayout.PropertyField(useDebugSpritesProp);

            var provider = (LLMProvider)providerProp.enumValueIndex;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("OpenRouter (images + vision eval)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(apiKeyProp, new GUIContent("API Key"));
            EditorGUILayout.PropertyField(imageModelProp, new GUIContent("Image Model"));
            EditorGUILayout.PropertyField(evalModelProp, new GUIContent("Vision Eval Model"));
            if (provider == LLMProvider.OpenRouter)
            {
                bool debug = useDebugSpritesProp.boolValue;
                EditorGUILayout.PropertyField(modelProp,           new GUIContent("Text Model (debug)",     "Used when Use Debug Sprites is ON — no images attached, can be text-only."));
                EditorGUILayout.PropertyField(multimodalModelProp, new GUIContent("Multimodal Model (live)", "Used when Use Debug Sprites is OFF — DM attaches the painted battlemap, so this needs vision support."));
                EditorGUILayout.HelpBox(
                    debug
                        ? $"Active: {modelProp.stringValue}  (debug sprites → text-only)"
                        : $"Active: {multimodalModelProp.stringValue}  (live images → multimodal)",
                    MessageType.None);
            }

            if (provider == LLMProvider.LMStudio)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("LM Studio (text)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(lmStudioBaseUrlProp, new GUIContent("Base URL"));
                EditorGUILayout.PropertyField(lmStudioApiKeyProp,  new GUIContent("API Key"));
                DrawLMStudioModelRow();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Cache", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useCacheProp);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLMStudioModelRow()
        {
            string baseUrl = (lmStudioBaseUrlProp.stringValue ?? "").TrimEnd('/');
            string current = lmStudioModelProp.stringValue ?? "";

            EditorGUILayout.BeginHorizontal();
            if (_modelCache.TryGetValue(baseUrl, out var models) && models != null && models.Length > 0)
            {
                int idx = System.Array.IndexOf(models, current);
                if (idx < 0) idx = 0;
                int newIdx = EditorGUILayout.Popup("Model", idx, models);
                if (models[newIdx] != current)
                    lmStudioModelProp.stringValue = models[newIdx];
            }
            else
            {
                EditorGUILayout.PropertyField(lmStudioModelProp, new GUIContent("Model"));
            }

            bool busy = _inFlight.Contains(baseUrl);
            using (new EditorGUI.DisabledScope(busy || string.IsNullOrEmpty(baseUrl)))
            {
                if (GUILayout.Button(busy ? "…" : "Refresh", GUILayout.Width(70)))
                    _ = FetchModelsAsync(baseUrl);
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_lastError))
                EditorGUILayout.HelpBox(_lastError, MessageType.Warning);
        }

        private static async Task FetchModelsAsync(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl) || _inFlight.Contains(baseUrl)) return;
            _inFlight.Add(baseUrl);
            _lastError = null;
            try
            {
                using var req = UnityWebRequest.Get($"{baseUrl}/v1/models");
                req.timeout = 5;
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    _lastError = $"LM Studio /v1/models failed: {req.error}";
                    return;
                }
                var dto = JsonUtility.FromJson<ModelsResponse>(req.downloadHandler.text);
                if (dto?.data == null || dto.data.Length == 0)
                {
                    _lastError = "LM Studio returned no models — load one in LM Studio first.";
                    return;
                }
                var ids = new string[dto.data.Length];
                for (int i = 0; i < dto.data.Length; i++) ids[i] = dto.data[i].id;
                _modelCache[baseUrl] = ids;
            }
            catch (System.Exception e)
            {
                _lastError = $"LM Studio fetch error: {e.Message}";
            }
            finally
            {
                _inFlight.Remove(baseUrl);
                EditorApplication.delayCall += RepaintAll;
            }
        }

        private static void RepaintAll()
        {
            foreach (var ed in Resources.FindObjectsOfTypeAll<LLMServiceEditor>())
                ed.Repaint();
        }

        [System.Serializable] private class ModelsResponse { public Model[] data; }
        [System.Serializable] private class Model { public string id; }
    }
}
