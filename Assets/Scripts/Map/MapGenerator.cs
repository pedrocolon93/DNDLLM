using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using DNDLLM.Services;

namespace DNDLLM.Map
{
    public enum TileType { Floor, Wall, Door, Chest, EnemySpawn, Exit }

    public class MapGenerator : MonoBehaviour
    {
        public static MapGenerator Instance { get; private set; }

        public event System.Action OnMapReady;

        [Header("Map Settings")]
        public int width  = 7;
        public int height = 7;
        public float cellSize = 1.0f;

        [System.Serializable]
        public class MapTile
        {
            public int x, y;
            public TileType type;
            public string description;
            public Texture2D visual;
            public GameObject obj;
            public bool walkable;
        }

        [Header("Visuals")]
        public GameObject tilePrefab; // Optional prefab override

        public MapTile[,] grid;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(gameObject);
        }

        public async void GenerateMap(string keywords)
        {
            Debug.Log($"[MapGenerator] GenerateMap: {keywords}");

            // Cleanup previous tile objects
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            string theme = string.IsNullOrEmpty(keywords) ? "stone dungeon" : keywords.Split(',')[0].Trim();

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("Planning map layout...");

            // ── 1. Plan tile layout ──────────────────────────────────────────
            grid = new MapTile[width, height];
            GenerateLayout();

            // Log layout to console
            LogLayout();

            // ── 2. Create tile GameObjects with white placeholder ─────────────
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    CreateTileVisual(x, y, Texture2D.whiteTexture);

            // ── 3. Generate the style anchor (one floor tile = visual reference) ─
            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("Generating style anchor...");

            string anchorPrompt = $"Top-down 2D RPG tile: {theme} stone floor, seamlessly tileable. Flat overhead view, painterly game art. No borders, no perspective, no drop shadows.";
            Texture2D styleAnchor = await LLMService.Instance.GenerateImage(anchorPrompt);

            if (styleAnchor == null)
            {
                Debug.LogWarning("[MapGenerator] Style anchor failed; using white fallback.");
                styleAnchor = Texture2D.whiteTexture;
            }
            else
            {
                // Show anchor on all floor tiles while the rest generates
                for (int x = 0; x < width; x++)
                    for (int y = 0; y < height; y++)
                        if (grid[x, y].type == TileType.Floor)
                            UpdateTileVisual(x, y, styleAnchor);
            }

            // ── 4. Generate every tile individually with style anchor + cues ──
            int total = width * height;
            int done  = 0;

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage($"Generating {total} tiles...");

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    string prompt = BuildTilePrompt(theme, grid[x, y].type, x, y);
                    Texture2D tex = await LLMService.Instance.GenerateStyledTile(prompt, styleAnchor);

                    if (tex != null)
                    {
                        grid[x, y].visual = tex;
                        UpdateTileVisual(x, y, tex);
                    }

                    done++;
                    if (done % 5 == 0 || done == total)
                    {
                        if (DnD.UI.ChatUI.Instance != null)
                            DnD.UI.ChatUI.Instance.AddSystemMessage($"Tiles: {done}/{total}");
                    }
                }
            }

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("Map ready.");

            OnMapReady?.Invoke();
            Debug.Log("[MapGenerator] Generation complete.");
            AdjustCamera();
        }

        // ── Layout planning ──────────────────────────────────────────────────

        private void GenerateLayout()
        {
            // Fill with walkable floor
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    grid[x, y] = new MapTile { x = x, y = y, type = TileType.Floor, walkable = true, description = "Stone floor." };

            // Border walls
            for (int x = 0; x < width; x++)
            {
                SetTile(x, 0,          TileType.Wall);
                SetTile(x, height - 1, TileType.Wall);
            }
            for (int y = 0; y < height; y++)
            {
                SetTile(0,         y, TileType.Wall);
                SetTile(width - 1, y, TileType.Wall);
            }

            // Doors at midpoint of each wall edge
            SetTile(width / 2,  0,          TileType.Door);
            SetTile(width / 2,  height - 1, TileType.Door);
            SetTile(0,          height / 2, TileType.Door);
            SetTile(width - 1,  height / 2, TileType.Door);

            // Interior POIs (only valid for grids >= 5x5)
            if (width >= 5 && height >= 5)
            {
                SetTile(2,         2,          TileType.Chest);
                SetTile(width - 3, 2,          TileType.EnemySpawn);
                SetTile(width / 2, height / 2, TileType.Exit);
            }
        }

        private void SetTile(int x, int y, TileType type)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            grid[x, y].type     = type;
            grid[x, y].walkable = type != TileType.Wall;
        }

        // ── Prompt construction ──────────────────────────────────────────────

        private string BuildTilePrompt(string theme, TileType tileType, int x, int y)
        {
            string typeDesc;
            switch (tileType)
            {
                case TileType.Wall:       typeDesc = $"{theme} dungeon wall with rough stone blocks, solid impassable surface"; break;
                case TileType.Door:       typeDesc = $"{theme} dungeon doorway opening, worn stone threshold, passage through wall"; break;
                case TileType.Chest:      typeDesc = $"wooden treasure chest sitting on {theme} stone floor"; break;
                case TileType.EnemySpawn: typeDesc = $"dark monster lair on {theme} stone floor, scattered bones and claws marks"; break;
                case TileType.Exit:       typeDesc = $"glowing magical exit portal on {theme} stone floor, mystical runes"; break;
                default:                  typeDesc = $"{theme} dungeon stone floor, flat ground"; break;
            }

            string connectivity = GetConnectivityCue(x, y);
            return $"Top-down 2D RPG tile: {typeDesc}. Position: {connectivity}. "
                 + "Match the exact art style, color palette, and line weight of the reference image. "
                 + "Seamless edges, flat overhead view, no perspective, no drop shadows, no borders.";
        }

        /// <summary>
        /// Describes which sides of this tile connect to neighbors, based on its grid position.
        /// Corner → 2 connected sides. Edge → 3. Center → 4.
        /// </summary>
        private string GetConnectivityCue(int x, int y)
        {
            bool top    = y < height - 1;
            bool bottom = y > 0;
            bool left   = x > 0;
            bool right  = x < width - 1;
            int  count  = (top ? 1 : 0) + (bottom ? 1 : 0) + (left ? 1 : 0) + (right ? 1 : 0);

            if (count == 4) return "center tile, seamlessly connected on all four sides (top, bottom, left, right)";
            if (count == 0) return "standalone tile";

            var sides = new List<string>();
            if (top)    sides.Add("top");
            if (bottom) sides.Add("bottom");
            if (left)   sides.Add("left");
            if (right)  sides.Add("right");

            if (count == 2 && top   && bottom) return "middle-vertical tile, connected at top and bottom only";
            if (count == 2 && left  && right)  return "middle-horizontal tile, connected at left and right only";
            if (count == 2)                    return $"corner tile, connected at {sides[0]} and {sides[1]}";
            return $"edge tile, connected at {string.Join(", ", sides)}";
        }

        // ── Visuals ──────────────────────────────────────────────────────────

        private void CreateTileVisual(int x, int y, Texture2D texture)
        {
            Vector3 pos = new Vector3(x * cellSize, 0, y * cellSize);
            GameObject tileObj = new GameObject($"Tile_{x}_{y}");
            tileObj.transform.position = pos;
            tileObj.transform.parent   = transform;
            tileObj.transform.rotation = Quaternion.Euler(90, 0, 0);

            SpriteRenderer sr = tileObj.AddComponent<SpriteRenderer>();
            if (texture == null) texture = Texture2D.whiteTexture;
            ApplyTextureToSpriteRenderer(sr, texture);
            grid[x, y].obj = tileObj;
        }

        private void UpdateTileVisual(int x, int y, Texture2D newTexture)
        {
            if (grid[x, y].obj == null) return;
            SpriteRenderer sr = grid[x, y].obj.GetComponent<SpriteRenderer>();
            if (sr != null) ApplyTextureToSpriteRenderer(sr, newTexture);
        }

        private void ApplyTextureToSpriteRenderer(SpriteRenderer sr, Texture2D texture)
        {
            float ppu = texture.width / cellSize;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), ppu);
            sr.sprite = sprite;
        }

        private void AdjustCamera()
        {
            Camera cam = null;
            var mapCamGO = GameObject.Find("MapCamera");
            if (mapCamGO != null) cam = mapCamGO.GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            float centerX = (width  - 1) * cellSize / 2f;
            float centerZ = (height - 1) * cellSize / 2f;
            cam.transform.position = new Vector3(centerX, 10f, centerZ);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.orthographic = true;

            float targetHeight = height * cellSize;
            float targetWidth  = width  * cellSize;

            float rtAspect = (cam.targetTexture != null)
                ? (float)cam.targetTexture.width / cam.targetTexture.height
                : cam.aspect;

            float targetRatio = targetWidth / targetHeight;
            cam.orthographicSize = rtAspect >= targetRatio
                ? (targetHeight / 2f) + 1f
                : (targetHeight / 2f * (targetRatio / rtAspect)) + 1f;
        }

        private void LogLayout()
        {
            string log = "[MapGenerator] Layout (y=top..bottom):\n";
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    switch (grid[x, y].type)
                    {
                        case TileType.Wall:       log += "[W]"; break;
                        case TileType.Door:       log += "[D]"; break;
                        case TileType.Chest:      log += "[C]"; break;
                        case TileType.EnemySpawn: log += "[E]"; break;
                        case TileType.Exit:       log += "[X]"; break;
                        default:                  log += "[ ]"; break;
                    }
                }
                log += "\n";
            }
            Debug.Log(log);
        }
    }
}
