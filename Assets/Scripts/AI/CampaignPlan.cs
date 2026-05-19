using System;
using System.Collections.Generic;
using UnityEngine;

namespace DnD.AI
{
    public enum CampaignSize { Small, Medium, Large }

    /// <summary>Static helpers — never throws, never allocates a Behaviour. Lives next to CampaignPlan
    /// so the table of "what does Medium mean" is one place.</summary>
    public static class CampaignSizeInfo
    {
        public static int MapDim(CampaignSize s)   => s == CampaignSize.Small ? 5 : s == CampaignSize.Large ? 9 : 7;
        public static int BeatCount(CampaignSize s) => s == CampaignSize.Small ? 3 : s == CampaignSize.Large ? 7 : 5;
        public static int FeatureCount(CampaignSize s) => s == CampaignSize.Small ? 3 : s == CampaignSize.Large ? 8 : 5;
        public static string Label(CampaignSize s) => s switch
        {
            CampaignSize.Small  => "Small",
            CampaignSize.Large  => "Large",
            _                   => "Medium",
        };
    }

    /// <summary>Structured campaign plan emitted by the LLM at adventure-start. JsonUtility-friendly:
    /// all fields are concrete types. Falls back to a minimal plan if the JSON round-trip fails.</summary>
    [Serializable]
    public class CampaignPlan
    {
        public string       seed;
        public string       sizeName = "Medium";     // CampaignSize.ToString() — JsonUtility can't serialise the enum cleanly across project assemblies
        public string       hook;
        public List<string> beats        = new List<string>();
        public string       climax;
        public string       resolution;
        public string       startingArea;
        public List<string> keyLocations = new List<string>();
        public List<string> keyNPCs      = new List<string>();
        /// <summary>Human-readable rollup shown in the chat after generation.</summary>
        public string       timelineText;

        public CampaignSize Size
        {
            get
            {
                if (Enum.TryParse<CampaignSize>(sizeName ?? "", true, out var s)) return s;
                return CampaignSize.Medium;
            }
            set => sizeName = value.ToString();
        }

        public int MapDim   => CampaignSizeInfo.MapDim(Size);

        /// <summary>Renders the plan as plain text for the chat / save file. Stable so saves diff cleanly.</summary>
        public string ToReadableText()
        {
            var sb = new System.Text.StringBuilder(512);
            sb.AppendLine($"=== Campaign · {CampaignSizeInfo.Label(Size)} ===");
            if (!string.IsNullOrEmpty(hook))         sb.AppendLine($"Hook: {hook}");
            if (!string.IsNullOrEmpty(startingArea)) sb.AppendLine($"Starting area: {startingArea}");
            if (beats != null && beats.Count > 0)
            {
                sb.AppendLine("Beats:");
                for (int i = 0; i < beats.Count; i++) sb.AppendLine($"  {i + 1}. {beats[i]}");
            }
            if (!string.IsNullOrEmpty(climax))      sb.AppendLine($"Climax: {climax}");
            if (!string.IsNullOrEmpty(resolution))  sb.AppendLine($"Resolution: {resolution}");
            if (keyLocations != null && keyLocations.Count > 0)
                sb.AppendLine($"Key locations: {string.Join(", ", keyLocations)}");
            if (keyNPCs != null && keyNPCs.Count > 0)
                sb.AppendLine($"Key NPCs: {string.Join(", ", keyNPCs)}");
            return sb.ToString().TrimEnd();
        }

        /// <summary>Minimal fallback when the LLM can't be reached or returns unparseable text.</summary>
        public static CampaignPlan Fallback(string seed, CampaignSize size)
        {
            var p = new CampaignPlan
            {
                seed         = seed ?? "",
                hook         = "An unexpected adventure begins.",
                startingArea = "the threshold of the unknown",
                climax       = "Confront the source of trouble.",
                resolution   = "Return home, changed.",
            };
            p.Size = size;
            int n = CampaignSizeInfo.BeatCount(size);
            for (int i = 1; i <= n; i++) p.beats.Add($"Beat {i}: encounter awaits.");
            p.timelineText = p.ToReadableText();
            return p;
        }
    }
}
