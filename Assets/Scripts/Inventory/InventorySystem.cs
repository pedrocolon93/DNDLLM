using System;
using System.Collections.Generic;
using UnityEngine;

namespace DnD.Inventory
{
    /// <summary>
    /// Instance of an item in inventory (wraps ScriptableObject)
    /// </summary>
    [Serializable]
    public class ItemInstance
    {
        public Item item;
        public int quantity = 1;
        public int durability = 100; // For weapons/armor

        public ItemInstance(Item item, int quantity = 1)
        {
            this.item = item;
            this.quantity = quantity;
        }
    }

    /// <summary>
    /// ScriptableObject-based inventory system
    /// </summary>
    [CreateAssetMenu(fileName = "New Inventory", menuName = "DnD/Inventory")]
    public class InventorySystem : ScriptableObject
    {
        [SerializeField] private int maxSlots = 20;
        [SerializeField] private List<ItemInstance> items = new List<ItemInstance>();

        public int MaxSlots => maxSlots;
        public List<ItemInstance> Items => items;

        public event Action OnInventoryChanged;

        public bool AddItem(Item item, int quantity = 1)
        {
            // Check if stackable and already exists
            if (item.isStackable)
            {
                ItemInstance existing = items.Find(i => i.item == item);
                if (existing != null)
                {
                    existing.quantity += quantity;
                    OnInventoryChanged?.Invoke();
                    return true;
                }
            }

            // Check if we have room
            if (items.Count >= maxSlots)
            {
                Debug.LogWarning("Inventory full!");
                return false;
            }

            items.Add(new ItemInstance(item, quantity));
            OnInventoryChanged?.Invoke();
            return true;
        }

        public bool RemoveItem(Item item, int quantity = 1)
        {
            ItemInstance instance = items.Find(i => i.item == item);
            if (instance == null)
                return false;

            instance.quantity -= quantity;
            if (instance.quantity <= 0)
            {
                items.Remove(instance);
            }

            OnInventoryChanged?.Invoke();
            return true;
        }

        public bool HasItem(Item item, int quantity = 1)
        {
            ItemInstance instance = items.Find(i => i.item == item);
            return instance != null && instance.quantity >= quantity;
        }

        public int GetItemCount(Item item)
        {
            ItemInstance instance = items.Find(i => i.item == item);
            return instance?.quantity ?? 0;
        }

        public void Clear()
        {
            items.Clear();
            OnInventoryChanged?.Invoke();
        }

        public float GetTotalWeight()
        {
            float total = 0;
            foreach (var instance in items)
            {
                total += instance.item.weight * instance.quantity;
            }
            return total;
        }

        public int GetTotalValue()
        {
            int total = 0;
            foreach (var instance in items)
            {
                total += instance.item.value * instance.quantity;
            }
            return total;
        }
    }
}
