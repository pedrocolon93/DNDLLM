using System;
using System.Collections.Generic;
using UnityEngine;
using DnD.Core;

namespace DnD.Data
{
    [Serializable]
    public class SaveData
    {
        // Slot metadata
        public int    slotIndex;
        public string slotLabel;    // "Aric · Fighter · Lv3"
        public string lastPlayed;   // ISO-8601 UTC e.g. "2026-04-19T12:00:00Z"

        // Campaign
        public string campaignSeed;      // player's initial prompt
        public string campaignTimeline;  // DM's generated intro text

        // Character identity
        public string characterName;
        public string raceName;      // Race.ToString()
        public string className;     // CharacterClassName.ToString()
        public string appearanceDescription;
        public string backstory;

        // Character stats
        public int level;
        public int maxHP;
        public int currentHP;
        public int armorClass;
        public int str, dex, con, intel, wis, cha;

        // Game state
        public string gameState;   // GameState.ToString()

        // Full conversation history
        public List<ChatMessageData> messages;
    }

    [Serializable]
    public class ChatMessageData
    {
        public string type;   // "Player" | "DM" | "System"
        public string text;
    }

    // Passed from CharacterCreationPopup to GameManager on completion
    public struct CharacterCreationData
    {
        public string             characterName;
        public Race               race;
        public CharacterClassName characterClass;
        public string             appearanceDescription;
        public string             backstory;
        public Texture2D          portrait;  // null if generation timed out
    }
}
