using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace DnD.UI
{
    /// <summary>
    /// In-game overlay menu: Save, Load/Main Menu, and per-tile regeneration.
    /// Opened by the MENU HUD button; wired to GameManager via public Actions.
    /// </summary>
    public class InGameMenuPanel : MonoBehaviour
    {
        public static InGameMenuPanel Instance { get; private set; }

        // Events wired by GameManager
        public System.Action OnSave;
        public System.Action OnLoad;
        public System.Action<int, int> OnRegenerateTile;

        // Set by UISceneBuilder
        [SerializeField] private GameObject tileListContainer;

        private readonly List<GameObject> _tileRows = new List<GameObject>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        public void Open(int playerX, int playerY)
        {
            gameObject.SetActive(true);
            RebuildTileList(playerX, playerY);
        }

        public void Close() => gameObject.SetActive(false);

        public void RebuildTileList(int px, int py)
        {
            foreach (var r in _tileRows) if (r != null) Destroy(r);
            _tileRows.Clear();
            if (tileListContainer == null) return;

            var gen = DNDLLM.Map.MapGenerator.Instance;
            if (gen == null || gen.grid == null) return;

            var slots = new (int x, int y, string label)[]
            {
                (px,     py,     "Current"),
                (px,     py + 1, "North"),
                (px,     py - 1, "South"),
                (px + 1, py,     "East"),
                (px - 1, py,     "West"),
            };

            foreach (var (x, y, label) in slots)
            {
                if (x < 0 || x >= gen.width || y < 0 || y >= gen.height) continue;
                var row = BuildTileRow(label, gen.grid[x, y].type.ToString(), x, y);
                _tileRows.Add(row);
            }
        }

        private GameObject BuildTileRow(string label, string tileType, int x, int y)
        {
            var row = new GameObject($"TileRow_{label}", typeof(RectTransform));
            row.transform.SetParent(tileListContainer.transform, false);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding                = new RectOffset(4, 4, 2, 2);
            hlg.spacing                = 6;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = false;
            hlg.childControlHeight     = true;
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 26;
            le.flexibleWidth   = 1;

            // Direction + tile type label
            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(row.transform, false);
            var labelLE = labelGO.AddComponent<LayoutElement>();
            labelLE.preferredWidth = 170;
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = $"{label}  [{tileType}]";
            tmp.fontSize  = 10f;
            tmp.color     = UITheme.SystemText;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Regenerate button
            var btnGO = new GameObject("RegenBtn", typeof(RectTransform));
            btnGO.transform.SetParent(row.transform, false);
            var btnLE = btnGO.AddComponent<LayoutElement>();
            btnLE.preferredWidth = 88;
            btnGO.AddComponent<Image>().color = new Color(0.2f, 0.15f, 0.1f);
            var btn = btnGO.AddComponent<Button>();
            var btnColors = btn.colors;
            btnColors.highlightedColor = new Color(0.35f, 0.25f, 0.1f);
            btn.colors = btnColors;

            var btnTextGO = new GameObject("Text", typeof(RectTransform));
            btnTextGO.transform.SetParent(btnGO.transform, false);
            var rt = btnTextGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var btnTMP = btnTextGO.AddComponent<TextMeshProUGUI>();
            btnTMP.text      = "Regenerate";
            btnTMP.fontSize  = 9f;
            btnTMP.color     = UITheme.GoldAccent;
            btnTMP.alignment = TextAlignmentOptions.Center;

            int cx = x, cy = y;
            btn.onClick.AddListener(() => OnRegenerateTile?.Invoke(cx, cy));

            return row;
        }
    }
}
