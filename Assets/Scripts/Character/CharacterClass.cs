using UnityEngine;
using DnD.Core;

namespace DnD.Character
{
    /// <summary>
    /// ScriptableObject defining a D&D character class
    /// </summary>
    [CreateAssetMenu(fileName = "New Class", menuName = "DnD/Character Class")]
    public class CharacterClass : ScriptableObject
    {
        [Header("Basic Info")]
        public CharacterClassName className;
        public string description;
        public Sprite classIcon;

        [Header("Hit Points")]
        [Tooltip("Hit die size (d6, d8, d10, d12)")]
        public int hitDieSize = 8;

        [Header("Primary Abilities")]
        public AbilityScore primaryAbility;
        public AbilityScore secondaryAbility;

        [Header("Proficiencies")]
        public AbilityScore[] savingThrowProficiencies;
        public ArmorType[] armorProficiencies;
        public WeaponType[] weaponProficiencies;

        [Header("Starting Equipment")]
        public int startingGold = 100;

        [Header("Spellcasting")]
        public bool isSpellcaster;
        public AbilityScore spellcastingAbility;
        public int spellsKnownPerLevel = 0;

        [Header("Class Features")]
        [TextArea(3, 10)]
        public string[] classFeatureDescriptions;

        /// <summary>
        /// Calculate hit points for a level
        /// First level gets max die, subsequent levels get average + CON modifier
        /// </summary>
        public int GetHitPointsForLevel(int level, int constitutionModifier)
        {
            if (level == 1)
            {
                return hitDieSize + constitutionModifier;
            }
            else
            {
                int avgRoll = (hitDieSize / 2) + 1;
                return (hitDieSize + constitutionModifier) + ((level - 1) * (avgRoll + constitutionModifier));
            }
        }

        /// <summary>
        /// Get recommended ability score priority for this class
        /// </summary>
        public string GetAbilityPriority()
        {
            switch (className)
            {
                case CharacterClassName.Fighter:
                    return "STR > CON > DEX";
                case CharacterClassName.Wizard:
                    return "INT > DEX > CON";
                case CharacterClassName.Rogue:
                    return "DEX > INT > CHA";
                case CharacterClassName.Cleric:
                    return "WIS > CON > STR";
                default:
                    return "Varies";
            }
        }
    }
}
