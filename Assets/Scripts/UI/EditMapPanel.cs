using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using DNDLLM.Map;

namespace DnD.UI
{
    /// <summary>
    /// In-game map editor. Lets the player click any tile on the map, change its
    /// TileType, write a custom description, and regenerate its artwork.
    /// All changes apply directly to MapGenerator.grid and are saved when the
    /// player saves via the normal save flow.
    /// </summary>
    public class EditMapPanel : MonoBehaviour
    {
        public static EditMapPanel Instance { get; private set; }

        /// <summary>Invoked when the player clicks "Save Changes" so GameManager can persist.</summary>
        public System.Action OnSaveRequested;

        // ── Wired by UISceneBuilder ──────────────────────────────────────────
        [SerializeField] private RectTransform   tileGridContainer;
        [SerializeField] private TextMeshProUGUI selectedTileLabel;
        [SerializeField] private TMP_Dropdown    tileTypeDropdown;
        [SerializeField] private TMP_InputField  descriptionInput;
        [SerializeField] private RawImage        selectedTilePreview;
        [SerializeField] private Button          regenerateButton;
        [SerializeField] private Button          applyButton;
        [SerializeField] private Button          saveButton;

        // ── State ────────────────────────────────────────────────────────────
        private int _selectedX = -1, _selectedY = -1;
        private readonly List<RawImage> _thumbnails = new List<RawImage>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void Open()
        {
            gameObject.SetActive(true);
            RebuildGrid();
            ClearSelection();
        }

        public void Close() => gameObject.SetActive(false);

        // ── Grid ─────────────────────────────────────────────────────────────

        private void RebuildGrid()
        {
            // Clear old thumbnails
            foreach (Transform c in tileGridContainer) Destroy(c.gameObject);
            _thumbnails.Clear();

            var gen = MapGenerator.Instance;
            if (gen == null || gen.grid == null) return;

            // Populate row-major top-to-bottom (y = height-1 down to 0 so north is top)
            for (int y = gen.height - 1; y >= 0; y--)
            for (int x = 0; x < gen.width;  x++)
            {
                int cx = x, cy = y;
                var cellGO = new GameObject($"Cell_{x}_{y}", typeof(RectTransform));
                cellGO.transform.SetParent(tileGridContainer, false);

                // Background tint to indicate selection
                var bg = cellGO.AddComponent<Image>();
                bg.color = new Color32(0x28, 0x1E, 0x10, 0xFF);

                // Tile thumbnail
                var rawGO = new GameObject("Thumb", typeof(RectTransform));
                rawGO.transform.SetParent(cellGO.transform, false);
                var rawRT = rawGO.GetComponent<RectTransform>();
                rawRT.anchorMin = new Vector2(0.05f, 0.05f);
                rawRT.anchorMax = new Vector2(0.95f, 0.95f);
                rawRT.offsetMin = Vector2.zero;
                rawRT.offsetMax = Vector2.zero;
                var raw = rawGO.AddComponent<RawImage>();
                raw.texture = gen.grid[x, y].visual;
                raw.color   = raw.texture != null ? Color.white : new Color32(0x4A, 0x38, 0x20, 0xFF);
                _thumbnails.Add(raw);

                // Type label overlay
                var labelGO = new GameObject("TypeLabel", typeof(RectTransform));
                labelGO.transform.SetParent(cellGO.transform, false);
                var labelRT = labelGO.GetComponent<RectTransform>();
                labelRT.anchorMin = new Vector2(0, 0);
                labelRT.anchorMax = new Vector2(1, 0.3f);
                labelRT.offsetMin = Vector2.zero;
                labelRT.offsetMax = Vector2.zero;
                var lbg = labelGO.AddComponent<Image>();
                lbg.color = new Color(0, 0, 0, 0.6f);
                var typeLabelGO = new GameObject("Text", typeof(RectTransform));
                typeLabelGO.transform.SetParent(labelGO.transform, false);
                var typeLabelRT = typeLabelGO.GetComponent<RectTransform>();
                typeLabelRT.anchorMin = Vector2.zero; typeLabelRT.anchorMax = Vector2.one;
                typeLabelRT.offsetMin = Vector2.zero; typeLabelRT.offsetMax = Vector2.zero;
                var typeTMP = typeLabelGO.AddComponent<TextMeshProUGUI>();
                typeTMP.text      = TileTypeAbbr(gen.grid[x, y].type);
                typeTMP.fontSize  = 7f;
                typeTMP.color     = UITheme.GoldAccent;
                typeTMP.alignment = TextAlignmentOptions.Center;

                // Clickable button overlay
                var btn = cellGO.AddComponent<Button>();
                btn.onClick.AddListener(() => SelectTile(cx, cy));
            }
        }

        private static string TileTypeAbbr(TileType t) => t switch
        {
            TileType.Floor     => "FLR",
            TileType.Wall      => "WLL",
            TileType.Door      => "DR",
            TileType.Chest     => "CST",
            TileType.EnemySpawn=> "ENM",
            TileType.Exit      => "EXT",
            TileType.House     => "HSE",
            TileType.Inn       => "INN",
            TileType.Market    => "MKT",
            TileType.Fountain  => "FNT",
            TileType.NpcSpawn  => "NPC",
            _                  => "?",
        };

