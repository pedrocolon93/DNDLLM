using System.Collections.Generic;

namespace DNDLLM.Map
{
    /// <summary>
    /// Logical grid produced by the LLM in Strategy D — distributes story features across an NxN map
    /// before any image is generated. Mirrors the Python Pydantic schema in DndMapGenerator/main.py.
    /// </summary>
    [System.Serializable]
    public class LogicalTile
    {
        public int x;
        public int y;
        public string terrain_type;   // "grass", "dirt", "stone", "water", "wall", ...
        public string feature;        // "tavern", "monastery", "armory", or null/empty
        public string description;    // visual description for the image generator
    }

    [System.Serializable]
    public class LogicalGrid
    {
        public int size;
        public List<LogicalTile> tiles = new List<LogicalTile>();

        public LogicalTile GetTile(int x, int y)
        {
            if (tiles == null) return null;
            foreach (var t in tiles) if (t.x == x && t.y == y) return t;
            return null;
        }

        /// <summary>Heuristic: terrain keywords that block movement.</summary>
        public static bool IsBlockingTerrain(string terrain)
        {
            if (string.IsNullOrEmpty(terrain)) return false;
            string t = terrain.ToLowerInvariant();
            return t.Contains("wall") || t.Contains("cliff") || t.Contains("water")
                || t.Contains("void") || t.Contains("lava") || t.Contains("chasm")
                || t.Contains("pit");
        }
    }
}
