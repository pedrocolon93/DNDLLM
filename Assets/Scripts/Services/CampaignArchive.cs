// Assets/Scripts/Services/CampaignArchive.cs
//
// Folder-per-slot archive that augments the flat SaveSystem. Layout under
// Application.persistentDataPath/Campaigns/slot_{N}/:
//
//   save.json                Slim metadata pointer (slot index, last played, character)
//   campaign.json            CampaignPlan JSON (full structured plan)
//   history.jsonl            One ChatMessageData per line (append-only friendly)
//   images/portrait.png      Lead character portrait
//   images/map.png           Painted map background (current visible map)
//   images/player_token.png  Lead character map token
//   images/players/{i}_portrait.png       (party member i ≥ 1)
//   images/players/{i}_token.png
//   images/entities/{idx}_{name}.png      (map enemies + NPCs)
//   images/tiles/{x}_{y}.png              (only when per-tile generation is used)
//
// The flat SaveSystem keeps writing alongside this for back-compat; the
// CampaignArchive is the readable, inspectable, "everything in one folder"
// view that the user asked for. Either layout can drive a load; CampaignArchive
// is preferred when both exist.

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DnD.Data;
using DnD.AI;

namespace DNDLLM.Services
{
    /// <summary>What you get back from CampaignArchive.Load — same shape as SlotLoadResult plus
    /// the structured plan and a hydrated history list.</summary>
    public sealed class CampaignArchiveResult
    {
        public SaveData          Data;
        public CampaignPlan      Plan;
        public List<ChatMessageData> History = new List<ChatMessageData>();
        public Texture2D         Portrait;
        public Texture2D         MapToken;
        public Texture2D         MapBackground;
        public List<Texture2D>   EntitySprites = new List<Texture2D>();
    }

    public static class CampaignArchive
    {
        private static string RootDir => Path.Combine(Application.persistentDataPath, "Campaigns");
        private static string SlotDir(int i)              => Path.Combine(RootDir, $"slot_{i}");
        private static string SaveJsonPath(int i)         => Path.Combine(SlotDir(i), "save.json");
        private static string CampaignJsonPath(int i)     => Path.Combine(SlotDir(i), "campaign.json");
        private static string HistoryPath(int i)          => Path.Combine(SlotDir(i), "history.jsonl");
        private static string ImagesDir(int i)            => Path.Combine(SlotDir(i), "images");
        private static string EntitiesDir(int i)          => Path.Combine(ImagesDir(i), "entities");
        private static string PlayersDir(int i)           => Path.Combine(ImagesDir(i), "players");
        private static string TilesDir(int i)             => Path.Combine(ImagesDir(i), "tiles");
        private static string PortraitPath(int i)         => Path.Combine(ImagesDir(i), "portrait.png");
        private static string MapPath(int i)              => Path.Combine(ImagesDir(i), "map.png");
        private static string TokenPath(int i)            => Path.Combine(ImagesDir(i), "player_token.png");
        private static string PlayerImagePath(int i, int j, bool token)
            => Path.Combine(PlayersDir(i), $"{j}_{(token ? "token" : "portrait")}.png");
        private static string EntityImagePath(int i, int idx, string name)
            => Path.Combine(EntitiesDir(i), $"{idx:D2}_{SafeName(name)}.png");
        private static string TileImagePath(int i, int x, int y)
            => Path.Combine(TilesDir(i), $"{x}_{y}.png");

        public static bool Exists(int slotIndex) => File.Exists(SaveJsonPath(slotIndex));

