using System.Collections.Generic;
using UnityEngine;
using DnD.Character;
using DnD.Core;
using DnD.Managers;

namespace DNDLLM.Map
{
    /// <summary>
    /// Renders one party-member's character as a sprite token on the map,
    /// positioned at their current grid cell and updated on movement.
    ///
    /// Multi-player foundation: <see cref="Instance"/> is kept for the ~28
    /// existing call sites and points at the active turn's controller. New
    /// callers should prefer <see cref="Current"/> (which mirrors Instance)
    /// or <see cref="For(CharacterStats)"/> for "move that specific player".
    /// </summary>
    public class MapCharacterController : MonoBehaviour
    {
        public static MapCharacterController Instance { get; private set; }
        public static readonly List<MapCharacterController> All = new List<MapCharacterController>();

        /// <summary>The CharacterStats this token is rendering for; assigned by GameManager
        /// after spawn so multi-player tools can resolve "which token belongs to which character".</summary>
        public CharacterStats Stats;

        /// <summary>Active controller — same as <see cref="Instance"/> for now; will diverge once
        /// multiple party tokens exist on the same map and the turn queue rotates control.</summary>
        public static MapCharacterController Current => Instance;

        private SpriteRenderer spriteRenderer;

        public int GridX { get; private set; }
        public int GridY { get; private set; }

        private void Awake()
        {
            // Hard cap at one controller for now (single-player UX). When the multi-player
            // UI lands, GameManager will instantiate additional MapCharacterController GOs
            // and we'll switch from "first wins" to per-player registration.
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
            All.Add(this);
        }

        private void OnDestroy()
        {
            All.Remove(this);
            if (Instance == this) Instance = null;
        }

        /// <summary>Look up the controller rendering a given CharacterStats, or null if none.</summary>
        public static MapCharacterController For(CharacterStats stats)
        {
            if (stats == null) return null;
            foreach (var c in All) if (c != null && c.Stats == stats) return c;
            return null;
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
                // Character occupies 55 % of a tile so it stays within the cell bounds
                float ppu = characterTex.width / (cs * 0.55f);
                var sprite = Sprite.Create(
                    characterTex,
                    new Rect(0, 0, characterTex.width, characterTex.height),
                    new Vector2(0.5f, 0.5f),
                    ppu);
                spriteRenderer.sprite = sprite;
            }

            MoveTo(startX, startY);
            if (MapGenerator.Instance != null)
                MapGenerator.Instance.RevealAround(startX, startY);
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
            MapGenerator.Instance.RevealAround(nx, ny);
            CheckEncounterAt(nx, ny);
            return true;
        }

        public int MoveTo(int tx, int ty, bool stepByStep)
        {
            if (!stepByStep) { MoveTo(tx, ty); return 0; }
            int steps = 0;
            int guard = 0;
            int max = (MapGenerator.Instance != null) ? (MapGenerator.Instance.width + MapGenerator.Instance.height) : 64;
            while ((GridX != tx || GridY != ty) && guard++ < max)
            {
                int sdx = tx > GridX ? 1 : (tx < GridX ? -1 : 0);
                int sdy = ty > GridY ? 1 : (ty < GridY ? -1 : 0);
                bool moved = false;
                if (sdx != 0 && TryMove(sdx, 0)) { steps++; moved = true; }
                else if (sdy != 0 && TryMove(0, sdy)) { steps++; moved = true; }
                if (!moved) break;
            }
            return steps;
        }

        public void MoveTo(int x, int y)
        {
            GridX = x;
            GridY = y;
            float cs = MapGenerator.Instance != null ? MapGenerator.Instance.cellSize : 1f;
            // y = 0.05 prevents Z-fighting with the floor tiles at y = 0
            transform.position = new Vector3(x * cs, 0.05f, y * cs);
        }

        private void CheckEncounterAt(int x, int y)
        {
            var gen = MapGenerator.Instance;
            if (gen?.grid == null) return;
            if (GameManager.Instance == null) return;
            if (GameManager.Instance.GetCurrentState() == GameState.Combat) return;

            bool hostileHere = false;
            string foeName = "Goblin";
            int foeHp = 7, foeAc = 12, foeDex = 14;
            foreach (var e in MapEntityController.All)
            {
                if (e == null || !e.IsEnemy || e.IsHidden) continue;
                if (e.GridX == x && e.GridY == y)
                {
                    hostileHere = true;
                    if (!string.IsNullOrEmpty(e.EntityName)) foeName = e.EntityName;
                    foeHp = e.HP > 0 ? e.HP : foeHp;
                    foeAc = e.AC > 0 ? e.AC : foeAc;
                    break;
                }
            }

            bool spawnTile = gen.grid[x, y].type == TileType.EnemySpawn;
            if (!hostileHere && !spawnTile) return;

            var enemyGO = new GameObject(foeName);
            var enemy = enemyGO.AddComponent<CharacterStats>();
            enemy.characterName    = foeName;
            enemy.maxHitPoints     = foeHp;
            enemy.currentHitPoints = foeHp;
            enemy.armorClass       = foeAc;
            enemy.abilities        = new AbilityScores(10, foeDex, 10, 8, 8, 8);

            if (spawnTile)
            {
                gen.grid[x, y].type     = TileType.Floor;
                gen.grid[x, y].walkable = true;
            }

            GameManager.Instance.EnterCombat(new List<CharacterStats> { enemy });
        }
    }
}
