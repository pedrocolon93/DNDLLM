using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using DNDLLM.Services;

namespace DNDLLM.Map
{
    public enum TileType { Floor, Wall, Door, Chest, EnemySpawn, Exit, House, Inn, Market, Fountain, NpcSpawn }

    public class MapGenerator : MonoBehaviour
    {
        public static MapGenerator Instance { get; private set; }

        public event System.Action OnMapReady;

        /// <summary>The floor tile used as the visual style reference for all other tile types.</summary>
        public Texture2D StyleAnchor { get; internal set; }

        /// <summary>Returns the world-space centre of the given grid cell.</summary>
        public Vector3 GetTileWorldPos(int x, int y) => new Vector3(x * cellSize, 0f, y * cellSize);

        [Header("Map Settings")]
        public int width  = 7;
        public int height = 7;
        public float cellSize = 1.0f;

        [Header("Strategy D — holistic map")]
        [Tooltip("Use the holistic-paint pipeline (one big LLM image + evaluate/refine loop) instead of per-tile generation.")]
        public bool useStrategyD = true;
        [Tooltip("Number of evaluate→refine iterations before accepting the map.")]
        public int refinementRounds = 2;
        [Tooltip("If true, the holistic prompt explicitly mentions a tile grid (stricter placement, more visible seams). False = seamless painting.")]
        public bool strategyDGridMode = false;
        public string LastTheme => _lastTheme;
        private string _lastTheme = "stone dungeon";
        /// <summary>Set to true before calling GenerateMap to skip the LLM description call (use saved descriptions instead).</summary>
        public bool SkipDescriptionGeneration { get; set; } = false;

        /// <summary>
        /// Optional context string set by the caller before GenerateMap.
        /// Included in the tile description prompt so the LLM understands what the
        /// parent location was (e.g. "came from a forest village with torch-lit streets").
        /// Cleared automatically after each generation.
        /// </summary>
        public string StartingContext { get; set; } = "";

        /// <summary>
        /// Feature nouns (e.g. "tavern", "armory", "well") that the LLM must place on the
        /// logical grid. Set by GameManager before generation from CampaignPlan.keyLocations.
        /// Empty / null leaves the LLM free to invent its own.
        /// </summary>
        public System.Collections.Generic.List<string> KeyFeatures { get; set; }

        /// <summary>
        /// Style description built once per map from the theme.
        /// Prepended to every tile prompt so DALL-E (which can't receive image references)
        /// stays on the same flat 2D top-down style across all tiles.
        /// </summary>
        private string _styleSummary = "";

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

        /// <summary>Stored map state for sub-map navigation (push/pop).</summary>
        public class MapSnapshot
        {
            public string       lastTheme;
            public int          width, height;
            public TileType[,]  tileTypes;
            public string[,]    tileDescs;
            public Texture2D[,] tileVisuals;
            public bool[,]      walkable;
            public int          playerX, playerY;
            public Texture2D    styleAnchor;
            // Strategy D: holistic background image (overrides per-tile visuals when set).
            public Texture2D    backgroundImage;
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

        // Strict perspective lock prepended to every tile prompt.
        // DALL-E 3 ignores the style-anchor image, so all style guidance must come from text.
        private const string PERSPECTIVE_LOCK =
            "STRICTLY FLAT 2D TOP-DOWN TILE — camera points straight down like looking at a table. "
          + "ZERO isometric angle. ZERO 3D perspective. ZERO building sides visible. "
          + "Classic SNES/GBA RPG overhead view (Zelda: A Link to the Past, Final Fantasy 6). ";

        public async void GenerateMap(string keywords)
        {
            if (useStrategyD)
                await GenerateMapStrategyDAsync(keywords);
            else
                await GenerateMapPerTileAsync(keywords);
        }

