using UnityEngine;
using System.Collections;
using System;

namespace DNDLLM.Core
{
    public enum GameState
    {
        Setup,
        StoryGeneration,
        MapGeneration,
        CharacterGeneration,
        GameplayLoop,
        GameOver
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState CurrentState { get; private set; }

        // Events for state changes
        public event Action<GameState> OnStateChanged;

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

        private void Start()
        {
            ChangeState(GameState.Setup);
        }

        public string GeneratedMapKeywords { get; set; }

        public void ChangeState(GameState newState)
        {
            CurrentState = newState;
            Debug.Log($"Game State Changed to: {newState}");
            OnStateChanged?.Invoke(newState);

            switch (newState)
            {
                case GameState.Setup:
                    StartCoroutine(SetupGame());
                    break;
                case GameState.StoryGeneration:
                    // StoryEngine is triggered by UI input manually for now.
                    break;
                case GameState.MapGeneration:
                    // Trigger Map Generation
                    string keywords = string.IsNullOrEmpty(GeneratedMapKeywords) ? "Dungeon, Dark" : GeneratedMapKeywords;
                    if (DNDLLM.Map.MapGenerator.Instance != null)
                    {
                        DNDLLM.Map.MapGenerator.Instance.GenerateMap(keywords); 
                        if (Camera.main != null)
                            Debug.Log($"Main Camera is at {Camera.main.transform.position}. Ensure it looks at (0,0,0) to see the map.");
                        else
                            Debug.LogError("Main Camera not found! You won't see anything.");
                    }
                    break;
                case GameState.CharacterGeneration:
                    if (DNDLLM.Character.CharacterManager.Instance != null)
                    {
                        // Mock: automatically create a player and enemy for the prototype
                        _ = DNDLLM.Character.CharacterManager.Instance.CreatePlayerCharacter("A brave warrior");
                        _ = DNDLLM.Character.CharacterManager.Instance.CreateEnemy("A slime monster");
                        ChangeState(GameState.GameplayLoop);
                    }
                    break;
                case GameState.GameplayLoop:
                    Debug.Log("Gameplay Loop Started! Type actions like 'attack goblin'.");
                    break;
            }
        }

        private IEnumerator SetupGame()
        {
            Debug.Log("Initializing game systems...");
            // Simulate initialization time
            yield return new WaitForSeconds(1f);
            
            // Should transition to StoryGeneration after UI input, but for now just log ready
            Debug.Log("Game Ready. Waiting for Player Input to start story.");
        }

        // Helper to start the flow from UI
        public void StartNewGame(string initialPrompt)
        {
            // Store prompt in StoryEngine (to be implemented)
            ChangeState(GameState.StoryGeneration);
        }
    }
}
