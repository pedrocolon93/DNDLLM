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

        // ── Death-saving-throws (D&D 5e) ─────────────────────────────────
        // Three successes → Stable (still 0HP, unconscious). Three failures → Dead.
        // A nat-20 immediately wakes the character with 1HP. A nat-1 counts as two
        // failures. Damage while dying adds a failure (two on a critical hit).
        public int deathSaveSuccesses;
        public int deathSaveFailures;
        public bool isStable;   // succeeded all three saves
        public bool isDead;     // failed all three saves

        public void TakeDamage(int damage)
        {
            // 5e: if the character is already at 0HP (dying), incoming damage adds
            // a failed death save instead of dropping below 0.
            if (currentHitPoints <= 0 && HasCondition(Condition.Unconscious) && !isDead)
            {
                AddDeathSaveFailure();
                return;
            }

            // Temporary HP absorbs damage first
            if (temporaryHitPoints > 0)
            {
                int overflow = damage - temporaryHitPoints;
                temporaryHitPoints = Mathf.Max(0, temporaryHitPoints - damage);
                damage = Mathf.Max(0, overflow);
            }

            currentHitPoints -= damage;
            currentHitPoints = Mathf.Max(0, currentHitPoints);

            if (currentHitPoints == 0 && !isDead)
            {
                AddCondition(Condition.Unconscious, -1); // -1 = indefinite
                isStable = false;                          // newly dropped — reset stability
            }
        }

        public void Heal(int healing)
        {
            // 5e: any healing > 0 revives a dying character. Stable characters also
            // wake (but at the rolled HP, not zero). Death overrides healing.
            if (isDead) return;
            currentHitPoints = Mathf.Min(currentHitPoints + healing, maxHitPoints);
            if (currentHitPoints > 0)
            {
                deathSaveSuccesses = 0;
                deathSaveFailures  = 0;
                isStable           = false;
                RemoveCondition(Condition.Unconscious);
            }
        }

        /// <summary>Roll a death save. Returns (rolled value, outcome message). Caller
        /// is responsible for displaying the outcome.</summary>
        public (int roll, string outcome) RollDeathSave()
        {
            if (isDead)        return (0, $"{characterName} is already dead.");
            if (isStable)      return (0, $"{characterName} is stable.");
            if (currentHitPoints > 0) return (0, $"{characterName} doesn't need to roll death saves.");

            int roll = DnD.Core.DiceRoller.D20Roll();
            if (roll == 20)
            {
                // Critical success — regain 1HP, wake up.
                currentHitPoints = 1;
                deathSaveSuccesses = 0;
                deathSaveFailures  = 0;
                isStable = false;
                RemoveCondition(Condition.Unconscious);
                return (roll, $"{characterName} rolls a NATURAL 20 — regains consciousness at 1 HP!");
            }
            if (roll == 1)
            {
                AddDeathSaveFailure();
                AddDeathSaveFailure();
                return (roll, $"{characterName} rolls a 1 — two failed death saves! ({deathSaveFailures}/3)");
            }
            if (roll >= 10)
            {
                AddDeathSaveSuccess();
                return (roll, $"{characterName} succeeds on a death save. ({deathSaveSuccesses}/3)");
            }
            AddDeathSaveFailure();
            return (roll, $"{characterName} fails a death save. ({deathSaveFailures}/3)");
        }

        private void AddDeathSaveSuccess()
        {
            deathSaveSuccesses = Mathf.Min(3, deathSaveSuccesses + 1);
            if (deathSaveSuccesses >= 3) isStable = true;
        }

        private void AddDeathSaveFailure()
        {
            deathSaveFailures = Mathf.Min(3, deathSaveFailures + 1);
            if (deathSaveFailures >= 3) isDead = true;
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
