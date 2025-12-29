using UnityEngine;

namespace DnD.Core
{
    /// <summary>
    /// Handles all dice rolling mechanics for D&D 5e
    /// Implements advantage/disadvantage rules
    /// </summary>
    public static class DiceRoller
    {
        public enum RollType
        {
            Normal,
            Advantage,
            Disadvantage
        }

        /// <summary>
        /// Roll a d20 with optional advantage/disadvantage
        /// </summary>
        public static int D20Roll(RollType rollType = RollType.Normal)
        {
            int roll1 = Random.Range(1, 21);
            if (rollType == RollType.Normal) return roll1;

            int roll2 = Random.Range(1, 21);
            return rollType == RollType.Advantage ?
                Mathf.Max(roll1, roll2) : Mathf.Min(roll1, roll2);
        }

        /// <summary>
        /// Roll any die (d4, d6, d8, d10, d12, d20, d100)
        /// </summary>
        public static int Roll(int sides)
        {
            return Random.Range(1, sides + 1);
        }

        /// <summary>
        /// Roll multiple dice and sum the result
        /// </summary>
        public static int Roll(int count, int sides)
        {
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                total += Roll(sides);
            }
            return total;
        }

        /// <summary>
        /// Roll 4d6, drop the lowest (standard D&D character creation)
        /// </summary>
        public static int Roll4d6DropLowest()
        {
            int[] rolls = new int[4];
            for (int i = 0; i < 4; i++)
            {
                rolls[i] = Roll(6);
            }

            // Find and remove lowest
            int min = int.MaxValue;
            int minIndex = 0;
            for (int i = 0; i < 4; i++)
            {
                if (rolls[i] < min)
                {
                    min = rolls[i];
                    minIndex = i;
                }
            }

            int total = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i != minIndex)
                    total += rolls[i];
            }

            return total;
        }

        /// <summary>
        /// Roll damage dice with critical hit support (doubles dice, not modifiers)
        /// </summary>
        public static int RollDamage(int count, int sides, int modifier, bool isCritical = false)
        {
            int diceCount = isCritical ? count * 2 : count;
            return Roll(diceCount, sides) + modifier;
        }
    }
}
