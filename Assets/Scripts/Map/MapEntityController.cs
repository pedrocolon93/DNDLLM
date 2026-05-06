using UnityEngine;
using System.Collections.Generic;

namespace DNDLLM.Map
{
    /// <summary>
    /// Sprite token for an enemy or NPC placed on the map grid.
    /// Use ClearAll() when transitioning between maps.
    /// </summary>
    public class MapEntityController : MonoBehaviour
    {
        public static readonly List<MapEntityController> All = new List<MapEntityController>();

        public string EntityName;
        public int HP, MaxHP, AC;
        public bool IsEnemy;
        public int GridX, GridY;

        private SpriteRenderer _sr;
        private bool _isHidden;

        /// <summary>
        /// When true, the sprite is not rendered. The entity still exists in <see cref="All"/>
        /// so the DM can target it via REVEAL_ENTITY / KILL_ENTITY tools, and saves persist
        /// the flag so reloads keep concealment intact.
        /// </summary>
        public bool IsHidden
        {
            get => _isHidden;
            set
            {
                _isHidden = value;
                if (_sr != null) _sr.enabled = !value;
            }
        }

        private void Awake()     => All.Add(this);
        private void OnDestroy() => All.Remove(this);

        public static void ClearAll()
        {
            for (int i = All.Count - 1; i >= 0; i--)
                if (All[i] != null) Destroy(All[i].gameObject);
            All.Clear();
        }

        public void Initialize(Texture2D tex, string name, int x, int y,
                               int hp, int ac, bool isEnemy, bool isHidden = false)
        {
            EntityName = name;
            HP = MaxHP = hp;
            AC = ac;
            IsEnemy = isEnemy;

            _sr = GetComponent<SpriteRenderer>()
               ?? gameObject.AddComponent<SpriteRenderer>();
            _sr.sortingOrder = 2;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (tex != null)
            {
                float cs  = MapGenerator.Instance != null ? MapGenerator.Instance.cellSize : 1f;
                float ppu = tex.width / (cs * 0.45f);
                _sr.sprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    ppu);
            }

            // Apply concealment last — needs _sr already wired up
            IsHidden = isHidden;

            PlaceAt(x, y);
        }

        public void PlaceAt(int x, int y)
        {
            GridX = x;
            GridY = y;
            float cs = MapGenerator.Instance != null ? MapGenerator.Instance.cellSize : 1f;
            transform.position = new Vector3(x * cs, 0.08f, y * cs);
        }
    }
}
