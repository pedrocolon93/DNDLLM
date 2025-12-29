using UnityEngine;
using DnD.Character;
using DnD.Combat;

namespace DnD.AI
{
    /// <summary>
    /// Command pattern for game actions
    /// Encapsulates player actions parsed from natural language
    /// </summary>
    public interface IGameCommand
    {
        string CommandName { get; }
        string Description { get; }
        void Execute();
        bool CanExecute();
    }

    public class AttackCommand : IGameCommand
    {
        private CharacterStats attacker;
        private CharacterStats target;

        public string CommandName => "Attack";
        public string Description => $"{attacker?.characterName} attacks {target?.characterName}";

        public AttackCommand(CharacterStats attacker, CharacterStats target)
        {
            this.attacker = attacker;
            this.target = target;
        }

        public bool CanExecute()
        {
            return attacker != null && target != null &&
                   attacker.currentHitPoints > 0 && target.currentHitPoints > 0;
        }

        public void Execute()
        {
            if (CanExecute() && CombatManager.Instance != null)
            {
                CombatManager.Instance.ExecuteAttack(attacker, target);
            }
        }
    }

    public class MoveCommand : IGameCommand
    {
        private Transform characterTransform;
        private Vector2 direction;
        private float distance;

        public string CommandName => "Move";
        public string Description => $"Move {direction} by {distance} units";

        public MoveCommand(Transform characterTransform, Vector2 direction, float distance = 1f)
        {
            this.characterTransform = characterTransform;
            this.direction = direction.normalized;
            this.distance = distance;
        }

        public bool CanExecute()
        {
            return characterTransform != null;
        }

        public void Execute()
        {
            if (CanExecute())
            {
                Vector3 newPos = characterTransform.position + (Vector3)(direction * distance);
                characterTransform.position = newPos;
                Debug.Log($"Moved to {newPos}");
            }
        }
    }

    public class UseItemCommand : IGameCommand
    {
        private CharacterStats character;
        private string itemName;

        public string CommandName => "Use Item";
        public string Description => $"{character?.characterName} uses {itemName}";

        public UseItemCommand(CharacterStats character, string itemName)
        {
            this.character = character;
            this.itemName = itemName;
        }

        public bool CanExecute()
        {
            return character != null && !string.IsNullOrEmpty(itemName);
        }

        public void Execute()
        {
            if (CanExecute())
            {
                Debug.Log($"{character.characterName} used {itemName}");
                // TODO: Implement actual item usage
            }
        }
    }

    public class RestCommand : IGameCommand
    {
        private CharacterStats character;

        public string CommandName => "Rest";
        public string Description => $"{character?.characterName} takes a rest";

        public RestCommand(CharacterStats character)
        {
            this.character = character;
        }

        public bool CanExecute()
        {
            return character != null;
        }

        public void Execute()
        {
            if (CanExecute())
            {
                // Short rest: heal some HP
                int healing = character.level * 5;
                character.Heal(healing);
                Debug.Log($"{character.characterName} rested and recovered {healing} HP");
            }
        }
    }

    public class DialogueCommand : IGameCommand
    {
        private string npcName;
        private string dialogue;

        public string CommandName => "Talk";
        public string Description => $"Speak with {npcName}";

        public DialogueCommand(string npcName, string dialogue)
        {
            this.npcName = npcName;
            this.dialogue = dialogue;
        }

        public bool CanExecute()
        {
            return !string.IsNullOrEmpty(npcName);
        }

        public void Execute()
        {
            if (CanExecute())
            {
                Debug.Log($"Talking to {npcName}: {dialogue}");
                // TODO: Implement dialogue system
            }
        }
    }
}
