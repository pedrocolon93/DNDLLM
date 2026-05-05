// Assets/Scripts/Services/SaveSystem.cs
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using DnD.Data;

namespace DNDLLM.Services
{
    /// <summary>Resources read back when loading a save slot — references stay null when not on disk.</summary>
    public sealed class SlotLoadResult
    {
        public SaveData          Data;
        public Texture2D         Portrait;
        public Texture2D         MapToken;
        public Texture2D         MapBackground;
        public List<Texture2D>   EntitySprites = new List<Texture2D>();
    }

    public static class SaveSystem
    {
        private static string SaveDir => Path.Combine(Application.persistentDataPath, "Saves");

        /// <summary>Loads one slot's SaveData + portrait + map token + map background + entity sprites.
        /// Returns null if the slot is empty.</summary>
        public static SlotLoadResult Load(int slotIndex)
        {
            string jsonPath = SlotJsonPath(slotIndex);
            if (!File.Exists(jsonPath)) return null;

            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(jsonPath));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveSystem] Failed to parse slot {slotIndex}: {e.Message}");
                return null;
            }

            var result = new SlotLoadResult
            {
                Data          = data,
                Portrait      = LoadPng(SlotPortraitPath(slotIndex)),
                MapToken      = LoadPng(SlotMapTokenPath(slotIndex)),
                MapBackground = LoadPng(SlotMapBackgroundPath(slotIndex)),
            };

            int entityCount = data.entities != null ? data.entities.Count : 0;
            for (int i = 0; i < entityCount; i++)
                result.EntitySprites.Add(LoadPng(SlotEntitySpritePath(slotIndex, i)));

            return result;
        }

        /// <summary>
        /// Writes SaveData + portrait + map token + map background + entity sprites for the slot.
        /// Any null texture is skipped (the existing file on disk is left untouched). Stale entity
        /// sprites from a previous save are removed when fewer entities are written this time.
        /// </summary>
        public static void Save(
            int slotIndex,
            SaveData data,
            Texture2D portrait      = null,
            Texture2D mapToken      = null,
            Texture2D mapBackground = null,
            IList<Texture2D> entitySprites = null)
        {
            Directory.CreateDirectory(SaveDir);
            data.slotIndex  = slotIndex;
            data.lastPlayed = System.DateTime.UtcNow.ToString("o");
            File.WriteAllText(SlotJsonPath(slotIndex), JsonUtility.ToJson(data, prettyPrint: true));
            WritePng(SlotPortraitPath(slotIndex),      portrait,      "Portrait");
            WritePng(SlotMapTokenPath(slotIndex),      mapToken,      "Map token");
            WritePng(SlotMapBackgroundPath(slotIndex), mapBackground, "Map background");

            // Entity sprites: write new set, then delete any lingering files past the new count.
            int kept = 0;
            if (entitySprites != null)
                for (int i = 0; i < entitySprites.Count; i++, kept++)
                    WritePng(SlotEntitySpritePath(slotIndex, i), entitySprites[i], $"Entity sprite {i}");

            // Sweep stale files: try indices kept..kept+32 and delete any that exist.
            for (int i = kept; i < kept + 32; i++)
            {
                string p = SlotEntitySpritePath(slotIndex, i);
                if (File.Exists(p)) File.Delete(p);
            }
        }

        /// <summary>Deletes all files for the given slot — JSON, legacy + per-player portrait/token images,
        /// the map background, and entity sprites — leaving no orphan PNGs.</summary>
        public static void Delete(int slotIndex)
        {
            foreach (string p in new[]
            {
                SlotJsonPath(slotIndex),
                SlotPortraitPath(slotIndex),
                SlotMapTokenPath(slotIndex),
                SlotMapBackgroundPath(slotIndex),
            })
                if (File.Exists(p)) File.Delete(p);

            // Sweep entity sprites and per-player images.
            for (int i = 0; i < 64; i++)
            {
                string p = SlotEntitySpritePath(slotIndex, i);
                if (File.Exists(p)) File.Delete(p);
            }
            DeletePlayerImages(slotIndex);
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

        // ── Multi-player extras ──────────────────────────────────────────────
        // The single-player Save/Load above keeps the slot_{i}_portrait.png and
        // slot_{i}_token.png paths for the active/lead character. For additional
        // party members (index ≥ 1) callers use SavePlayerImage / LoadPlayerImage
        // with the new slot_{i}_player_{j}_*.png paths. Player index 0 falls back
        // to the legacy paths so old saves keep loading without a migration step.

        public enum PlayerImageKind { Portrait, MapToken }

        public static void SavePlayerImage(int slotIndex, int playerIndex, PlayerImageKind kind, Texture2D tex)
        {
            if (tex == null) return;
            Directory.CreateDirectory(SaveDir);
            WritePng(PlayerImagePath(slotIndex, playerIndex, kind), tex,
                     kind == PlayerImageKind.Portrait ? "Player portrait" : "Player map token");
        }

        public static Texture2D LoadPlayerImage(int slotIndex, int playerIndex, PlayerImageKind kind)
        {
            string newPath = PlayerImagePath(slotIndex, playerIndex, kind);
            if (File.Exists(newPath)) return LoadPng(newPath);

            // Legacy fallback: player 0 maps to the original slot_{i}_portrait.png /
            // slot_{i}_token.png so pre-multi-player saves still produce sprites.
            if (playerIndex == 0)
                return LoadPng(kind == PlayerImageKind.Portrait
                    ? SlotPortraitPath(slotIndex)
                    : SlotMapTokenPath(slotIndex));

            return null;
        }

        public static void DeletePlayerImages(int slotIndex)
        {
            // Sweep the per-player files for up to 4 players × 2 kinds. Cheap and ensures
            // a slot delete leaves no orphan PNGs behind.
            for (int j = 0; j < 4; j++)
            {
                foreach (PlayerImageKind k in System.Enum.GetValues(typeof(PlayerImageKind)))
                {
                    string p = PlayerImagePath(slotIndex, j, k);
                    if (File.Exists(p)) File.Delete(p);
                }
            }
        }

        private static string PlayerImagePath(int slotIndex, int playerIndex, PlayerImageKind kind)
        {
            string suffix = kind == PlayerImageKind.Portrait ? "portrait" : "token";
            return Path.Combine(SaveDir, $"slot_{slotIndex}_player_{playerIndex}_{suffix}.png");
        }

        private static string SlotJsonPath(int i)              => Path.Combine(SaveDir, $"slot_{i}.json");
        private static string SlotPortraitPath(int i)          => Path.Combine(SaveDir, $"slot_{i}_portrait.png");
        private static string SlotMapTokenPath(int i)          => Path.Combine(SaveDir, $"slot_{i}_token.png");
        private static string SlotMapBackgroundPath(int i)     => Path.Combine(SaveDir, $"slot_{i}_map.png");
        private static string SlotEntitySpritePath(int i, int e)=> Path.Combine(SaveDir, $"slot_{i}_entity_{e}.png");
    }
}