        public static void Save(
            int slotIndex,
            SaveData data,
            CampaignPlan plan,
            IList<ChatMessageData> history,
            Texture2D portrait,
            Texture2D mapToken,
            Texture2D mapBackground,
            IList<Texture2D> entitySprites)
        {
            Directory.CreateDirectory(SlotDir(slotIndex));
            Directory.CreateDirectory(ImagesDir(slotIndex));

            // Slim save.json — same data the flat SaveSystem writes (kept dual for back-compat).
            data.slotIndex  = slotIndex;
            data.lastPlayed = System.DateTime.UtcNow.ToString("o");
            File.WriteAllText(SaveJsonPath(slotIndex),
                JsonUtility.ToJson(data, prettyPrint: true));

            // campaign.json — the structured plan, separate from save.json so a human can
            // open the folder, read the plan, and edit it without touching gameplay state.
            if (plan != null)
                File.WriteAllText(CampaignJsonPath(slotIndex),
                    JsonUtility.ToJson(plan, prettyPrint: true));

            // history.jsonl — one ChatMessageData per line. Re-written each save (simpler
            // than maintaining a write-cursor; the file stays small relative to the images).
            if (history != null && history.Count > 0)
            {
                var sb = new StringBuilder(history.Count * 64);
                foreach (var m in history)
                {
                    if (m == null) continue;
                    sb.AppendLine(JsonUtility.ToJson(m));
                }
                File.WriteAllText(HistoryPath(slotIndex), sb.ToString());
            }
            else if (File.Exists(HistoryPath(slotIndex)))
            {
                File.Delete(HistoryPath(slotIndex));
            }

            WritePng(PortraitPath(slotIndex), portrait);
            WritePng(TokenPath(slotIndex),    mapToken);
            WritePng(MapPath(slotIndex),      mapBackground);

            // Entities: write the current set with stable names, then sweep stale files.
            Directory.CreateDirectory(EntitiesDir(slotIndex));
            int kept = 0;
            if (entitySprites != null && data.entities != null)
            {
                int max = System.Math.Min(entitySprites.Count, data.entities.Count);
                for (int i = 0; i < max; i++, kept++)
                    WritePng(EntityImagePath(slotIndex, i, data.entities[i]?.name ?? "entity"),
                             entitySprites[i]);
            }
            // Sweep entity files past the current count.
            if (Directory.Exists(EntitiesDir(slotIndex)))
                foreach (var f in Directory.GetFiles(EntitiesDir(slotIndex), "*.png"))
                {
                    string fname = Path.GetFileName(f);
                    int us = fname.IndexOf('_');
                    if (us <= 0 || !int.TryParse(fname.Substring(0, us), out int idx)) continue;
                    if (idx >= kept) File.Delete(f);
                }
        }

        public static void SavePlayerImage(int slotIndex, int playerIndex, Texture2D tex, bool isToken)
        {
            if (tex == null) return;
            Directory.CreateDirectory(PlayersDir(slotIndex));
            WritePng(PlayerImagePath(slotIndex, playerIndex, isToken), tex);
        }

        public static void SaveTileImage(int slotIndex, int x, int y, Texture2D tex)
        {
            if (tex == null) return;
            Directory.CreateDirectory(TilesDir(slotIndex));
            WritePng(TileImagePath(slotIndex, x, y), tex);
        }

        public static CampaignArchiveResult Load(int slotIndex)
        {
            string sj = SaveJsonPath(slotIndex);
            if (!File.Exists(sj)) return null;

            var r = new CampaignArchiveResult();
            try { r.Data = JsonUtility.FromJson<SaveData>(File.ReadAllText(sj)); }
            catch (System.Exception e) { Debug.LogError($"[CampaignArchive] save.json parse failed: {e.Message}"); return null; }

            if (File.Exists(CampaignJsonPath(slotIndex)))
            {
                try { r.Plan = JsonUtility.FromJson<CampaignPlan>(File.ReadAllText(CampaignJsonPath(slotIndex))); }
                catch (System.Exception e) { Debug.LogWarning($"[CampaignArchive] campaign.json parse failed: {e.Message}"); }
            }

            if (File.Exists(HistoryPath(slotIndex)))
            {
                foreach (var line in File.ReadAllLines(HistoryPath(slotIndex)))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var msg = JsonUtility.FromJson<ChatMessageData>(line);
                        if (msg != null) r.History.Add(msg);
                    }
                    catch (System.Exception) { /* skip malformed line */ }
                }
            }

            r.Portrait      = LoadPng(PortraitPath(slotIndex));
            r.MapToken      = LoadPng(TokenPath(slotIndex));
            r.MapBackground = LoadPng(MapPath(slotIndex));

            if (r.Data != null && r.Data.entities != null && Directory.Exists(EntitiesDir(slotIndex)))
            {
                for (int i = 0; i < r.Data.entities.Count; i++)
                {
                    string name = r.Data.entities[i]?.name ?? "entity";
                    string p    = EntityImagePath(slotIndex, i, name);
                    r.EntitySprites.Add(File.Exists(p) ? LoadPng(p) : null);
                }
            }

            return r;
        }

        public static Texture2D LoadPlayerImage(int slotIndex, int playerIndex, bool isToken)
            => LoadPng(PlayerImagePath(slotIndex, playerIndex, isToken));

        public static void Delete(int slotIndex)
        {
            string dir = SlotDir(slotIndex);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static Texture2D LoadPng(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(File.ReadAllBytes(path))) return tex;
            Object.DestroyImmediate(tex);
            return null;
        }

        private static void WritePng(string path, Texture2D tex)
        {
            if (tex == null) return;
            byte[] png = null;
            try { png = tex.EncodeToPNG(); }
            catch (System.Exception e) { Debug.LogWarning($"[CampaignArchive] EncodeToPNG failed for {Path.GetFileName(path)}: {e.Message}"); }
            if (png == null || png.Length == 0) return;
            File.WriteAllBytes(path, png);
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "entity";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString().Trim('_');
        }
    }
}
