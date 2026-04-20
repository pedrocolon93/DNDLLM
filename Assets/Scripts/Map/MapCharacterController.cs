using UnityEngine;

namespace DNDLLM.Map
{
    /// <summary>
    /// Renders the player's character as a sprite token on the map,
    /// positioned at their current grid cell and updated on movement.
    /// </summary>
    public class MapCharacterController : MonoBehaviour
    {
        public static MapCharacterController Instance { get; private set; }

        private SpriteRenderer spriteRenderer;

        public int GridX { get; private set; }
        public int GridY { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        /// <summary>
        /// Sets the character sprite and places it at the given grid cell.
        /// Safe to call again when the map regenerates.
        /// </summary>
        public void Initialize(Texture2D characterTex, int startX, int startY)
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>()
                              ?? gameObject.AddComponent<SpriteRenderer>();

            spriteRenderer.sortingOrder = 2; // above floor tiles (order 0)
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (characterTex != null)
            {
                float cs  = MapGenerator.Instance != null ? MapGenerator.Instance.cellSize : 1f;
                // Character occupies 70 % of a tile so it stays within the cell bounds
                float ppu = characterTex.width / (cs * 0.7f);
                var sprite = Sprite.Create(
                    characterTex,
                    new Rect(0, 0, characterTex.width, characterTex.height),
                    new Vector2(0.5f, 0.5f),
                    ppu);
                spriteRenderer.sprite = sprite;
            }

            MoveTo(startX, startY);
        }

        /// <summary>
        /// Attempts to move by (dx, dy) grid steps.
        /// Returns false if the destination is out of bounds or not walkable.
        /// </summary>
        public bool TryMove(int dx, int dy)
        {
            if (MapGenerator.Instance?.grid == null) return false;
            int nx = GridX + dx;
            int ny = GridY + dy;
            if (nx < 0 || nx >= MapGenerator.Instance.width)  return false;
            if (ny < 0 || ny >= MapGenerator.Instance.height) return false;
            if (!MapGenerator.Instance.grid[nx, ny].walkable) return false;
            MoveTo(nx, ny);
            return true;
        }

        public void MoveTo(int x, int y)
        {
            GridX = x;
            GridY = y;
            float cs = MapGenerator.Instance != null ? MapGenerator.Instance.cellSize : 1f;
            // y = 0.05 prevents Z-fighting with the floor tiles at y = 0
            transform.position = new Vector3(x * cs, 0.05f, y * cs);
        }
    }
}
