using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DnD.Character;
using DnD.Core;

namespace DnD.UI
{
    /// <summary>
    /// Read-only character sheet overlay: portrait, ability scores, HP, AC, and backstory.
    /// Opened by the CHARACTER HUD button; closed by its own Close button.
    /// </summary>
    public class CharacterScreenPanel : MonoBehaviour
    {
        public static CharacterScreenPanel Instance { get; private set; }

        // ── Wired by UISceneBuilder ──────────────────────────────────────────
        [SerializeField] private RawImage          portraitImage;
        [SerializeField] private TMP_Text   nameText;
        [SerializeField] private TMP_Text   raceClassText;
        [SerializeField] private TMP_Text   levelText;
        [SerializeField] private TMP_Text   hpText;
        [SerializeField] private TMP_Text   acText;
        /// <summary>Six labels, one per ability: STR/DEX/CON/INT/WIS/CHA.</summary>
        [SerializeField] private TMP_Text[] abilityLabels;
        [SerializeField] private TMP_Text   appearanceText;
        [SerializeField] private TMP_Text   backstoryText;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void Open(CharacterStats stats, Texture2D portrait, string appearance, string backstory)
        {
            gameObject.SetActive(true);
            Refresh(stats, portrait, appearance, backstory);
        }

        public void Close() => gameObject.SetActive(false);

        public void Refresh(CharacterStats stats, Texture2D portrait, string appearance, string backstory)
        {
            if (portraitImage)
            {
                portraitImage.texture = portrait;
                portraitImage.color   = portrait != null ? Color.white : new Color32(0x4A, 0x38, 0x20, 0xFF);
            }

            if (stats == null) return;

            if (nameText)      nameText.text      = stats.characterName;
            if (raceClassText) raceClassText.text = $"{stats.race}  ·  {stats.characterClass?.className.ToString() ?? "Adventurer"}";
            if (levelText)     levelText.text     = $"Level {stats.level}";
            if (hpText)        hpText.text        = $"HP  {stats.currentHitPoints} / {stats.maxHitPoints}";
            if (acText)        acText.text        = $"AC  {stats.armorClass}";

            // Ability scores
            var scoreKeys  = new[] { AbilityScore.Strength, AbilityScore.Dexterity, AbilityScore.Constitution,
                                     AbilityScore.Intelligence, AbilityScore.Wisdom, AbilityScore.Charisma };
            var shortNames = new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
            if (abilityLabels != null)
                for (int i = 0; i < 6 && i < abilityLabels.Length; i++)
                {
                    if (abilityLabels[i] == null) continue;
                    int score = stats.abilities.GetScore(scoreKeys[i]);
                    int mod   = stats.abilities.GetModifier(scoreKeys[i]);
                    string sign = mod >= 0 ? "+" : "";
                    abilityLabels[i].text = $"<b>{shortNames[i]}</b>\n{score}\n<size=80%>({sign}{mod})</size>";
                }

            if (appearanceText) appearanceText.text = string.IsNullOrEmpty(appearance) ? "" : $"<i>{appearance}</i>";
            if (backstoryText)  backstoryText.text  = backstory ?? "";
        }
    }
}
