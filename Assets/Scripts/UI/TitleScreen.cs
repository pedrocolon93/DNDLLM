// Assets/Scripts/UI/TitleScreen.cs
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DnD.Data;
using DNDLLM.Services;

namespace DnD.UI
{
    public class TitleScreen : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button newGameButton;

        [Header("Slot Rows (3 entries, index 0-2)")]
        [SerializeField] private Button[]   slotButtons;       // entire row is clickable
        [SerializeField] private RawImage[] slotPortraits;     // 36x36 portrait thumbnail
        [SerializeField] private TMP_Text[] slotNameTexts;     // "Aric the Bold"
        [SerializeField] private TMP_Text[] slotSubTexts;      // "Fighter · Level 3 · Human"
        [SerializeField] private TMP_Text[] slotCampaignTexts; // "The Sunken Crypts…"
        [SerializeField] private TMP_Text[] slotDateTexts;     // "2 days ago"

        // ── Events ────────────────────────────────────────────────────────
        public event Action<int> OnSlotSelected;  // loaded slot index
        public event Action      OnNewGame;

        private void OnEnable() => Refresh();

        public void Refresh()
        {
            if (Application.isPlaying == false) return;

            if (newGameButton != null)
            {
                newGameButton.onClick.RemoveAllListeners();
                newGameButton.onClick.AddListener(() => OnNewGame?.Invoke());
            }
            else
            {
                Debug.LogWarning("[TitleScreen] newGameButton is not assigned in the Inspector.", this);
            }

            for (int i = 0; i < 3; i++)
            {
                if (slotButtons == null || i >= slotButtons.Length || slotButtons[i] == null)
                    continue;

                var (data, portrait) = SaveSystem.Load(i);
                bool populated = data != null;

                slotButtons[i].interactable = populated;

                if (populated)
                {
                    if (slotNameTexts != null && i < slotNameTexts.Length && slotNameTexts[i] != null)
                        slotNameTexts[i].text = data.characterName;

                    if (slotSubTexts != null && i < slotSubTexts.Length && slotSubTexts[i] != null)
                        slotSubTexts[i].text = $"{data.className} · Level {data.level} · {data.raceName}";

                    if (slotCampaignTexts != null && i < slotCampaignTexts.Length && slotCampaignTexts[i] != null)
                    {
                        string seed = data.campaignSeed ?? "";
                        slotCampaignTexts[i].text = seed.Length > 30 ? seed.Substring(0, 30) + "…" : seed;
                    }

                    if (slotDateTexts != null && i < slotDateTexts.Length && slotDateTexts[i] != null)
                        slotDateTexts[i].text = FormatDate(data.lastPlayed);

                    if (slotPortraits != null && i < slotPortraits.Length && slotPortraits[i] != null)
                    {
                        var oldTex = slotPortraits[i].texture;
                        if (oldTex != null) UnityEngine.Object.Destroy(oldTex);
                        slotPortraits[i].texture = portrait;
                    }
                }
                else
                {
                    if (slotNameTexts != null && i < slotNameTexts.Length && slotNameTexts[i] != null)
                        slotNameTexts[i].text = "Empty slot";

                    if (slotSubTexts != null && i < slotSubTexts.Length && slotSubTexts[i] != null)
                        slotSubTexts[i].text = "";

                    if (slotCampaignTexts != null && i < slotCampaignTexts.Length && slotCampaignTexts[i] != null)
                        slotCampaignTexts[i].text = "";

                    if (slotDateTexts != null && i < slotDateTexts.Length && slotDateTexts[i] != null)
                        slotDateTexts[i].text = "";

                    if (slotPortraits != null && i < slotPortraits.Length && slotPortraits[i] != null)
                    {
                        var oldTex = slotPortraits[i].texture;
                        if (oldTex != null) UnityEngine.Object.Destroy(oldTex);
                        slotPortraits[i].texture = null;
                    }
                }

                int captured = i;
                slotButtons[i].onClick.RemoveAllListeners();
                slotButtons[i].onClick.AddListener(() => OnSlotSelected?.Invoke(captured));
            }
        }

        private static string FormatDate(string isoDate)
        {
            if (string.IsNullOrEmpty(isoDate)) return "";
            if (!DateTime.TryParse(isoDate, null, DateTimeStyles.RoundtripKind, out var dt))
                return "";
            var diff = DateTime.UtcNow - dt;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalHours   < 1) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays    < 1) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays    < 2) return "Yesterday";
            return $"{(int)diff.TotalDays} days ago";
        }
    }
}
