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

        // Player position on the current map (zero on first save → falls back to default start)
        public int playerX;
        public int playerY;

        // Full conversation history
        public List<ChatMessageData> messages = new List<ChatMessageData>();

        // Map tile descriptions (one per unique tile type)
        public List<TileDescriptionEntry> tileDescriptions = new List<TileDescriptionEntry>();

        // Full per-tile grid state (captures EditMapPanel changes and map graph edits)
        public List<TileGridEntry> tileGrid = new List<TileGridEntry>();

        // Enemies / NPCs spawned on the current map (sprite textures persisted alongside as PNGs)
        public List<EntityEntry> entities = new List<EntityEntry>();

        // Multi-player party. When this is empty (legacy single-player save), SaveSystem
        // synthesises a single PlayerSaveEntry from the flat character fields above on
        // load. New saves populate both forms — `players[0]` is the source of truth and
        // the flat fields are written for backwards compatibility with older builds.
        public List<PlayerSaveEntry> players = new List<PlayerSaveEntry>();
        public int currentPlayerIndex = 0;

        // TTS preference (per-slot)
        public bool audioAutoplay = false;
    }

    /// <summary>One party member persisted in a save slot. Sprite textures are saved
    /// alongside as slot_{i}_player_{j}_portrait.png and slot_{i}_player_{j}_token.png.</summary>
    [Serializable]
    public class PlayerSaveEntry
    {
        public string characterName;
        public string raceName;       // Race.ToString()
        public string className;      // CharacterClassName.ToString()
        public string appearanceDescription;
        public string backstory;

        public int level;
        public int maxHP;
        public int currentHP;
        public int armorClass;
        public int str, dex, con, intel, wis, cha;

        // Per-player position on the active map. Default (0,0) means "use the default
        // start cell" — same fallback as the legacy top-level playerX/playerY fields.
        public int gridX;
        public int gridY;
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

    /// <summary>An enemy or NPC on the current map. Sprite is saved separately as slot_{i}_entity_{idx}.png.</summary>
    [Serializable]
    public class EntityEntry
    {
        public string name;
        public int    x, y;
        public int    hp, maxHp, ac;
        public bool   isEnemy;
        public bool   isHidden; // when true the sprite is suppressed until REVEAL_ENTITY flips it
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