        // ── Selection ────────────────────────────────────────────────────────

        private void SelectTile(int x, int y)
        {
            _selectedX = x; _selectedY = y;
            var gen = MapGenerator.Instance;
            if (gen == null || gen.grid == null) return;
            var tile = gen.grid[x, y];

            if (selectedTileLabel) selectedTileLabel.text = $"Tile ({x}, {y})  —  {tile.type}";
            if (tileTypeDropdown)  { tileTypeDropdown.value = (int)tile.type; tileTypeDropdown.RefreshShownValue(); }
            if (descriptionInput)  descriptionInput.text = tile.description ?? "";
            if (selectedTilePreview)
            {
                selectedTilePreview.texture = tile.visual;
                selectedTilePreview.color   = tile.visual != null ? Color.white : new Color32(0x4A, 0x38, 0x20, 0xFF);
            }
            if (regenerateButton) regenerateButton.interactable = true;
            if (applyButton)      applyButton.interactable      = true;
        }

        private void ClearSelection()
        {
            _selectedX = _selectedY = -1;
            if (selectedTileLabel) selectedTileLabel.text = "Select a tile to edit it";
            if (descriptionInput)  descriptionInput.text  = "";
            if (selectedTilePreview) { selectedTilePreview.texture = null; selectedTilePreview.color = new Color32(0x28, 0x1E, 0x10, 0xFF); }
            if (regenerateButton)  regenerateButton.interactable  = false;
            if (applyButton)       applyButton.interactable       = false;
        }

        // ── Edit actions ─────────────────────────────────────────────────────

        /// <summary>Writes the editor fields back into MapGenerator.grid for the selected tile.</summary>
        public void ApplyCurrentTileEdits()
        {
            if (_selectedX < 0 || _selectedY < 0) return;
            var gen = MapGenerator.Instance;
            if (gen == null || gen.grid == null) return;

            var tile = gen.grid[_selectedX, _selectedY];
            if (tileTypeDropdown)
            {
                tile.type     = (TileType)tileTypeDropdown.value;
                tile.walkable = tile.type == TileType.Floor    || tile.type == TileType.Exit
                             || tile.type == TileType.Door     || tile.type == TileType.NpcSpawn;
            }
            if (descriptionInput) tile.description = descriptionInput.text;

            // Refresh the header label
            if (selectedTileLabel) selectedTileLabel.text = $"Tile ({_selectedX}, {_selectedY})  —  {tile.type}";

            // Refresh the abbreviated type label in the grid thumbnail
            RefreshThumbnailLabel(_selectedX, _selectedY);
        }

        /// <summary>Applies edits then regenerates the visual for the selected tile.</summary>
        public async void RegenerateSelectedTile()
        {
            if (_selectedX < 0 || _selectedY < 0) return;
            ApplyCurrentTileEdits();

            if (regenerateButton) regenerateButton.interactable = false;

            await MapGenerator.Instance.RegenerateTileAsync(_selectedX, _selectedY);

            // Refresh preview and grid thumbnail
            var gen = MapGenerator.Instance;
            if (gen != null && gen.grid != null)
            {
                var visual = gen.grid[_selectedX, _selectedY].visual;
                if (selectedTilePreview)
                {
                    selectedTilePreview.texture = visual;
                    selectedTilePreview.color   = visual != null ? Color.white : new Color32(0x4A, 0x38, 0x20, 0xFF);
                }
                RefreshThumbnailTexture(_selectedX, _selectedY, visual);
            }

            if (regenerateButton) regenerateButton.interactable = true;
        }

        private void RefreshThumbnailLabel(int x, int y)
        {
            int idx = ThumbnailIndex(x, y);
            if (idx < 0 || idx >= _thumbnails.Count) return;
            var cell = _thumbnails[idx]?.transform.parent;
            if (cell == null) return;
            var gen = MapGenerator.Instance;
            if (gen == null || gen.grid == null) return;
            foreach (TextMeshProUGUI tmp in cell.GetComponentsInChildren<TextMeshProUGUI>())
                tmp.text = TileTypeAbbr(gen.grid[x, y].type);
        }

        private void RefreshThumbnailTexture(int x, int y, Texture2D tex)
        {
            int idx = ThumbnailIndex(x, y);
            if (idx >= 0 && idx < _thumbnails.Count && _thumbnails[idx] != null)
            {
                _thumbnails[idx].texture = tex;
                _thumbnails[idx].color   = tex != null ? Color.white : new Color32(0x4A, 0x38, 0x20, 0xFF);
            }
        }

        /// <summary>Thumbnail array is populated row-major top-to-bottom (y=height-1..0, x=0..width-1).</summary>
        private int ThumbnailIndex(int x, int y)
        {
            var gen = MapGenerator.Instance;
            if (gen == null) return -1;
            int row = (gen.height - 1) - y;
            return row * gen.width + x;
        }
    }
}