        /// <summary>
        /// Skip the LLM holistic-paint pipeline and reuse a previously saved background +
        /// grid state. Mirrors the post-paint phases of GenerateMapStrategyDAsync (background
        /// sprite, OnMapReady, camera) so callers get the same observable behaviour.
        /// </summary>
        public void RehydrateFromSavedState(
            Texture2D backgroundTex,
            string keywords,
            IList<DnD.Data.TileGridEntry> savedTiles)
        {
            if (backgroundTex == null)
            {
                Debug.LogWarning("[MapGenerator] RehydrateFromSavedState called with null background; aborting.");
                return;
            }

            Debug.Log($"[MapGenerator] Rehydrate from save: {keywords} ({savedTiles?.Count ?? 0} tiles)");
            foreach (Transform child in transform) Destroy(child.gameObject);

            _lastTheme = string.IsNullOrEmpty(keywords) ? "dungeon" : keywords.Split(',')[0].Trim();

            // Hydrate grid[,] directly from the save (skipping the per-cell overlay loop in
            // OnMapReadyNarrate; that loop becomes a no-op when the grid is already correct).
            grid = new MapTile[width, height];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = new MapTile { x = x, y = y, type = TileType.Floor, walkable = true, description = "" };

            if (savedTiles != null)
            {
                foreach (var entry in savedTiles)
                {
                    if (entry.x < 0 || entry.x >= width || entry.y < 0 || entry.y >= height) continue;
                    if (System.Enum.TryParse<TileType>(entry.tileType, out TileType t))
                    {
                        grid[entry.x, entry.y].type     = t;
                        grid[entry.x, entry.y].walkable = t == TileType.Floor || t == TileType.Exit
                                                       || t == TileType.Door  || t == TileType.NpcSpawn;
                    }
                    grid[entry.x, entry.y].description = entry.description ?? "";
                }
            }

            StyleAnchor = backgroundTex;
            CreateBigBackgroundSprite(backgroundTex);
            LogLayout();

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("[Strategy D] Map restored from save.");

            OnMapReady?.Invoke();
            AdjustCamera();
        }

        // ── Strategy D: holistic paint + evaluate/refine ────────────────────

        private async Task GenerateMapStrategyDAsync(string keywords)
        {
            Debug.Log($"[MapGenerator-D] GenerateMap: {keywords}");
            using var _busy = DNDLLM.Services.BusyIndicator.Show("Painting map…");

            foreach (Transform child in transform) Destroy(child.gameObject);

            int size = Mathf.Min(width, height);
            if (width != height)
            {
                Debug.LogWarning($"[MapGenerator-D] Strategy D requires a square grid; coercing to {size}x{size}.");
                width = height = size;
            }

            string story = string.IsNullOrEmpty(keywords)
                ? "A mysterious dungeon explored by adventurers."
                : keywords;
            _lastTheme = story.Split(',')[0].Trim();

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage($"[Strategy D] Planning {size}x{size} logical grid...");

            // Phase 1 — logical grid via LLM (KeyFeatures from CampaignPlan, when set)
            LogicalGrid logical = await LLMService.Instance.GenerateLogicalGridAsync(size, story, KeyFeatures);
            if (logical == null || logical.tiles == null || logical.tiles.Count == 0)
            {
                Debug.LogError("[MapGenerator-D] Logical grid generation failed; falling back to per-tile generator.");
                await GenerateMapPerTileAsync(keywords);
                return;
            }

            // Hydrate grid[,] from the logical grid (drives walkability, descriptions, spawns)
            grid = new MapTile[width, height];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var lt = logical.GetTile(x, y)
                       ?? new LogicalTile { x = x, y = y, terrain_type = "floor", description = "" };
                bool walk = !LogicalGrid.IsBlockingTerrain(lt.terrain_type);
                TileType type = walk ? TileType.Floor : TileType.Wall;
                if (!string.IsNullOrEmpty(lt.feature)) type = FeatureToTileType(lt.feature);
                string desc = string.IsNullOrEmpty(lt.feature) ? lt.description : $"{lt.feature}: {lt.description}";
                grid[x, y] = new MapTile { x = x, y = y, type = type, walkable = walk, description = desc };
            }

            // Phase 2 — holistic base map
            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("[Strategy D] Painting holistic map (30-60s)...");

            Texture2D current = await LLMService.Instance.GenerateHolisticMapAsync(logical, strategyDGridMode);
            if (current == null)
            {
                Debug.LogError("[MapGenerator-D] Holistic map generation failed.");
                if (DnD.UI.ChatUI.Instance != null)
                    DnD.UI.ChatUI.Instance.AddSystemMessage("[Strategy D] Map paint failed; aborting.");
                return;
            }

