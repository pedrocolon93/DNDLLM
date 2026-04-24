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
        // 5x5 (25 tiles) is the coherence-first default: each tile is generated sequentially
        // with neighbor-as-reference, so cost scales linearly. Still satisfies the width >= 5
        // / height >= 5 guard in GenerateLayout / GenerateTownLayout for interior POIs.
        public int width  = 5;
        public int height = 5;
        public float cellSize = 1.0f;

        [Tooltip("Render the whole map as ONE image then slice it into tiles. ~1 image call total, "
               + "seam-coherent by construction. Disable to use per-tile BFS generation.")]
        [SerializeField] private bool useBigSliceStrategy = true;
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
        /// Free-form player description of the adventure's starting point.
        /// When set, triggers a per-coord description LLM pass instead of the
        /// per-type default, so each tile gets its own unique narrative sentence.
        /// Cleared automatically after each generation.
        /// </summary>
        public string StartingPointNarrative { get; set; } = "";

        /// <summary>
        /// Persistent setting context (narrative + breadcrumb of the current location
        /// in the MapGraph). Unlike StartingContext / StartingPointNarrative this is
        /// NOT consumed after generation — it stays so later single-tile regenerations
        /// keep matching the adventure.
        /// </summary>
        public string NarrativeContext { get; set; } = "";

        /// <summary>
        /// Style description built once per map from the theme.
        /// Prepended to every tile prompt so DALL-E (which can't receive image references)
        /// stays on the same flat 2D top-down style across all tiles.
        /// </summary>
        private string _styleSummary = "";

        /// <summary>
        /// LLM-authored style bible: palette, materials, lighting, motifs, edge treatment.
        /// Prepended to every tile prompt. Falls back to _styleSummary if generation fails.
        /// Persisted via SaveData.styleBible so regenerations after reload stay on-style.
        /// </summary>
        public string StyleBible { get; internal set; } = "";

        /// <summary>
        /// Secondary style anchor for wall/structure tiles (complements the floor anchor).
        /// Sent to the multimodal LLM alongside neighbor textures for non-Floor tiles.
        /// </summary>
        public Texture2D SecondaryAnchor { get; internal set; }

        /// <summary>
        /// Optional style bible injected by the caller (e.g. GameManager on reload).
        /// When non-empty, GenerateMap skips the style-bible LLM call and uses this string instead.
        /// Cleared automatically at the end of GenerateMap.
        /// </summary>
        public string PreloadedStyleBible { get; set; } = "";

        [System.Serializable]
        public class MapTile
        {
            public int x, y;
            public TileType type;
            public string description;
            public Texture2D visual;
            public GameObject obj;
            public bool walkable;
            /// <summary>Edge feature descriptions indexed [N, E, S, W]. Populated by the spatial-plan LLM pass.</summary>
            public string[] edgeSignatures = new string[4];
        }

        /// <summary>Index constants for MapTile.edgeSignatures.</summary>
        public const int EdgeN = 0, EdgeE = 1, EdgeS = 2, EdgeW = 3;

        /// <summary>Stored map state for sub-map navigation (push/pop).</summary>
        public class MapSnapshot
        {
            public string       lastTheme;
            public int          width, height;
            public TileType[,]  tileTypes;
            public string[,]    tileDescs;
            public string[,,]   tileEdges;    // [x, y, dir] where dir in {EdgeN, EdgeE, EdgeS, EdgeW}
            public Texture2D[,] tileVisuals;
            public bool[,]      walkable;
            public int          playerX, playerY;
            public Texture2D    styleAnchor;
            public Texture2D    secondaryAnchor;
            public string       styleBible;
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
            Debug.Log($"[MapGenerator] GenerateMap: {keywords}");

            // Cleanup previous tile objects
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            string theme = string.IsNullOrEmpty(keywords) ? "stone dungeon" : keywords.Split(',')[0].Trim();
            _lastTheme = theme;

            // Hand-built style summary — the guaranteed fallback if the LLM style-bible call fails
            // or the model path doesn't support it.
            string narrativeFlavor = string.IsNullOrEmpty(NarrativeContext)
                ? ""
                : $" Setting flavor: {NarrativeContext.Replace("\n", " ").Trim()}.";
            _styleSummary = IsTownTheme(theme)
                ? $"Flat 2D top-down RPG tileset. {theme} setting.{narrativeFlavor} "
                + "Warm earthy cobblestone and stone palette. Consistent SNES/GBA RPG art style. "
                + "Seamlessly tileable edges."
                : $"Flat 2D top-down RPG tileset. {theme} setting.{narrativeFlavor} "
                + "Muted stone and earth palette adapted to the setting. Consistent SNES/GBA RPG art style. "
                + "Seamlessly tileable edges.";

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("Planning map layout...");

            // ── 1. Plan tile layout ──────────────────────────────────────────
            grid = new MapTile[width, height];
            if (IsTownTheme(theme))
                GenerateTownLayout();
            else
                GenerateLayout();

            LogLayout();

            // ── 2. Create tile GameObjects with white placeholder ─────────────
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    CreateTileVisual(x, y, Texture2D.whiteTexture);

            // ── 3. Style bible (LLM text call) — defines palette / materials / lighting / motifs.
            //      Preloaded bible from a saved slot skips this call.
            if (!string.IsNullOrEmpty(PreloadedStyleBible))
            {
                StyleBible = PreloadedStyleBible;
                Debug.Log("[MapGenerator] Using preloaded style bible from save.");
            }
            else
            {
                if (DnD.UI.ChatUI.Instance != null)
                    DnD.UI.ChatUI.Instance.AddSystemMessage("Authoring style bible...");
                StyleBible = await GenerateStyleBibleAsync(theme);
            }
            PreloadedStyleBible = ""; // consume

            // ── 4. Spatial plan: per-tile description + 4 edge signatures (1 LLM text call).
            //      On reload we'll have saved descriptions+edges already stamped, so skip re-planning.
            if (!SkipDescriptionGeneration)
            {
                if (DnD.UI.ChatUI.Instance != null)
                    DnD.UI.ChatUI.Instance.AddSystemMessage("Planning tile contents and edges...");
                await GenerateSpatialPlanAsync(theme, StyleBible);
            }
            else
            {
                // Ensure every tile has a non-null edge slot array even without re-planning.
                for (int x = 0; x < width; x++)
                    for (int y = 0; y < height; y++)
                        if (grid[x, y].edgeSignatures == null)
                            grid[x, y].edgeSignatures = new string[4];
                ReconcileEdges(); // fills boundary sentinels and aligns any mismatched restored pairs
            }
            SkipDescriptionGeneration = false; // reset for next call

            // ── 5. Big-slice: render the whole map as one image and cut it into tiles.
            //      ~1 image call, seams coherent by construction. The per-tile BFS below
            //      is the fallback when the flag is off or the big call fails.
            if (useBigSliceStrategy)
            {
                if (DnD.UI.ChatUI.Instance != null)
                    DnD.UI.ChatUI.Instance.AddSystemMessage("Rendering map image...");
                bool ok = await BigSliceGenerateAsync(theme);
                if (ok)
                {
                    if (DnD.UI.ChatUI.Instance != null)
                        DnD.UI.ChatUI.Instance.AddSystemMessage("Map ready.");
                    OnMapReady?.Invoke();
                    Debug.Log("[MapGenerator] Generation complete (big-slice).");
                    AdjustCamera();
                    return;
                }
                Debug.LogWarning("[MapGenerator] Big-slice failed; falling back to per-tile BFS.");
                if (DnD.UI.ChatUI.Instance != null)
                    DnD.UI.ChatUI.Instance.AddSystemMessage("Big-slice failed — regenerating per-tile...");
            }

            // ── 5b. Anchor pair: one Floor + one Wall tile, generated with full prompts so the
            //      rest of the BFS has two style references to anchor both floor-like and
            //      structure-like tiles.
            int primaryX = width  / 2;
            int primaryY = 1;

            if (DnD.UI.ChatUI.Instance != null)
                DnD.UI.ChatUI.Instance.AddSystemMessage("Generating anchor tiles...");

            // Primary anchor — the Floor at the player's entrance.
            string primaryPrompt = BuildTilePrompt(theme, grid[primaryX, primaryY].type, primaryX, primaryY);
            Texture2D primary = await LLMService.Instance.GenerateStyledTile(primaryPrompt, (Texture2D[])null);
            if (primary == null)
            {
                Debug.LogWarning("[MapGenerator] Primary anchor failed; using white fallback.");
                primary = Texture2D.whiteTexture;
            }
            StyleAnchor = primary;
            grid[primaryX, primaryY].visual = primary;
            UpdateTileVisual(primaryX, primaryY, primary);

            // Secondary anchor — pick any Wall tile (first found scanning from the south edge).
            int secondaryX = -1, secondaryY = -1;
            for (int y = 0; y < height && secondaryX < 0; y++)
                for (int x = 0; x < width && secondaryX < 0; x++)
                    if (grid[x, y].type == TileType.Wall) { secondaryX = x; secondaryY = y; }

            if (secondaryX >= 0)
            {
                string secondaryPrompt = BuildTilePrompt(theme, grid[secondaryX, secondaryY].type, secondaryX, secondaryY);
                Texture2D secondary = await LLMService.Instance.GenerateStyledTile(secondaryPrompt, new[] { primary });
                if (secondary != null)
                {
                    SecondaryAnchor = secondary;
                    grid[secondaryX, secondaryY].visual = secondary;
                    UpdateTileVisual(secondaryX, secondaryY, secondary);
                }
            }

            // ── 6. BFS sequential generation — each tile uses actual neighbor textures as
            //      references, so path/wall features carry across edges. Coherence > speed.
            int total = width * height;
            int done  = 0;
            if (grid[primaryX, primaryY].visual != null) done++;
            if (secondaryX >= 0 && grid[secondaryX, secondaryY].visual != null) done++;

            GameObject progressHandle = null;
            if (DnD.UI.ChatUI.Instance != null)
                progressHandle = DnD.UI.ChatUI.Instance.AddProgressIndicator($"Generating tiles: {done}/{total}");

            await BfsGenerate(theme, primaryX, primaryY, total, done, progressHandle);

            if (DnD.UI.ChatUI.Instance != null)
            {
                DnD.UI.ChatUI.Instance.RemoveProgressIndicator(progressHandle);
                DnD.UI.ChatUI.Instance.AddSystemMessage("Map ready.");
            }

            OnMapReady?.Invoke();
            Debug.Log("[MapGenerator] Generation complete.");
            AdjustCamera();
        }

        // ── Big-slice strategy ───────────────────────────────────────────────

        /// <summary>
        /// Renders the entire map as ONE image via Gemini multimodal, center-crops to a square,
        /// and slices it into width×height tiles that stitch back together seamlessly.
        /// Returns true on success; caller falls back to BFS on false.
        /// </summary>
        private async Task<bool> BigSliceGenerateAsync(string theme)
        {
            if (LLMService.Instance == null) return false;

            string ascii = BuildAsciiLayout();
            string styleBlock = string.IsNullOrEmpty(StyleBible) ? _styleSummary : StyleBible;
            string prompt = BuildBigSlicePrompt(theme, styleBlock, ascii);

            Texture2D big = await LLMService.Instance.GenerateImage(prompt);
            if (big == null) return false;
            StyleAnchor = big;

            if (!SliceIntoGrid(big))
            {
                Debug.LogWarning("[MapGenerator] Slicing failed (unreadable texture?); falling back to BFS.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Builds a compact ASCII map where y=0 is the TOP row in the returned string
        /// (matches the "y=0 is top" promise inside the big-slice prompt). The image the
        /// model returns is then sliced row-by-row from top, so image row 0 corresponds
        /// to Unity grid y=height-1.
        /// </summary>
        private string BuildAsciiLayout()
        {
            var sb = new System.Text.StringBuilder();
            for (int y = height - 1; y >= 0; y--)        // top row first
            {
                int rowTop = (height - 1) - y;            // 0 at the top of the ascii
                sb.Append($"y={rowTop}: ");
                for (int x = 0; x < width; x++)
                    sb.Append(SymbolForType(grid[x, y].type));
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static char SymbolForType(TileType t)
        {
            switch (t)
            {
                case TileType.Wall:       return '#';
                case TileType.Door:       return '+';
                case TileType.House:
                case TileType.Inn:
                case TileType.Market:     return 'B';
                case TileType.Fountain:   return 'F';
                case TileType.NpcSpawn:   return 'N';
                case TileType.Chest:      return 'C';
                case TileType.EnemySpawn: return 'E';
                case TileType.Exit:       return 'X';
                default:                  return '.'; // Floor
            }
        }

        private static string BuildBigSlicePrompt(string theme, string styleBlock, string asciiLayout)
        {
            return
                "Render ONE complete overhead pixel-art map as a single square image.\n\n"
              + $"THEME: {theme}\n\n"
              + $"STYLE BIBLE:\n{styleBlock}\n\n"
              + $"LAYOUT (y=0 is top; symbols: .=Floor, #=Wall, +=Door, B=Building, F=Fountain, N=NPC, C=Chest, E=Enemy lair, X=Exit):\n{asciiLayout}\n\n"
              + "CRITICAL RULES:\n"
              + "- Render the ENTIRE map as ONE continuous picture with NO internal frames, NO grid lines, NO tile borders.\n"
              + "- Strict orthographic top-down view. No perspective, no vignette, no title card, no caption.\n"
              + "- Walls (#) form a CONTINUOUS ring around the map. Adjacent wall cells join seamlessly.\n"
              + "- Doors (+) are OPENINGS in that wall ring — the ground passes through them.\n"
              + "- Floor cells (.) share one continuous ground texture; no walls between adjacent floors.\n"
              + "- Buildings / objects (B/F/N/C/E/X) are standalone features on the floor, drawn to fit inside their cell.\n"
              + "- The image fills its canvas edge to edge. The outer perimeter of the map meets the canvas edge directly — no matting, no drop shadow, no frame.\n"
              + "- Lighting and color must stay uniform across the whole image per the style bible.\n\n"
              + "Output one square image only.";
        }

        /// <summary>
        /// Center-crops the big image to a square, divides it into width×height cells,
        /// and assigns each cell to grid[x,y].visual (flipping row order so image-top maps
        /// to Unity y=height-1). Returns false if the source texture is unreadable.
        /// </summary>
        private bool SliceIntoGrid(Texture2D big)
        {
            int side  = Mathf.Min(big.width, big.height);
            int leftX = (big.width  - side) / 2;
            int botY  = (big.height - side) / 2;
            int cellW = side / width;
            int cellH = side / height;
            if (cellW <= 0 || cellH <= 0) return false;

            Color[] all;
            try { all = big.GetPixels(leftX, botY, cellW * width, cellH * height); }
            catch (UnityException) { return false; }

            int stride = cellW * width;
            for (int r = 0; r < height; r++)              // r=0 is top row in image
            {
                int uy = (height - 1) - r;                 // Unity y (y=height-1 is north/top)
                // image-top = highest pixel y in the cropped area
                int cellTopPy  = cellH * height - r * cellH;        // exclusive top (from cropped origin)
                int cellBotPy  = cellTopPy - cellH;                  // inclusive bottom
                for (int c = 0; c < width; c++)
                {
                    var cell = new Texture2D(cellW, cellH, TextureFormat.RGBA32, false);
                    var pixels = new Color[cellW * cellH];
                    for (int py = 0; py < cellH; py++)
                        for (int px = 0; px < cellW; px++)
                            pixels[py * cellW + px] = all[(cellBotPy + py) * stride + (c * cellW + px)];
                    cell.SetPixels(pixels);
                    cell.Apply();
                    grid[c, uy].visual = cell;
                    UpdateTileVisual(c, uy, cell);
                }
            }
            return true;
        }

        // ── Per-tile BFS strategy (fallback) ─────────────────────────────────

        /// <summary>
        /// BFS outward from the primary anchor, generating each tile sequentially so
        /// neighbor textures are available as multimodal references when their turn comes up.
        /// Tiles that already have a visual (anchor pair) are skipped but still used to
        /// enqueue their neighbors.
        /// </summary>
        private async Task BfsGenerate(string theme, int startX, int startY, int total, int done, GameObject progressHandle)
        {
            var visited = new bool[width, height];
            var queue   = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));
            visited[startX, startY] = true;

            // Seed visited for any pre-existing visuals (secondary anchor), so BFS still reaches them for neighbor enqueue.
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (grid[x, y].visual != null && !visited[x, y])
                    {
                        queue.Enqueue((x, y));
                        visited[x, y] = true;
                    }

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();

                if (grid[cx, cy].visual == null)
                {
                    string prompt = BuildTilePrompt(theme, grid[cx, cy].type, cx, cy);
                    Texture2D[] refs = GetNeighborVisuals(cx, cy, grid[cx, cy].type);
                    Texture2D tex = await LLMService.Instance.GenerateStyledTile(prompt, refs);
                    if (tex == null) tex = Texture2D.whiteTexture;
                    grid[cx, cy].visual = tex;
                    UpdateTileVisual(cx, cy, tex);

                    done++;
                    if (DnD.UI.ChatUI.Instance != null && progressHandle != null)
                        DnD.UI.ChatUI.Instance.UpdateProgressIndicator(progressHandle,
                            $"Generating tiles: {done}/{total}");
                }

                // Enqueue cardinal neighbors
                void Push(int nx, int ny)
                {
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) return;
                    if (visited[nx, ny]) return;
                    visited[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
                Push(cx,     cy + 1);
                Push(cx + 1, cy);
                Push(cx,     cy - 1);
                Push(cx - 1, cy);
            }
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

            // Map-graph context: the tile's narrative description (from spatial plan) plus which
            // tile types sit in each cardinal direction. Lets the model paint a fountain-adjacent
            // cobblestone differently from a wall-adjacent one, instead of one generic "Floor" visual.
            string narrative = string.IsNullOrEmpty(grid[x, y].description)
                ? ""
                : $" NARRATIVE: {grid[x, y].description}.";
            string neighbors = skipConnectivity ? "" : $" ADJACENT: {GetNeighborTypeSummary(x, y)}.";
            string location  = $" LOCATION: {GetPositionHint(x, y)}.";
            string settingCtx = string.IsNullOrEmpty(NarrativeContext)
                ? ""
                : $" SETTING: {NarrativeContext.Replace("\n", " ").Trim()}.";

            // Edge continuity block — concrete phrases for what must cross each edge so
            // neighbors visually connect. Populated by GenerateSpatialPlanAsync + ReconcileEdges.
            string edges = "";
            var eSig = grid[x, y].edgeSignatures;
            if (eSig != null && eSig.Length == 4
                && (eSig[EdgeN] != null || eSig[EdgeE] != null || eSig[EdgeS] != null || eSig[EdgeW] != null))
            {
                edges = " EDGES: "
                      + $"top={eSig[EdgeN] ?? ""}; "
                      + $"right={eSig[EdgeE] ?? ""}; "
                      + $"bottom={eSig[EdgeS] ?? ""}; "
                      + $"left={eSig[EdgeW] ?? ""}. "
                      + "Paint each edge so these exact features reach the image border unbroken; "
                      + "they will tile seamlessly against neighbor tiles that carry the same feature.";
            }

            // Prefer the LLM-authored style bible; fall back to the hand-built summary if bibling failed.
            string styleBlock = string.IsNullOrEmpty(StyleBible) ? _styleSummary : StyleBible;

            return PERSPECTIVE_LOCK + styleBlock
                 + $" Square 1:1 tile. CONTENT: {typeDesc}. POSITION: {connectivity}."
                 + settingCtx + location + neighbors + narrative + edges
                 + " Use the exact art style, color palette, and brushwork of the reference images. "
                 + "Seamless edges, no perspective, no drop shadows, no borders, no extra UI elements.";
        }

        /// <summary>
        /// Collects up to 4 neighbor visuals as multimodal references for the given tile.
        /// Falls back to the type-matched style anchor (primary for Floor-like, secondary for Wall-like)
        /// when neighbors haven't been generated yet.
        /// </summary>
        private Texture2D[] GetNeighborVisuals(int x, int y, TileType selfType)
        {
            var refs = new List<Texture2D>(4);
            // Cardinal neighbors
            if (y < height - 1 && grid[x, y + 1].visual != null) refs.Add(grid[x, y + 1].visual);
            if (x < width  - 1 && grid[x + 1, y].visual != null) refs.Add(grid[x + 1, y].visual);
            if (y > 0          && grid[x, y - 1].visual != null) refs.Add(grid[x, y - 1].visual);
            if (x > 0          && grid[x - 1, y].visual != null) refs.Add(grid[x - 1, y].visual);

            // Always include the type-matched anchor so style stays stable even when neighbors are sparse.
            Texture2D anchor = (selfType == TileType.Floor) ? StyleAnchor : (SecondaryAnchor ?? StyleAnchor);
            if (anchor != null && !refs.Contains(anchor))
            {
                // Cap at 4 total for token budget
                if (refs.Count >= 4) refs[refs.Count - 1] = anchor;
                else                 refs.Add(anchor);
            }
            return refs.ToArray();
        }

        /// <summary>Comma-separated list of cardinal-neighbor tile types, e.g. "north: Wall, east: Floor, south: Door, west: Floor".</summary>
        private string GetNeighborTypeSummary(int x, int y)
        {
            var parts = new List<string>(4);
            if (y < height - 1) parts.Add($"north: {grid[x, y + 1].type}");
            if (x < width  - 1) parts.Add($"east: {grid[x + 1, y].type}");
            if (y > 0)          parts.Add($"south: {grid[x, y - 1].type}");
            if (x > 0)          parts.Add($"west: {grid[x - 1, y].type}");
            return parts.Count == 0 ? "isolated" : string.Join(", ", parts);
        }

        /// <summary>Classifies grid position: corner / edge / interior, with quadrant hint.</summary>
        private string GetPositionHint(int x, int y)
        {
            bool left   = x == 0;
            bool right  = x == width - 1;
            bool top    = y == height - 1;
            bool bottom = y == 0;
            int  edges  = (left ? 1 : 0) + (right ? 1 : 0) + (top ? 1 : 0) + (bottom ? 1 : 0);

            if (edges == 2)
            {
                string v = top ? "NE" : "SE";
                if (left) v = top ? "NW" : "SW";
                return $"{v} corner of the map";
            }
            if (edges == 1)
            {
                if (left)   return "west edge of the map";
                if (right)  return "east edge of the map";
                if (top)    return "north edge of the map";
                if (bottom) return "south edge of the map";
            }
            int cx = width / 2, cy = height / 2;
            return (x == cx && y == cy) ? "center of the map" : "interior of the map";
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

        /// <summary>
        /// Per-coord description generation driven by <see cref="StartingPointNarrative"/>.
        /// Builds an ASCII layout of tile types, asks the LLM for one sentence per (x,y),
        /// and stores the result on <c>grid[x,y].description</c>. Walls and unmatched
        /// coords fall back to the per-type default so nothing renders blank.
        /// </summary>
        private async System.Threading.Tasks.Task GeneratePerTileDescriptionsAsync(string theme)
        {
            if (LLMService.Instance == null) return;

            string narrative = StartingPointNarrative;
            StartingPointNarrative = ""; // consume — reset for next call

            // Build a compact layout so the LLM sees every coord + its type.
            var layout = new System.Text.StringBuilder();
            for (int y = height - 1; y >= 0; y--)      // print top row first for readability
            {
                for (int x = 0; x < width; x++)
                {
                    layout.Append($"({x},{y})={grid[x, y].type} ");
                }
                layout.AppendLine();
            }

            string sys = "You are a D&D dungeon designer. Reply only with the exact format requested, no extra text.";
            string usr =
                $"Theme: {theme}\n" +
                $"Starting point narrative from the player: \"{narrative}\"\n\n" +
                "Map layout (coord=type), top row first:\n" +
                layout +
                "\nWrite ONE evocative sentence for EACH coord, describing what the player sees there. " +
                "The sentences must collectively match the narrative, be internally consistent, and give " +
                "each tile a distinct feature, object, or detail (e.g. the tavern's hanging sign, the blacksmith's forge, " +
                "the monastery's carved door). Walls can be terse.\n" +
                "Use exactly this format, one line per tile (no extra text, no blank lines):\n" +
                "TILE x,y: <sentence>";

            string raw = await LLMService.Instance.SendPrompt(sys, usr);
            if (string.IsNullOrEmpty(raw))
            {
                Debug.LogWarning("[MapGenerator] Per-tile LLM call returned empty; falling back to per-type.");
                await GenerateTileDescriptionsAsync(theme);
                return;
            }

            int parsed = 0;
            foreach (string rawLine in raw.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                // Expect "TILE x,y: sentence" — skip anything else.
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string head = line.Substring(0, colon).Trim();
                string body = line.Substring(colon + 1).Trim();
                if (body.Length == 0) continue;
                if (!head.ToUpper().StartsWith("TILE")) continue;

                string coords = head.Substring(4).Trim();
                int comma = coords.IndexOf(',');
                if (comma < 0) continue;
                if (!int.TryParse(coords.Substring(0, comma).Trim(), out int tx)) continue;
                if (!int.TryParse(coords.Substring(comma + 1).Trim(), out int ty)) continue;
                if (tx < 0 || tx >= width || ty < 0 || ty >= height) continue;

                grid[tx, ty].description = body;
                parsed++;
            }

            Debug.Log($"[MapGenerator] Per-tile descriptions populated: {parsed}/{width * height}.");

            // Fill any gaps with a minimal hardcoded default so every tile renders with context.
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (string.IsNullOrEmpty(grid[x, y].description))
                        grid[x, y].description = DefaultDescriptionFor(grid[x, y].type);
        }

        private static string DefaultDescriptionFor(TileType t)
        {
            switch (t)
            {
                case TileType.Floor:      return "Open ground.";
                case TileType.Wall:       return "A solid wall blocks the way.";
                case TileType.Door:       return "A door stands here.";
                case TileType.Chest:      return "A chest waits to be opened.";
                case TileType.EnemySpawn: return "Something hostile stirs here.";
                case TileType.Exit:       return "A way onward.";
                case TileType.House:      return "A house.";
                case TileType.Inn:        return "An inn.";
                case TileType.Market:     return "A market stall.";
                case TileType.Fountain:   return "A fountain.";
                case TileType.NpcSpawn:   return "A figure stands here.";
                default:                  return "";
            }
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

        // ── Coherent generation — style bible + spatial plan ────────────────

        /// <summary>
        /// Asks the LLM for a structured style bible for this map: palette, materials,
        /// lighting, motifs, edge treatment. Returns a compact block that every tile
        /// prompt prepends so art style and atmosphere stay consistent across tiles.
        /// Returns empty string on LLM failure — caller falls back to _styleSummary.
        /// </summary>
        private async Task<string> GenerateStyleBibleAsync(string theme)
        {
            if (LLMService.Instance == null) return "";

            string narrative = string.IsNullOrEmpty(NarrativeContext)
                ? theme
                : $"{theme}. Setting: {NarrativeContext.Replace("\n", " ").Trim()}";

            string sys = "You are an art director for a 2D top-down RPG. "
                       + "Reply only with the exact format requested, no preamble, no extra text, no markdown.";
            string usr =
                $"Design a visual style bible for this map setting: \"{narrative}\".\n"
              + "The bible must be renderable in the SNES/GBA top-down RPG art style "
              + "(Zelda: A Link to the Past, Final Fantasy 6).\n\n"
              + "Reply with exactly these five lines — concrete, specific, vocabulary a pixel-art illustrator could follow:\n"
              + "PALETTE: <3-5 specific colors separated by commas>\n"
              + "MATERIALS: <2-4 surface treatments separated by commas>\n"
              + "LIGHTING: <time of day + direction, one phrase>\n"
              + "MOTIFS: <2-3 recurring visual details separated by commas>\n"
              + "EDGES: <how tiles meet — grout, seams, vegetation, etc.>";

            string raw = await LLMService.Instance.SendPrompt(sys, usr);
            if (string.IsNullOrEmpty(raw)) return "";

            // Keep only the five expected keys so stray model commentary doesn't pollute the prompt.
            var keep = new HashSet<string> { "PALETTE", "MATERIALS", "LIGHTING", "MOTIFS", "EDGES" };
            var sb = new System.Text.StringBuilder();
            foreach (string rawLine in raw.Split('\n'))
            {
                string line = rawLine.Trim();
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim().ToUpper();
                if (!keep.Contains(key)) continue;
                sb.AppendLine(line);
            }
            string result = sb.ToString().Trim();
            Debug.Log($"[MapGenerator] Style bible ({result.Length} chars):\n{result}");
            return result;
        }

        /// <summary>
        /// One LLM call that plans every tile's narrative content AND its 4-edge signature.
        /// Output format per tile:
        ///   TILE x,y: DESC=... | N=... | E=... | S=... | W=...
        /// The LLM is instructed to match (x,y).east with (x+1,y).west (and N/S similarly);
        /// a deterministic <see cref="ReconcileEdges"/> pass fixes any slips post-parse.
        /// On failure, falls back to the legacy per-type description path.
        /// </summary>
        private async Task GenerateSpatialPlanAsync(string theme, string styleBible)
        {
            if (LLMService.Instance == null) return;

            string narrative = string.IsNullOrEmpty(StartingPointNarrative) ? NarrativeContext : StartingPointNarrative;
            StartingPointNarrative = ""; // consume — reset for next call

            // Build compact layout (top row first for human/LLM readability)
            var layout = new System.Text.StringBuilder();
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                    layout.Append($"({x},{y})={grid[x, y].type} ");
                layout.AppendLine();
            }

            string sys = "You are a pixel-art tile planner for a top-down RPG map. "
                       + "Reply only with the exact format requested. No preamble, no markdown, no commentary.";
            string usr =
                $"Theme: {theme}\n"
              + (string.IsNullOrEmpty(narrative) ? "" : $"Adventure context: {narrative}\n")
              + (string.IsNullOrEmpty(styleBible) ? "" : $"\nStyle bible:\n{styleBible}\n")
              + $"\nMap layout (coord=type, top row first):\n{layout}\n"
              + "For EACH coord, author a tile plan with one short narrative sentence AND four edge signatures.\n"
              + "The edge signature is a concrete phrase describing the visual feature crossing that side of the tile "
              + "(e.g. \"continuous 2-stone-wide cobblestone path\", \"solid cut-stone wall, no opening\", \"mossy crack from center to right\").\n"
              + "HARD CONSTRAINT: tile (x,y).E must equal tile (x+1,y).W, and tile (x,y).N must equal tile (x,y+1).S. "
              + "Boundary edges with no neighbor may be \"map edge\".\n"
              + "Use exactly this format, one line per tile, no blank lines:\n"
              + "TILE x,y: DESC=<sentence> | N=<phrase> | E=<phrase> | S=<phrase> | W=<phrase>";

            string raw = await LLMService.Instance.SendPrompt(sys, usr);
            if (string.IsNullOrEmpty(raw))
            {
                Debug.LogWarning("[MapGenerator] Spatial plan empty; falling back to per-type descriptions.");
                await GenerateTileDescriptionsAsync(theme);
                return;
            }

            int parsed = 0;
            foreach (string rawLine in raw.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (!line.ToUpper().StartsWith("TILE ")) continue;

                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string head = line.Substring(5, colon - 5).Trim(); // strip "TILE "
                int comma = head.IndexOf(',');
                if (comma < 0) continue;
                if (!int.TryParse(head.Substring(0, comma).Trim(), out int tx)) continue;
                if (!int.TryParse(head.Substring(comma + 1).Trim(), out int ty)) continue;
                if (tx < 0 || tx >= width || ty < 0 || ty >= height) continue;

                string body = line.Substring(colon + 1);
                string desc = "", n = "", e = "", s = "", w = "";
                foreach (string part in body.Split('|'))
                {
                    string p = part.Trim();
                    int eq = p.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = p.Substring(0, eq).Trim().ToUpper();
                    string val = p.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "DESC": desc = val; break;
                        case "N":    n    = val; break;
                        case "E":    e    = val; break;
                        case "S":    s    = val; break;
                        case "W":    w    = val; break;
                    }
                }

                if (!string.IsNullOrEmpty(desc)) grid[tx, ty].description = desc;
                var edges = grid[tx, ty].edgeSignatures ?? (grid[tx, ty].edgeSignatures = new string[4]);
                if (!string.IsNullOrEmpty(n)) edges[EdgeN] = n;
                if (!string.IsNullOrEmpty(e)) edges[EdgeE] = e;
                if (!string.IsNullOrEmpty(s)) edges[EdgeS] = s;
                if (!string.IsNullOrEmpty(w)) edges[EdgeW] = w;
                parsed++;
            }

            // Fill gaps with per-type defaults so every tile has a description + non-null edge slots.
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (string.IsNullOrEmpty(grid[x, y].description))
                        grid[x, y].description = DefaultDescriptionFor(grid[x, y].type);
                    if (grid[x, y].edgeSignatures == null)
                        grid[x, y].edgeSignatures = new string[4];
                }

            ReconcileEdges();
            Debug.Log($"[MapGenerator] Spatial plan populated {parsed}/{width * height} tiles.");
        }

        /// <summary>
        /// Deterministic post-parse pass: for each shared edge, if the two phrases differ,
        /// pick the more specific one (longer wins) and copy it across so neighbors agree.
        /// Cheap and guarantees continuity even if the LLM slipped.
        /// </summary>
        private void ReconcileEdges()
        {
            if (grid == null) return;

            // Horizontal pairs: (x,y).E must equal (x+1,y).W
            for (int x = 0; x < width - 1; x++)
                for (int y = 0; y < height; y++)
                {
                    string a = grid[x, y].edgeSignatures[EdgeE] ?? "";
                    string b = grid[x + 1, y].edgeSignatures[EdgeW] ?? "";
                    string pick = PickMoreSpecific(a, b);
                    grid[x, y].edgeSignatures[EdgeE]     = pick;
                    grid[x + 1, y].edgeSignatures[EdgeW] = pick;
                }

            // Vertical pairs: (x,y).N must equal (x,y+1).S
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height - 1; y++)
                {
                    string a = grid[x, y].edgeSignatures[EdgeN]     ?? "";
                    string b = grid[x, y + 1].edgeSignatures[EdgeS] ?? "";
                    string pick = PickMoreSpecific(a, b);
                    grid[x, y].edgeSignatures[EdgeN]     = pick;
                    grid[x, y + 1].edgeSignatures[EdgeS] = pick;
                }

            // Mark map-boundary edges so the prompt can tell the model "this side is the outer edge"
            for (int x = 0; x < width; x++)
            {
                if (string.IsNullOrEmpty(grid[x, 0].edgeSignatures[EdgeS]))
                    grid[x, 0].edgeSignatures[EdgeS] = "map boundary";
                if (string.IsNullOrEmpty(grid[x, height - 1].edgeSignatures[EdgeN]))
                    grid[x, height - 1].edgeSignatures[EdgeN] = "map boundary";
            }
            for (int y = 0; y < height; y++)
            {
                if (string.IsNullOrEmpty(grid[0, y].edgeSignatures[EdgeW]))
                    grid[0, y].edgeSignatures[EdgeW] = "map boundary";
                if (string.IsNullOrEmpty(grid[width - 1, y].edgeSignatures[EdgeE]))
                    grid[width - 1, y].edgeSignatures[EdgeE] = "map boundary";
            }
        }

        private static string PickMoreSpecific(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            if (a == b) return a;
            // Longer phrase typically has more material detail; "map boundary" is a last-resort sentinel.
            if (a == "map boundary") return b;
            if (b == "map boundary") return a;
            return a.Length >= b.Length ? a : b;
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
            var snap = new MapSnapshot
            {
                lastTheme   = _lastTheme,
                width       = width,
                height      = height,
                playerX     = playerX,
                playerY     = playerY,
                styleAnchor = StyleAnchor,
                tileTypes   = new TileType[width, height],
                tileDescs   = new string[width, height],
                tileVisuals = new Texture2D[width, height],
                walkable    = new bool[width, height],
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
                    CreateTileVisual(x, y, snap.tileVisuals[x, y] ?? Texture2D.whiteTexture);
                }

            AdjustCamera();
            OnMapReady?.Invoke();
            Debug.Log("[MapGenerator] Map restored from snapshot.");
        }
    }
}
