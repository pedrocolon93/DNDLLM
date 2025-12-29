using System;

namespace DnD.Core
{
    /// <summary>
    /// The six core ability scores in D&D 5e
    /// </summary>
    public enum AbilityScore
    {
        Strength,
        Dexterity,
        Constitution,
        Intelligence,
        Wisdom,
        Charisma
    }

    /// <summary>
    /// Character classes from D&D 5e SRD
    /// </summary>
    public enum CharacterClassName
    {
        Fighter,
        Wizard,
        Rogue,
        Cleric,
        Barbarian,
        Ranger,
        Paladin,
        Monk,
        Bard,
        Druid,
        Warlock,
        Sorcerer
    }

    /// <summary>
    /// Character races from D&D 5e SRD
    /// </summary>
    public enum Race
    {
        Human,
        Elf,
        Dwarf,
        Halfling,
        Dragonborn,
        Gnome,
        HalfElf,
        HalfOrc,
        Tiefling
    }

    /// <summary>
    /// Status conditions using bit flags for efficient storage
    /// </summary>
    [Flags]
    public enum Condition
    {
        None = 0,
        Blinded = 1 << 0,
        Charmed = 1 << 1,
        Deafened = 1 << 2,
        Frightened = 1 << 3,
        Grappled = 1 << 4,
        Incapacitated = 1 << 5,
        Invisible = 1 << 6,
        Paralyzed = 1 << 7,
        Petrified = 1 << 8,
        Poisoned = 1 << 9,
        Prone = 1 << 10,
        Restrained = 1 << 11,
        Stunned = 1 << 12,
        Unconscious = 1 << 13
    }

    /// <summary>
    /// Damage types in D&D 5e
    /// </summary>
    public enum DamageType
    {
        Slashing,
        Piercing,
        Bludgeoning,
        Fire,
        Cold,
        Lightning,
        Thunder,
        Acid,
        Poison,
        Necrotic,
        Radiant,
        Force,
        Psychic
    }

    /// <summary>
    /// Item rarity
    /// </summary>
    public enum Rarity
    {
        Common,
        Uncommon,
        Rare,
        VeryRare,
        Legendary,
        Artifact
    }

    /// <summary>
    /// Armor types
    /// </summary>
    public enum ArmorType
    {
        None,
        Light,
        Medium,
        Heavy,
        Shield
    }

    /// <summary>
    /// Weapon types
    /// </summary>
    public enum WeaponType
    {
        Simple,
        Martial
    }

    /// <summary>
    /// Spell schools
    /// </summary>
    public enum SpellSchool
    {
        Abjuration,
        Conjuration,
        Divination,
        Enchantment,
        Evocation,
        Illusion,
        Necromancy,
        Transmutation
    }

    /// <summary>
    /// Combat states
    /// </summary>
    public enum BattleState
    {
        Start,
        RollInitiative,
        PlayerTurn,
        PlayerAction,
        EnemyTurn,
        EnemyAction,
        BattleWon,
        BattleLost
    }

    /// <summary>
    /// Game states
    /// </summary>
    public enum GameState
    {
        MainMenu,
        CharacterCreation,
        Exploration,
        Combat,
        Dialogue,
        Inventory,
        Rest,
        GameOver
    }
}