            // Phase 3 — evaluate / refine loop
            for (int round = 1; round <= refinementRounds; round++)
            {
                if (DnD.UI.ChatUI.Instance != null)
                    DnD.UI.ChatUI.Instance.AddSystemMessage($"[Strategy D] Refinement round {round}/{refinementRounds}...");

                string feedback = await LLMService.Instance.EvaluateMapImageAsync(current, logical);
                if (string.IsNullOrEmpty(feedback)) break;

                if (feedback.Trim().ToUpperInvariant().StartsWith("PERFECT"))
                {
                    if (DnD.UI.ChatUI.Instance != null)
                        DnD.UI.ChatUI.Instance.AddSystemMessage($"[Strategy D] Evaluator says PERFECT after {round - 1} round(s).");
                    break;
                }

                Texture2D refined = await LLMService.Instance.RefineMapImageAsync(current, feedback);
                if (refined != null) current = refined;
            }

            // Phase 4 — bake the grid overlay into the texture
            Texture2D withGrid = MapImageOverlay.DrawGridOverlay(current, size);
            StyleAnchor = withGrid; // exposes the final image for debug / save

            // Phase 5 — single background sprite spanning the whole grid
            CreateBigBackgroundSprite(withGrid);

            LogLayout();

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("[Strategy D] Map ready.");

            OnMapReady?.Invoke();
            AdjustCamera();
        }

