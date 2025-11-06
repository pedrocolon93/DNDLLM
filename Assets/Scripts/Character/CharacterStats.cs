using UnityEngine;
using System.Collections.Generic;
using DnD.Core;

namespace DnD.Character
{
    /// <summary>
    /// Core character statistics and state
    /// </summary>
    public class CharacterStats : MonoBehaviour
    {
        [Header("Identity")]
        public string characterName = "Adventurer";
        public Race race;
        public CharacterClass characterClass;

        [Header("Level & Experience")]
        public int level = 1;
        public int currentXP = 0;

        [Header("Ability Scores")]
        public AbilityScores abilities = new AbilityScores();

        [Header("Health")]
        public int maxHitPoints = 10;
        public int currentHitPoints = 10;
        public int temporaryHitPoints = 0;

        [Header("Combat Stats")]
        public int armorClass = 10;
        public int initiative;
        public int speed = 30;

        [Header("Conditions")]
        public Condition currentConditions = Condition.None;

        [Header("Resources")]
        public int currentHitDice;
        public int maxHitDice;

        private Dictionary<Condition, int> conditionDurations = new Dictionary<Condition, int>();

        private void Awake()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (characterClass != null)
            {
                maxHitPoints = characterClass.GetHitPointsForLevel(level, abilities.GetModifier(AbilityScore.Constitution));
                currentHitPoints = maxHitPoints;
                maxHitDice = level;
                currentHitDice = level;

                // Calculate AC (10 + DEX modifier for unarmored)
                armorClass = 10 + abilities.GetModifier(AbilityScore.Dexterity);
            }
        }

        public int GetProficiencyBonus()
        {
            return DnDConstants.GetProficiencyBonus(level);
        }

        public int GetAbilityModifier(AbilityScore ability)
        {
            return abilities.GetModifier(ability);
        }

        public int GetSavingThrow(AbilityScore ability)
        {
            int modifier = GetAbilityModifier(ability);

            // Check if proficient in this saving throw
            if (characterClass != null && System.Array.Exists(characterClass.savingThrowProficiencies, s => s == ability))
            {
                modifier += GetProficiencyBonus();
            }

            return modifier;
        }

        public void TakeDamage(int damage)
        {
            // Temporary HP absorbs damage first
            if (temporaryHitPoints > 0)
            {
                int overflow = damage - temporaryHitPoints;
                temporaryHitPoints = Mathf.Max(0, temporaryHitPoints - damage);
                damage = Mathf.Max(0, overflow);
            }

            currentHitPoints -= damage;
            currentHitPoints = Mathf.Max(0, currentHitPoints);

            if (currentHitPoints == 0)
            {
                AddCondition(Condition.Unconscious, -1); // -1 = indefinite
            }
        }

        public void Heal(int healing)
        {
            if (!HasCondition(Condition.Unconscious))
            {
                currentHitPoints = Mathf.Min(currentHitPoints + healing, maxHitPoints);
            }
        }

        public void AddCondition(Condition condition, int duration)
        {
            currentConditions |= condition;
            conditionDurations[condition] = duration;
        }

        public void RemoveCondition(Condition condition)
        {
            currentConditions &= ~condition;
            conditionDurations.Remove(condition);
        }

        public bool HasCondition(Condition condition)
        {
            return (currentConditions & condition) != 0;
        }

        public void UpdateConditionDurations()
        {
            List<Condition> toRemove = new List<Condition>();

            foreach (var kvp in conditionDurations)
            {
                if (kvp.Value > 0)
                {
                    conditionDurations[kvp.Key]--;
                    if (conditionDurations[kvp.Key] == 0)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
            }

            foreach (var condition in toRemove)
            {
                RemoveCondition(condition);
            }
        }

        public void GainXP(int xp)
        {
            currentXP += xp;
            CheckLevelUp();
        }

        private void CheckLevelUp()
        {
            int newLevel = DnDConstants.GetLevelFromXP(currentXP);
            if (newLevel > level)
            {
                LevelUp(newLevel);
            }
        }

        private void LevelUp(int newLevel)
        {
            level = newLevel;

            // Increase hit points
            if (characterClass != null)
            {
                maxHitPoints = characterClass.GetHitPointsForLevel(level, abilities.GetModifier(AbilityScore.Constitution));
                currentHitPoints = maxHitPoints; // Heal on level up

                maxHitDice = level;
                currentHitDice = level;
            }

            Debug.Log($"{characterName} reached level {level}!");
        }

        public int RollInitiative()
        {
            initiative = DiceRoller.D20Roll() + abilities.GetModifier(AbilityScore.Dexterity);
            return initiative;
        }
    }
}
