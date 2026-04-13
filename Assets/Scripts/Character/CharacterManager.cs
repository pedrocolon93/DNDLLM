using UnityEngine;
using System.Threading.Tasks;
using DNDLLM.Services;

namespace DNDLLM.Character
{
    public class CharacterManager : MonoBehaviour
    {
        public static CharacterManager Instance { get; private set; }

        [System.Serializable]
        public class CharacterData
        {
            public string name;
            public string charClass; // "Fighter", "Wizard", etc.
            public int hp;
            public int ac;
            public int str, dex, con, intel, wis, cha;
            public Texture2D visual;
            public string bio;
        }

        public CharacterData playerCharacter;
        public CharacterData currentEnemy;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else Destroy(gameObject);
        }

        public async Task CreatePlayerCharacter(string description)
        {
            Debug.Log($"Creating Character: {description}");
            
            // Mock parsing logic - in real usage, send prompt to LLM to return JSON stats
            playerCharacter = new CharacterData
            {
                name = "Hero",
                charClass = "Fighter",
                hp = 20,
                ac = 16,
                str = 16, dex = 12, con = 14, intel = 10, wis = 10, cha = 12,
                bio = description
            };

            // Generate Visual
            playerCharacter.visual = await LLMService.Instance.GenerateImage($"Fantasy portrait of {description}, D&D style");
            Debug.Log("Player Character Created");
        }

        public async Task CreateEnemy(string description)
        {
            Debug.Log($"Spawning Enemy: {description}");
            
            currentEnemy = new CharacterData
            {
                name = "Goblin",
                charClass = "Monster",
                hp = 10,
                ac = 12,
                str = 8, dex = 14, con = 10, intel = 10, wis = 8, cha = 8,
                bio = description
            };

             // Generate Visual
            currentEnemy.visual = await LLMService.Instance.GenerateImage($"Fantasy monster art of {description}");
            Debug.Log("Enemy Created");
        }
    }
}
