using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using DNDLLM.Services;

namespace DNDLLM.Map
{
    public class MapGenerator : MonoBehaviour
    {
        public static MapGenerator Instance { get; private set; }

        [Header("Map Settings")]
        public int width = 10;
        public int height = 10;
        public float cellSize = 1.0f;

        [System.Serializable]
        public class MapTile
        {
            public int x, y;
            public string type;
            public string description;
            public Texture2D visual;
            public GameObject obj;
        }

        [Header("Visuals")]
        public GameObject tilePrefab; // Optional prefab override

        public MapTile[,] grid;

        private void Awake()
        {
            if (Instance == null) 
            {
                Instance = this;
            }
            else Destroy(gameObject);
        }

        public async void GenerateMap(string keywords)
        {
            Debug.Log($"Generating map with keywords: {keywords}...");
            
            // Cleanup existing
            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }

            // 1. Generate Base Floor Texture (Once)
            string primaryKeyword = string.IsNullOrEmpty(keywords) ? "Grass" : keywords.Split(',')[0].Trim();
            Texture2D floorTexture = await LLMService.Instance.GenerateImage($"Top down 2d rpg map tile of {primaryKeyword} ground");

            // Simple Grid Generation
            grid = new MapTile[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    grid[x, y] = new MapTile 
                    { 
                        x = x, 
                        y = y, 
                        type = "Floor",
                        description = "A patch of ground."
                    };

                    CreateTileVisual(x, y, floorTexture);
                }
            }
            
            // Sprinkle some themed elements
            int poiCount = 3;
            for(int i = 0; i < poiCount; i++)
            {
                int rx = Random.Range(0, width);
                int ry = Random.Range(0, height);
                grid[rx, ry].type = "PointOfInterest";
                grid[rx, ry].description = $"A {(string.IsNullOrEmpty(keywords) ? "mystery" : keywords.Split(',')[0])} mystery spot.";
                
                // Generate Visual for POI
                grid[rx, ry].visual = await LLMService.Instance.GenerateImage($"Top down 2d rpg map tile of {grid[rx, ry].description}");
                
                if (grid[rx, ry].visual != null)
                {
                    UpdateTileVisual(rx, ry, grid[rx, ry].visual);
                }
            }

            // ASCII Map Log
            string mapLog = "Generated Map Structure:\n";
            for (int y = height - 1; y >= 0; y--) // Print top to bottom
            {
                for (int x = 0; x < width; x++)
                {
                    mapLog += grid[x, y].type == "PointOfInterest" ? "[X]" : "[ ]";
                }
                mapLog += "\n";
            }
            Debug.Log(mapLog);

            Debug.Log("Map Generation Completed");
            if (DNDLLM.Core.GameManager.Instance != null)
                DNDLLM.Core.GameManager.Instance.ChangeState(DNDLLM.Core.GameState.CharacterGeneration);
            
            AdjustCamera();
        }

        private void CreateTileVisual(int x, int y, Texture2D texture)
        {
            Vector3 pos = new Vector3(x * cellSize, 0, y * cellSize);
            GameObject tileObj = new GameObject($"Tile_{x}_{y}");
            tileObj.transform.position = pos;
            tileObj.transform.parent = transform;
            
            // Rotate 90 on X to lay flat on ground (since sprites face Z)
            tileObj.transform.rotation = Quaternion.Euler(90, 0, 0);

            SpriteRenderer sr = tileObj.AddComponent<SpriteRenderer>();
            
            if (texture == null)
            {
                 // Create default white texture
                 texture = Texture2D.whiteTexture;
            }

            ApplyTextureToSpriteRenderer(sr, texture);
            grid[x, y].obj = tileObj;
        }

        private void UpdateTileVisual(int x, int y, Texture2D newTexture)
        {
            if (grid[x, y].obj != null)
            {
                SpriteRenderer sr = grid[x, y].obj.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    ApplyTextureToSpriteRenderer(sr, newTexture);
                }
            }
        }

        private void ApplyTextureToSpriteRenderer(SpriteRenderer sr, Texture2D texture)
        {
             // Calculate PPU so the sprite fits exactly into cellSize
             // Texture Width (pixels) / PPU = World Width (units)
             // We want World Width = cellSize
             // So PPU = Texture Width / cellSize
             float ppu = texture.width / cellSize;
             
             Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), ppu);
             sr.sprite = sprite;
        }

        private void AdjustCamera()
        {
            if (Camera.main == null) return;

            // 1. Center Camera
            // Tiles are at 0, 1, 2... (width-1) * cellSize
            // Center is (width-1) * cellSize / 2
            float centerX = (width - 1) * cellSize / 2f;
            float centerZ = (height - 1) * cellSize / 2f;

            Camera.main.transform.position = new Vector3(centerX, 10f, centerZ);
            Camera.main.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // 2. Adjust Orthographic Size to fit grid
            Camera.main.orthographic = true;
            
            float targetHeight = height * cellSize;
            float targetWidth = width * cellSize;

            float screenRatio = (float)Screen.width / (float)Screen.height;
            float targetRatio = targetWidth / targetHeight;

            if (screenRatio >= targetRatio)
            {
                // Screen is wider than map, fit by height
                // Orthographic size is half of vertical size
                Camera.main.orthographicSize = (targetHeight / 2f) + 1f; // +1 padding
            }
            else
            {
                // Screen is narrower than map, fit by width
                float differenceInSize = targetRatio / screenRatio;
                Camera.main.orthographicSize = (targetHeight / 2f * differenceInSize) + 1f;
            }
        }



    }
}
