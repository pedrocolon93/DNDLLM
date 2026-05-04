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
        public List<ChatMessageData> messages = new List<ChatMessageData>();

        // Map tile descriptions (one per unique tile type)
        public List<TileDescriptionEntry> tileDescriptions = new List<TileDescriptionEntry>();

        // Full per-tile grid state (captures EditMapPanel changes and map graph edits)
        public List<TileGridEntry> tileGrid = new List<TileGridEntry>();

        // TTS preference (per-slot)
        public bool audioAutoplay = false;
    }

    [Serializable]
    public class ChatMessageData
    {
        public string type;   // "Player" | "DM" | "System"
        public string text;
    }

    [Serializable]
    public class TileDescriptionEntry
    {
        public string tileType;    // TileType.ToString()
        public string description;
    }

    /// <summary>Per-tile state saved for the current active map (captures EditMapPanel changes).</summary>
    [Serializable]
    public class TileGridEntry
    {
        public int    x, y;
        public string tileType;    // TileType.ToString()
        public string description;
    }

    // Passed from CharacterCreationPopup to GameManager on completion
    public struct CharacterCreationData
    {
        public string             characterName;
        public Race               race;
        public CharacterClassName characterClass;
        public string             appearanceDescription;
        public string             backstory;
        /// <summary>Player-allocated ability scores from the creation wizard. Null = use GenerateRandom fallback.</summary>
        public DnD.Character.AbilityScores abilities;
        // Not serialized via JsonUtility — SaveSystem writes/reads this as slot_N_portrait.png
        public Texture2D          portrait;  // null if generation timed out
    }
}
