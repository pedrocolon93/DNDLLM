// Assets/Scripts/Services/SaveSystem.cs
using UnityEngine;
using System.IO;
using DnD.Data;

namespace DNDLLM.Services
{
    public static class SaveSystem
    {
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
                {
                    UnityEngine.Object.DestroyImmediate(portrait);
                    portrait = null;
                }
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
            {
                byte[] png = portrait.EncodeToPNG();
                if (png != null)
                    File.WriteAllBytes(SlotPortraitPath(slotIndex), png);
                else
                    Debug.LogWarning($"[SaveSystem] Portrait for slot {slotIndex} could not be encoded — skipping.");
            }
        }

        /// <summary>Deletes all files for the given slot.</summary>
        public static void Delete(int slotIndex)
        {
            string jsonPath = SlotJsonPath(slotIndex);
            string portraitPath = SlotPortraitPath(slotIndex);
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
            if (File.Exists(portraitPath)) File.Delete(portraitPath);
        }

        private static string SlotJsonPath(int i)     => Path.Combine(SaveDir, $"slot_{i}.json");
        private static string SlotPortraitPath(int i)  => Path.Combine(SaveDir, $"slot_{i}_portrait.png");
    }
}
