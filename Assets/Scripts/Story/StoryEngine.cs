using UnityEngine;
using System.Collections;
using System.Threading.Tasks;
using DNDLLM.Core;
using DNDLLM.Services;

namespace DNDLLM.Story
{
    public class StoryEngine : MonoBehaviour
    {
        public static StoryEngine Instance { get; private set; }

        [SerializeField] private string currentStoryContext;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public async void GenerateStoryIntroduction(string userPrompt)
        {
            Debug.Log($"Generating story for: {userPrompt}");
            
            // 1. Generate Narrative
            string systemPrompt = "You are a Dungeon Master. Create a short intro for a D&D adventure based on the user's idea. Keep it under 100 words. return only the story text.";
            string storyIntro = await LLMService.Instance.SendPrompt(systemPrompt, userPrompt);
            
            currentStoryContext = storyIntro;
            MemoryManager.Instance.AddMemory($"Story Start: {storyIntro}");
            
            Debug.Log($"Story Intro: {storyIntro}");

            // 2. Extract Keywords for Map
            string keywords = await LLMService.Instance.SendPrompt(
                "You are a map generator helper.",
                "Based on the following story, list 3 keywords describing the environment (e.g., Forest, Cave, Desert). Return only the keywords separated by commas.\n\n" + storyIntro);
            
            Debug.Log($"Map Keywords: {keywords}");

            // 3. Transition to Map Generation (legacy GameManager removed; wiring handled by Task 7)
            Debug.Log($"[StoryEngine] Map keywords ready: {keywords}");
        }
    }
}
