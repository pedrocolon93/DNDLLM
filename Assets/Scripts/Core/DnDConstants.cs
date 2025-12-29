namespace DnD.Core
{
    /// <summary>
    /// D&D 5e game constants
    /// </summary>
    public static class DnDConstants
    {
        // Experience points required for each level (levels 1-20)
        public static readonly int[] XP_THRESHOLDS =
        {
            0,      // Level 1
            300,    // Level 2
            900,    // Level 3
            2700,   // Level 4
            6500,   // Level 5
            14000,  // Level 6
            23000,  // Level 7
            34000,  // Level 8
            48000,  // Level 9
            64000,  // Level 10
            85000,  // Level 11
            100000, // Level 12
            120000, // Level 13
            140000, // Level 14
            165000, // Level 15
            195000, // Level 16
            225000, // Level 17
            265000, // Level 18
            305000, // Level 19
            355000  // Level 20
        };

        // Proficiency bonus by level
        public static readonly int[] PROFICIENCY_BONUS =
        {
            2, 2, 2, 2,    // Levels 1-4
            3, 3, 3, 3,    // Levels 5-8
            4, 4, 4, 4,    // Levels 9-12
            5, 5, 5, 5,    // Levels 13-16
            6, 6, 6, 6     // Levels 17-20
        };

        // Standard ability score array for point buy
        public static readonly int[] STANDARD_ARRAY = { 15, 14, 13, 12, 10, 8 };

        // Maximum level
        public const int MAX_LEVEL = 20;

        // Base ability score for point buy
        public const int BASE_ABILITY_SCORE = 8;
        public const int MAX_ABILITY_SCORE = 20;

        /// <summary>
        /// Calculate ability modifier from ability score
        /// Formula: (score - 10) / 2 (rounded down)
        /// </summary>
        public static int GetAbilityModifier(int abilityScore)
        {
            return (abilityScore - 10) / 2;
        }

        /// <summary>
        /// Get proficiency bonus for a given level
        /// </summary>
        public static int GetProficiencyBonus(int level)
        {
            if (level < 1) level = 1;
            if (level > MAX_LEVEL) level = MAX_LEVEL;
            return PROFICIENCY_BONUS[level - 1];
        }

        /// <summary>
        /// Get XP required for a given level
        /// </summary>
        public static int GetXPForLevel(int level)
        {
            if (level < 1) level = 1;
            if (level > MAX_LEVEL) level = MAX_LEVEL;
            return XP_THRESHOLDS[level - 1];
        }

        /// <summary>
        /// Calculate level from XP
        /// </summary>
        public static int GetLevelFromXP(int xp)
        {
            for (int i = XP_THRESHOLDS.Length - 1; i >= 0; i--)
            {
                if (xp >= XP_THRESHOLDS[i])
                    return i + 1;
            }
            return 1;
        }
    }
}
