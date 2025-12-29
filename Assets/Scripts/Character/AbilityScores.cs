using System;
using System.Collections.Generic;
using UnityEngine;
using DnD.Core;

namespace DnD.Character
{
    /// <summary>
    /// Stores and manages the six D&D ability scores
    /// </summary>
    [Serializable]
    public class AbilityScores
    {
        [SerializeField] private int strength = 10;
        [SerializeField] private int dexterity = 10;
        [SerializeField] private int constitution = 10;
        [SerializeField] private int intelligence = 10;
        [SerializeField] private int wisdom = 10;
        [SerializeField] private int charisma = 10;

        private Dictionary<AbilityScore, int> abilityDict;

        public AbilityScores()
        {
            InitializeDictionary();
        }

        public AbilityScores(int str, int dex, int con, int intel, int wis, int cha)
        {
            strength = str;
            dexterity = dex;
            constitution = con;
            intelligence = intel;
            wisdom = wis;
            charisma = cha;
            InitializeDictionary();
        }

        private void InitializeDictionary()
        {
            abilityDict = new Dictionary<AbilityScore, int>
            {
                { AbilityScore.Strength, strength },
                { AbilityScore.Dexterity, dexterity },
                { AbilityScore.Constitution, constitution },
                { AbilityScore.Intelligence, intelligence },
                { AbilityScore.Wisdom, wisdom },
                { AbilityScore.Charisma, charisma }
            };
        }

        public int GetScore(AbilityScore ability)
        {
            if (abilityDict == null) InitializeDictionary();
            return abilityDict[ability];
        }

        public void SetScore(AbilityScore ability, int value)
        {
            if (abilityDict == null) InitializeDictionary();
            abilityDict[ability] = Mathf.Clamp(value, 1, 30);
            UpdateSerializedFields();
        }

        public int GetModifier(AbilityScore ability)
        {
            return DnDConstants.GetAbilityModifier(GetScore(ability));
        }

        private void UpdateSerializedFields()
        {
            strength = abilityDict[AbilityScore.Strength];
            dexterity = abilityDict[AbilityScore.Dexterity];
            constitution = abilityDict[AbilityScore.Constitution];
            intelligence = abilityDict[AbilityScore.Intelligence];
            wisdom = abilityDict[AbilityScore.Wisdom];
            charisma = abilityDict[AbilityScore.Charisma];
        }

        /// <summary>
        /// Generate random ability scores using 4d6 drop lowest method
        /// </summary>
        public static AbilityScores GenerateRandom()
        {
            return new AbilityScores(
                DiceRoller.Roll4d6DropLowest(),
                DiceRoller.Roll4d6DropLowest(),
                DiceRoller.Roll4d6DropLowest(),
                DiceRoller.Roll4d6DropLowest(),
                DiceRoller.Roll4d6DropLowest(),
                DiceRoller.Roll4d6DropLowest()
            );
        }

        /// <summary>
        /// Use standard array [15, 14, 13, 12, 10, 8]
        /// </summary>
        public static AbilityScores UseStandardArray()
        {
            return new AbilityScores(15, 14, 13, 12, 10, 8);
        }
    }
}
