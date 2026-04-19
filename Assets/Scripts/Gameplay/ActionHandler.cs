using UnityEngine;
using System.Threading.Tasks;
using DNDLLM.Services;
using DNDLLM.Character;
using DNDLLM.Utils;

namespace DNDLLM.Gameplay
{
    public class ActionHandler : MonoBehaviour
    {
        public static ActionHandler Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else Destroy(gameObject);
        }

        public async Task ProcessPlayerAction(string input)
        {
            Debug.Log($"Processing Action: {input}");
            
            // 1. Log to Memory
            MemoryManager.Instance.AddMemory($"Player Input: {input}");

            // 2. Validate/Parse with LLM
            string systemPrompt = @"You are a D&D DM. Parse the user's action into a JSON-like format: { ""actionType"": ""Attack"" | ""Move"" | ""Talk"" | ""Explore"", ""target"": ""TargetName"", ""stat"": ""STR|DEX|etc"" }
            If the action is impossible, return { ""actionType"": ""Invalid"", ""reason"": ""Why"" }";
            
            string parsedJson = await LLMService.Instance.SendPrompt(systemPrompt, input);
            Debug.Log($"Parsed Action: {parsedJson}");

            // Mock parsing for prototype
            if (input.ToLower().Contains("attack"))
            {
                ExecuteAttack();
            }
            else if (input.ToLower().Contains("look"))
            {
                // Describe surroundings
            }
            else
            {
                Debug.Log("Generic action taken.");
            }
        }

        private void ExecuteAttack()
        {
            // Simple combat logic
            int attackRoll = DiceRoller.RollCheck(CharacterManager.Instance.playerCharacter.str); // using STR bonus mock
            int ac = CharacterManager.Instance.currentEnemy != null ? CharacterManager.Instance.currentEnemy.ac : 10;

            if (attackRoll >= ac)
            {
                Debug.Log($"Hit! Rolled {attackRoll} vs AC {ac}");
                // Roll Damage
                var damage = DiceRoller.Roll(8, 1); // Mock 1d8 damage
                if (CharacterManager.Instance.currentEnemy != null)
                {
                    CharacterManager.Instance.currentEnemy.hp -= damage.total;
                    Debug.Log($"Dealt {damage.total} damage. Enemy HP: {CharacterManager.Instance.currentEnemy.hp}");
                    
                    MemoryManager.Instance.AddMemory($"Player attacked and hit for {damage.total} damage.");
                    
                    if (CharacterManager.Instance.currentEnemy.hp <= 0)
                    {
                        Debug.Log("Enemy Defeated!");
                        MemoryManager.Instance.AddMemory("Enemy defeated.");
                    }
                }
            }
            else
            {
                Debug.Log($"Miss! Rolled {attackRoll} vs AC {ac}");
                MemoryManager.Instance.AddMemory("Player attacked and missed.");
            }
        }
    }
}
