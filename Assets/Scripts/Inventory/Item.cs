using UnityEngine;
using DnD.Core;

namespace DnD.Inventory
{
    /// <summary>
    /// Base ScriptableObject for all items
    /// </summary>
    [CreateAssetMenu(fileName = "New Item", menuName = "DnD/Items/Item")]
    public class Item : ScriptableObject
    {
        [Header("Basic Info")]
        public string itemName;
        [TextArea(2, 5)]
        public string description;
        public Sprite icon;
        public Rarity rarity = Rarity.Common;

        [Header("Properties")]
        public int value = 1; // Gold pieces
        public float weight = 0.1f;
        public bool isStackable = false;
        public int maxStackSize = 1;

        public virtual string GetTooltip()
        {
            return $"<b>{itemName}</b>\n{description}\n\nValue: {value} gp\nWeight: {weight} lbs";
        }

        public virtual void Use()
        {
            Debug.Log($"Used {itemName}");
        }
    }

    /// <summary>
    /// Weapon item
    /// </summary>
    [CreateAssetMenu(fileName = "New Weapon", menuName = "DnD/Items/Weapon")]
    public class Weapon : Item
    {
        [Header("Weapon Stats")]
        public WeaponType weaponType;
        public int damageDiceCount = 1;
        public int damageDie = 6;
        public DamageType damageType = DamageType.Slashing;
        public bool isTwoHanded = false;
        public bool isFinesse = false;
        public int range = 5; // In feet

        public override string GetTooltip()
        {
            return base.GetTooltip() + $"\n\nDamage: {damageDiceCount}d{damageDie} {damageType}\n" +
                   $"Type: {weaponType}\n" +
                   (isTwoHanded ? "Two-Handed\n" : "") +
                   (isFinesse ? "Finesse\n" : "");
        }
    }

    /// <summary>
    /// Armor item
    /// </summary>
    [CreateAssetMenu(fileName = "New Armor", menuName = "DnD/Items/Armor")]
    public class Armor : Item
    {
        [Header("Armor Stats")]
        public ArmorType armorType;
        public int baseArmorClass = 10;
        public bool addDexModifier = true;
        public int maxDexBonus = 10;
        public int strengthRequirement = 0;

        public override string GetTooltip()
        {
            string acText = addDexModifier ? $"{baseArmorClass} + DEX" : $"{baseArmorClass}";
            if (maxDexBonus < 10)
                acText += $" (max {maxDexBonus})";

            return base.GetTooltip() + $"\n\nAC: {acText}\nType: {armorType}\n" +
                   (strengthRequirement > 0 ? $"Str Requirement: {strengthRequirement}\n" : "");
        }
    }

    /// <summary>
    /// Consumable item (potions, scrolls, etc.)
    /// </summary>
    [CreateAssetMenu(fileName = "New Consumable", menuName = "DnD/Items/Consumable")]
    public class Consumable : Item
    {
        [Header("Consumable Properties")]
        public int healingAmount = 0;
        public int durationTurns = 0;
        public Condition conditionToRemove = Condition.None;

        public override string GetTooltip()
        {
            string effects = "";
            if (healingAmount > 0)
                effects += $"\nHeals: {healingAmount} HP";
            if (conditionToRemove != Condition.None)
                effects += $"\nRemoves: {conditionToRemove}";
            if (durationTurns > 0)
                effects += $"\nDuration: {durationTurns} turns";

            return base.GetTooltip() + effects;
        }
    }
}