        private void CreateBigBackgroundSprite(Texture2D tex)
        {
            var go = new GameObject("MapBackground");
            go.transform.parent   = transform;
            // Centered so tile (0,0) world-pos lands on the image's (0,0) corner cell.
            go.transform.position = new Vector3((width  - 1) * cellSize / 2f, 0f, (height - 1) * cellSize / 2f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 0;
            float worldW = width * cellSize;
            float ppu    = tex.width / worldW;
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
        }

        /// <summary>Maps a free-text feature noun from the logical grid to one of the gameplay TileTypes.</summary>
        private static TileType FeatureToTileType(string feature)
        {
            string f = (feature ?? "").ToLowerInvariant();
            if (f.Contains("inn")     || f.Contains("tavern"))                  return TileType.Inn;
            if (f.Contains("market")  || f.Contains("stall") || f.Contains("shop")) return TileType.Market;
            if (f.Contains("fountain")|| f.Contains("well"))                    return TileType.Fountain;
            if (f.Contains("door")    || f.Contains("gate"))                    return TileType.Door;
            if (f.Contains("chest")   || f.Contains("treasure"))                return TileType.Chest;
            if (f.Contains("monster") || f.Contains("lair") || f.Contains("enemy")) return TileType.EnemySpawn;
            if (f.Contains("portal")  || f.Contains("exit") || f.Contains("stair")) return TileType.Exit;
            if (f.Contains("npc")     || f.Contains("merchant") || f.Contains("guard")) return TileType.NpcSpawn;
            if (f.Contains("house")   || f.Contains("hut") || f.Contains("home") || f.Contains("monastery") || f.Contains("armory")) return TileType.House;
            return TileType.Floor;
        }

        // ── Per-tile path (legacy) ──────────────────────────────────────────

        private async Task GenerateMapPerTileAsync(string keywords)
        {
            Debug.Log($"[MapGenerator] GenerateMap: {keywords}");
            using var _busy = DNDLLM.Services.BusyIndicator.Show("Generating map tiles…");

            // Cleanup previous tile objects
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            string theme = string.IsNullOrEmpty(keywords) ? "stone dungeon" : keywords.Split(',')[0].Trim();
            _lastTheme = theme;

            // Build style summary once per map — injected into every tile prompt for visual coherence.
            _styleSummary = IsTownTheme(theme)
                ? $"Flat 2D top-down RPG tileset. {theme} setting. "
                + "Warm earthy cobblestone and stone palette. Consistent SNES/GBA RPG art style. "
                + "Seamlessly tileable edges."
                : $"Flat 2D top-down RPG tileset. {theme} setting. "
                + "Muted stone and earth palette. Consistent SNES/GBA RPG art style. "
                + "Seamlessly tileable edges.";

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("Planning map layout...");

            // ── 1. Plan tile layout ──────────────────────────────────────────
            grid = new MapTile[width, height];
            if (IsTownTheme(theme))
                GenerateTownLayout();
            else
                GenerateLayout();

            // Log layout to console
            LogLayout();

            // ── 2. Create tile GameObjects with white placeholder ─────────────
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    CreateTileVisual(x, y, Texture2D.whiteTexture);

            // ── 3. Generate the starting tile — this dictates the art style ─
            int startX = width  / 2;
            int startY = 1; // row above the bottom wall — player's entrance

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("Generating starting tile...");

            string anchorPrompt = PERSPECTIVE_LOCK + _styleSummary + (IsTownTheme(theme)
                ? $" Square 1:1 tile: {theme} cobblestone floor, flat stone pavement pattern, neutral midground tone. Seamless edges. This tile will be the style reference for all other tiles in the map."
                : $" Square 1:1 tile: {theme} stone floor, flat ground texture, neutral midground tone. Seamless edges. This tile will be the style reference for all other tiles in the map.");
            Texture2D styleAnchor = await LLMService.Instance.GenerateImage(anchorPrompt);

            if (styleAnchor == null)
            {
                Debug.LogWarning("[MapGenerator] Style anchor failed; using white fallback.");
                styleAnchor = Texture2D.whiteTexture;
            }
            StyleAnchor = styleAnchor;
            grid[startX, startY].visual = styleAnchor;
            UpdateTileVisual(startX, startY, styleAnchor);

            // ── 4. Generate all remaining tiles in parallel ───────────────
            // All tasks are started on the main thread (Unity requires it for web requests),
            // then we await Task.WhenAll so they run concurrently rather than sequentially.
            int   total       = width * height - 1; // minus the starting tile already done
            int[] doneCounter = { 0 };              // int[] so async methods can capture+mutate it

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage($"Generating {total} tiles in parallel...");

            var tileTasks = new List<Task>();
            for (int tx = 0; tx < width; tx++)
            for (int ty = 0; ty < height; ty++)
            {
                if (tx == startX && ty == startY) continue;
                int cx = tx, cy = ty; // capture loop vars
                tileTasks.Add(GenerateSingleTileAsync(cx, cy, theme, styleAnchor, doneCounter, total));
            }
            await Task.WhenAll(tileTasks);

            if (!SkipDescriptionGeneration)
            {
                if (DnD.UI.ChatUI.Instance != null)
                    DnD.UI.ChatUI.Instance.AddSystemMessage("Generating tile descriptions...");
                await GenerateTileDescriptionsAsync(theme);
            }
            SkipDescriptionGeneration = false; // reset for next call

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("Map ready.");

            OnMapReady?.Invoke();
            Debug.Log("[MapGenerator] Generation complete.");
            AdjustCamera();
        }

        // ── Layout planning ──────────────────────────────────────────────────

        // Returns true if (x,y) is a corner of the grid
        private bool IsCorner(int x, int y) =>
            (x == 0 || x == width - 1) && (y == 0 || y == height - 1);

        private void GenerateLayout()
        {
            // Fill with walkable floor
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    grid[x, y] = new MapTile { x = x, y = y, type = TileType.Floor, walkable = true, description = "Stone floor." };

            // Border walls — skip corner cells so they remain floor
            for (int x = 0; x < width; x++)
            {
                if (!IsCorner(x, 0))          SetTile(x, 0,          TileType.Wall);
                if (!IsCorner(x, height - 1)) SetTile(x, height - 1, TileType.Wall);
            }
            for (int y = 0; y < height; y++)
            {
                if (!IsCorner(0,         y)) SetTile(0,         y, TileType.Wall);
                if (!IsCorner(width - 1, y)) SetTile(width - 1, y, TileType.Wall);
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

        private void GenerateTownLayout()
        {
            // Fill with cobblestone floor
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    grid[x, y] = new MapTile { x = x, y = y, type = TileType.Floor, walkable = true, description = "Cobblestone street." };

            // Town walls / fences — skip corner cells so they remain floor
            for (int x = 0; x < width; x++)
            {
                if (!IsCorner(x, 0))          SetTile(x, 0,          TileType.Wall);
                if (!IsCorner(x, height - 1)) SetTile(x, height - 1, TileType.Wall);
            }
            for (int y = 0; y < height; y++)
            {
                if (!IsCorner(0,         y)) SetTile(0,         y, TileType.Wall);
                if (!IsCorner(width - 1, y)) SetTile(width - 1, y, TileType.Wall);
            }

            // Town gates at cardinal midpoints
            SetTile(width / 2,  0,          TileType.Door);
            SetTile(width / 2,  height - 1, TileType.Door);
            SetTile(0,          height / 2, TileType.Door);
            SetTile(width - 1,  height / 2, TileType.Door);

            if (width >= 5 && height >= 5)
            {
                SetTile(1,         1,          TileType.House);
                SetTile(width - 2, 1,          TileType.House);
                SetTile(1,         height - 2, TileType.Inn);
                SetTile(2,         2,          TileType.Market);
                SetTile(width - 3, 2,          TileType.NpcSpawn);
                SetTile(width / 2, height / 2, TileType.Fountain);
            }
        }

        private static bool IsTownTheme(string theme)
        {
            string t = (theme ?? "").ToLower();
            return t.Contains("town") || t.Contains("village") || t.Contains("city")
                || t.Contains("settlement") || t.Contains("market") || t.Contains("inn");
        }

        private void SetTile(int x, int y, TileType type)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            grid[x, y].type     = type;
            // Walkable: passable terrain only
            grid[x, y].walkable = type == TileType.Floor    || type == TileType.Exit
                                || type == TileType.Door     || type == TileType.NpcSpawn;
        }

        // ── Prompt construction ──────────────────────────────────────────────

        private string BuildTilePrompt(string theme, TileType tileType, int x, int y)
        {
            bool isCornerWall = tileType == TileType.Wall
                && (x == 0 || x == width - 1) && (y == 0 || y == height - 1);

            string typeDesc;
            switch (tileType)
            {
                case TileType.Wall:
                    typeDesc = isCornerWall
                        ? $"{theme} corner wall block — solid stone where two outer walls meet at a right angle, completely filled from edge to edge, no gaps, no openings, no transparency"
                        : $"{theme} straight outer wall, solid stone blocks, completely filled, no openings";
                    break;
                case TileType.Door:       typeDesc = $"{theme} doorway opening, worn stone threshold, passage leading through the wall"; break;
                case TileType.Chest:      typeDesc = $"wooden treasure chest sitting on {theme} stone floor"; break;
                case TileType.EnemySpawn: typeDesc = $"dark monster lair on {theme} stone floor, scattered bones and claw marks"; break;
                case TileType.Exit:       typeDesc = $"glowing magical exit portal on {theme} stone floor, mystical runes"; break;
                case TileType.House:      typeDesc = $"small stone house on {theme} cobblestone street, tiled roof, wooden door, window"; break;
                case TileType.Inn:        typeDesc = $"cozy inn building on {theme} street, hanging lantern sign, warm candlelit window, welcoming entrance"; break;
                case TileType.Market:     typeDesc = $"market stall on {theme} cobblestone street, colorful awning, goods on display, merchant wares"; break;
                case TileType.Fountain:   typeDesc = $"stone fountain in {theme} town square, flowing water, carved stonework, decorative basin"; break;
                case TileType.NpcSpawn:   typeDesc = $"friendly meeting spot on {theme} cobblestone, warm ambient lighting, inviting area"; break;
                default:                  typeDesc = IsTownTheme(theme)
                                              ? $"{theme} cobblestone street, flat paved ground"
                                              : $"{theme} dungeon stone floor, flat ground";
                    break;
            }

            // Wall tiles must not use a connectivity cue — it causes edge-blending that creates black corners
            bool skipConnectivity = tileType == TileType.Wall;
            string connectivity   = skipConnectivity ? "solid border tile" : GetConnectivityCue(x, y);

            // PERSPECTIVE_LOCK and _styleSummary ensure visual coherence across tiles even when
            // DALL-E 3 cannot receive the style-anchor image as a visual reference.
            // "Use the art style of the seed image" tells the model to match the anchor's palette/brushwork.
            return PERSPECTIVE_LOCK + _styleSummary
                 + $" Square 1:1 tile. CONTENT: {typeDesc}. POSITION: {connectivity}. "
                 + "Use the exact art style, color palette, and brushwork of the seed/reference tile. "
                 + "Seamless edges, no perspective, no drop shadows, no borders, no extra UI elements.";
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

        /// <summary>
        /// Generates a single tile's visual, updates the grid and renderer, then logs progress.
        /// Designed to be fired without await so multiple tiles generate concurrently.
        /// doneCounter[0] is incremented atomically using Interlocked; int[] avoids ref-on-async limitation.
        /// </summary>
        private async Task GenerateSingleTileAsync(int x, int y, string theme, Texture2D styleAnchor,
            int[] doneCounter, int total)
        {
            string prompt = BuildTilePrompt(theme, grid[x, y].type, x, y);
            Texture2D tex = await LLMService.Instance.GenerateStyledTile(prompt, styleAnchor);
            if (tex == null) tex = Texture2D.whiteTexture;
            grid[x, y].visual = tex;
            UpdateTileVisual(x, y, tex);
            int current = System.Threading.Interlocked.Increment(ref doneCounter[0]);
            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage($"Tiles: {current}/{total}");
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
                        case TileType.House:      log += "[H]"; break;
                        case TileType.Inn:        log += "[I]"; break;
                        case TileType.Market:     log += "[M]"; break;
                        case TileType.Fountain:   log += "[F]"; break;
                        case TileType.NpcSpawn:   log += "[N]"; break;
                        default:                  log += "[ ]"; break;
                    }
                }
                log += "\n";
            }
            Debug.Log(log);
        }

