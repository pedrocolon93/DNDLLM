// Assets/Scripts/Services/SaveSystem.cs
using UnityEngine;
using System.IO;
using DnD.Data;

namespace DNDLLM.Services
{
    public static class SaveSystem
    {
        private static string SaveDir => Path.Combine(Application.persistentDataPath, "Saves");

        /// <summary>Loads one slot's SaveData + portrait + map token. Returns (null, null, null) if slot is empty.</summary>
        public static (SaveData data, Texture2D portrait, Texture2D mapToken) Load(int slotIndex)
        {
            string jsonPath = SlotJsonPath(slotIndex);
            if (!File.Exists(jsonPath)) return (null, null, null);

            SaveData data = null;
            try
            {
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(jsonPath));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveSystem] Failed to parse slot {slotIndex}: {e.Message}");
                return (null, null, null);
            }

            return (data, LoadPng(SlotPortraitPath(slotIndex)), LoadPng(SlotMapTokenPath(slotIndex)));
        }

        /// <summary>Writes SaveData + optional portrait PNG + optional map-token PNG to the slot's files.</summary>
        public static void Save(int slotIndex, SaveData data, Texture2D portrait, Texture2D mapToken = null)
        {
            Directory.CreateDirectory(SaveDir);
            data.slotIndex  = slotIndex;
            data.lastPlayed = System.DateTime.UtcNow.ToString("o");
            File.WriteAllText(SlotJsonPath(slotIndex), JsonUtility.ToJson(data, prettyPrint: true));
            WritePng(SlotPortraitPath(slotIndex), portrait, "Portrait");
            WritePng(SlotMapTokenPath(slotIndex), mapToken, "Map token");
        }

        /// <summary>Deletes all files for the given slot.</summary>
        public static void Delete(int slotIndex)
        {
            foreach (string p in new[] { SlotJsonPath(slotIndex), SlotPortraitPath(slotIndex), SlotMapTokenPath(slotIndex) })
                if (File.Exists(p)) File.Delete(p);
        }

        private static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(File.ReadAllBytes(path))) return tex;
            UnityEngine.Object.DestroyImmediate(tex);
            return null;
        }

        private static void WritePng(string path, Texture2D tex, string label)
        {
            if (tex == null) return;
            byte[] png = tex.EncodeToPNG();
            if (png != null) File.WriteAllBytes(path, png);
            else Debug.LogWarning($"[SaveSystem] {label} for {Path.GetFileName(path)} could not be encoded — skipping.");
        }

        private static string SlotJsonPath(int i)      => Path.Combine(SaveDir, $"slot_{i}.json");
        private static string SlotPortraitPath(int i)  => Path.Combine(SaveDir, $"slot_{i}_portrait.png");
        private static string SlotMapTokenPath(int i)  => Path.Combine(SaveDir, $"slot_{i}_token.png");
    }
}
