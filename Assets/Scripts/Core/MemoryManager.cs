using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace DNDLLM.Core
{
    public class MemoryManager : MonoBehaviour
    {
        public static MemoryManager Instance { get; private set; }

        [System.Serializable]
        public class GameMemory
        {
            public string content;
            public string timestamp;
            public List<string> tags;

            public GameMemory(string content, string timestamp, List<string> tags)
            {
                this.content = content;
                this.timestamp = timestamp;
                this.tags = tags;
            }
        }

        private List<GameMemory> memories = new List<GameMemory>();

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

        public void AddMemory(string content, List<string> tags = null)
        {
            string time = System.DateTime.Now.ToString("HH:mm:ss");
            if (tags == null) tags = new List<string>();
            
            GameMemory newMem = new GameMemory(content, time, tags);
            memories.Add(newMem);
            Debug.Log($"[Memory Stored]: {content}");
        }

        // Simple keyword-based retrieval (Pseudo-RAG)
        public List<string> GetRelevantMemories(string contextQuery, int limit = 5)
        {
            // In a real RAG system, we would generate embeddings for the query and stored memories,
            // then compute cosine similarity.
            // For now, we'll do a simple keyword match + recency.
            
            // Split query into keywords
            var keywords = contextQuery.ToLower().Split(' ');
            
            var scoredMemories = memories.Select(m => new
            {
                Memory = m,
                Score = CalculateRelevanceScore(m, keywords)
            })
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => $"[{x.Memory.timestamp}] {x.Memory.content}")
            .ToList();

            return scoredMemories;
        }

        private int CalculateRelevanceScore(GameMemory memory, string[] keywords)
        {
            int score = 0;
            string lowerContent = memory.content.ToLower();
            
            foreach (var keyword in keywords)
            {
                if (lowerContent.Contains(keyword) || memory.tags.Contains(keyword))
                {
                    score++;
                }
            }
            return score;
        }
    }
}