        // ── Tile descriptions ────────────────────────────────────────────────

        private async System.Threading.Tasks.Task GenerateTileDescriptionsAsync(string theme)
        {
            if (LLMService.Instance == null) return;

            string sys = "You are a D&D dungeon designer. Reply only with the exact format requested, no extra text.";
            string usr;

            // Include parent-region context so descriptions feel connected to what came before
            string ctxLine = string.IsNullOrEmpty(StartingContext)
                ? ""
                : $"\nContext: This area is entered from: {StartingContext}\n";
            StartingContext = ""; // consume — reset for next call

            if (IsTownTheme(theme))
            {
                usr = $"For a {theme} town, write one evocative sentence describing each location from the player's perspective.{ctxLine}\n"
                    + "Use exactly this format (one line per type):\n"
                    + "FLOOR: <sentence>\n"
                    + "WALL: <sentence>\n"
                    + "DOOR: <sentence>\n"
                    + "HOUSE: <sentence>\n"
                    + "INN: <sentence>\n"
                    + "MARKET: <sentence>\n"
                    + "FOUNTAIN: <sentence>\n"
                    + "NPC_SPAWN: <sentence>";
            }
            else
            {
                usr = $"For a {theme} dungeon, write one evocative sentence describing each tile from the player's perspective.{ctxLine}\n"
                    + "Use exactly this format (one line per type):\n"
                    + "FLOOR: <sentence>\n"
                    + "WALL: <sentence>\n"
                    + "DOOR: <sentence>\n"
                    + "CHEST: <sentence>\n"
                    + "ENEMY_SPAWN: <sentence>\n"
                    + "EXIT: <sentence>";
            }

            string raw = await LLMService.Instance.SendPrompt(sys, usr);
            if (string.IsNullOrEmpty(raw)) return;

            var map = new Dictionary<TileType, string>();
            foreach (string line in raw.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key  = line.Substring(0, colon).Trim().ToUpper();
                string desc = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "FLOOR":       map[TileType.Floor]      = desc; break;
                    case "WALL":        map[TileType.Wall]        = desc; break;
                    case "DOOR":        map[TileType.Door]        = desc; break;
                    case "CHEST":       map[TileType.Chest]       = desc; break;
                    case "ENEMY_SPAWN": map[TileType.EnemySpawn]  = desc; break;
                    case "EXIT":        map[TileType.Exit]        = desc; break;
                    case "HOUSE":       map[TileType.House]       = desc; break;
                    case "INN":         map[TileType.Inn]         = desc; break;
                    case "MARKET":      map[TileType.Market]      = desc; break;
                    case "FOUNTAIN":    map[TileType.Fountain]    = desc; break;
                    case "NPC_SPAWN":   map[TileType.NpcSpawn]    = desc; break;
                }
            }

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (map.TryGetValue(grid[x, y].type, out string d))
                        grid[x, y].description = d;

