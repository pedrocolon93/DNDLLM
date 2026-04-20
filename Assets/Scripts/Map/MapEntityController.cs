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

        private void Awake()     => All.Add(this);
        private void OnDestroy() => All.Remove(this);

        public static void ClearAll()
        {
            for (int i = All.Count - 1; i >= 0; i--)
                if (All[i] != null) Destroy(All[i].gameObject);
            All.Clear();
        }

        public void Initialize(Texture2D tex, string name, int x, int y,
                               int hp, int ac, bool isEnemy)
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
                float ppu = tex.width / (cs * 0.6f);
                _sr.sprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    ppu);
            }

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