            Debug.Log($"[MapGenerator] Tile descriptions populated for {map.Count} types.");
        }

        /// <summary>Returns one entry per unique tile type that has a generated description.</summary>
        public List<DnD.Data.TileDescriptionEntry> GetTileDescriptions()
        {
            var result = new List<DnD.Data.TileDescriptionEntry>();
            if (grid == null) return result;
            var seen = new HashSet<TileType>();
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    var tile = grid[x, y];
                    if (seen.Contains(tile.type)) continue;
                    if (!string.IsNullOrEmpty(tile.description)
                        && tile.description != "Stone floor."
                        && tile.description != "Cobblestone street.")
                    {
                        result.Add(new DnD.Data.TileDescriptionEntry
                            { tileType = tile.type.ToString(), description = tile.description });
                        seen.Add(tile.type);
                    }
                }
            return result;
        }

        /// <summary>Stamps grid tile descriptions from a saved list (call after map generates on load).</summary>
        public void LoadTileDescriptions(List<DnD.Data.TileDescriptionEntry> entries)
        {
            if (entries == null || grid == null) return;
            var map = new Dictionary<TileType, string>();
            foreach (var e in entries)
                if (System.Enum.TryParse<TileType>(e.tileType, out TileType t))
                    map[t] = e.description;
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (map.TryGetValue(grid[x, y].type, out string d))
                        grid[x, y].description = d;
        }

        /// <summary>Regenerates the visual for a single tile using the stored style anchor.</summary>
        public async System.Threading.Tasks.Task RegenerateTileAsync(int x, int y)
        {
            if (grid == null || x < 0 || x >= width || y < 0 || y >= height) return;
            if (LLMService.Instance == null) return;
            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage($"Regenerating tile ({x},{y})...");
            string prompt = BuildTilePrompt(_lastTheme, grid[x, y].type, x, y);
            Texture2D tex = await LLMService.Instance.GenerateStyledTile(prompt, StyleAnchor);
            if (tex != null)
            {
                grid[x, y].visual = tex;
                UpdateTileVisual(x, y, tex);
                if (DnD.UI.ChatUI.Instance != null)
                    DnD.UI.ChatUI.Instance.AddSystemMessage($"Tile ({x},{y}) regenerated.");
            }
        }

        /// <summary>Returns a formatted string describing the current tile and its cardinal neighbours.</summary>
        public string GetTileContext(int x, int y)
        {
            if (grid == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Current tile ({grid[x, y].type}): {grid[x, y].description}");
            void Append(int nx, int ny, string dir)
            {
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    sb.AppendLine($"  {dir}: {grid[nx, ny].type} — {grid[nx, ny].description}");
            }
            Append(x, y + 1, "North"); Append(x, y - 1, "South");
            Append(x + 1, y, "East");  Append(x - 1, y, "West");
            return sb.ToString().TrimEnd();
        }

        // ── Snapshot (sub-map navigation) ────────────────────────────────────

        /// <summary>Captures the entire current map state for later restoration.</summary>
        public MapSnapshot TakeSnapshot(int playerX, int playerY)
        {
            // Strategy D: capture the painted background sprite (if present) so save/load round-trips it.
            Texture2D bg = null;
            var bgGO = transform.Find("MapBackground");
            if (bgGO != null)
            {
                var sr = bgGO.GetComponent<SpriteRenderer>();
                if (sr?.sprite != null) bg = sr.sprite.texture;
            }

            var snap = new MapSnapshot
            {
                lastTheme       = _lastTheme,
                width           = width,
                height          = height,
                playerX         = playerX,
                playerY         = playerY,
                styleAnchor     = StyleAnchor,
                backgroundImage = bg,
                tileTypes       = new TileType[width, height],
                tileDescs       = new string[width, height],
                tileVisuals     = new Texture2D[width, height],
                walkable        = new bool[width, height],
            };
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    snap.tileTypes[x, y]   = grid[x, y].type;
                    snap.tileDescs[x, y]   = grid[x, y].description;
                    snap.tileVisuals[x, y] = grid[x, y].visual;
                    snap.walkable[x, y]    = grid[x, y].walkable;
                }
            return snap;
        }

        /// <summary>Restores a previously saved map state without any LLM calls.</summary>
        public void RestoreFromSnapshot(MapSnapshot snap)
        {
            if (snap == null) return;

            // Destroy current tile objects
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            width       = snap.width;
            height      = snap.height;
            _lastTheme  = snap.lastTheme;
            StyleAnchor = snap.styleAnchor;
            grid        = new MapTile[width, height];

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    grid[x, y] = new MapTile
                    {
                        x           = x,
                        y           = y,
                        type        = snap.tileTypes[x, y],
                        description = snap.tileDescs[x, y],
                        visual      = snap.tileVisuals[x, y],
                        walkable    = snap.walkable[x, y],
                    };
                    // Strategy D: skip per-tile sprites when a background image is present.
                    if (snap.backgroundImage == null)
                        CreateTileVisual(x, y, snap.tileVisuals[x, y] ?? Texture2D.whiteTexture);
                }

            if (snap.backgroundImage != null)
                CreateBigBackgroundSprite(snap.backgroundImage);

            AdjustCamera();
            OnMapReady?.Invoke();
            Debug.Log("[MapGenerator] Map restored from snapshot.");
        }
    }
}
