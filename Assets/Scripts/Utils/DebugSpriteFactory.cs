using UnityEngine;

namespace DNDLLM.Utils
{
    public static class DebugSpriteFactory
    {
        public static Texture2D MakeSolid(Color color, int size = 64)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = color;
            t.SetPixels(px); t.Apply();
            return t;
        }

        public static Color ColorForTerrain(string terrain)
        {
            string s = (terrain ?? "").ToLowerInvariant();
            if (s.Contains("grass"))  return new Color(0.45f, 0.70f, 0.35f);
            if (s.Contains("dirt"))   return new Color(0.55f, 0.42f, 0.28f);
            if (s.Contains("stone"))  return new Color(0.55f, 0.55f, 0.55f);
            if (s.Contains("cobble")) return new Color(0.60f, 0.58f, 0.52f);
            if (s.Contains("wood"))   return new Color(0.50f, 0.32f, 0.18f);
            if (s.Contains("water"))  return new Color(0.25f, 0.45f, 0.75f);
            if (s.Contains("sand"))   return new Color(0.85f, 0.78f, 0.55f);
            if (s.Contains("wall"))   return new Color(0.25f, 0.22f, 0.20f);
            if (s.Contains("cliff"))  return new Color(0.30f, 0.28f, 0.26f);
            if (s.Contains("lava"))   return new Color(0.85f, 0.30f, 0.10f);
            if (s.Contains("void"))   return new Color(0.05f, 0.05f, 0.08f);
            return new Color(0.50f, 0.50f, 0.50f);
        }

        public enum Shape { None, Square, Circle, Triangle, Diamond, Cross, Star }

        public static (Shape shape, Color color) BadgeForFeature(string feature)
        {
            string s = (feature ?? "").ToLowerInvariant();
            if (string.IsNullOrEmpty(s)) return (Shape.None, Color.white);
            if (s.Contains("tavern")  || s.Contains("inn"))           return (Shape.Square,   new Color(0.95f, 0.65f, 0.20f));
            if (s.Contains("market")  || s.Contains("shop")  || s.Contains("stall"))   return (Shape.Square, new Color(0.85f, 0.75f, 0.20f));
            if (s.Contains("house")   || s.Contains("hut")   || s.Contains("home"))    return (Shape.Square, new Color(0.70f, 0.50f, 0.30f));
            if (s.Contains("monaster") || s.Contains("temple") || s.Contains("shrine")) return (Shape.Cross, new Color(0.95f, 0.95f, 0.80f));
            if (s.Contains("armory")  || s.Contains("forge"))          return (Shape.Square,   new Color(0.45f, 0.45f, 0.55f));
            if (s.Contains("fountain")|| s.Contains("well"))           return (Shape.Circle,   new Color(0.40f, 0.70f, 0.95f));
            if (s.Contains("chest")   || s.Contains("treasure"))       return (Shape.Diamond,  new Color(0.95f, 0.80f, 0.20f));
            if (s.Contains("monster") || s.Contains("lair")  || s.Contains("enemy"))   return (Shape.Triangle, new Color(0.90f, 0.20f, 0.20f));
            if (s.Contains("exit")    || s.Contains("portal")|| s.Contains("stair"))   return (Shape.Cross,    new Color(0.70f, 0.40f, 0.90f));
            if (s.Contains("door")    || s.Contains("gate"))           return (Shape.Square,   new Color(0.40f, 0.25f, 0.10f));
            if (s.Contains("npc")     || s.Contains("merchant") || s.Contains("guard") || s.Contains("villager"))
                return (Shape.Circle, new Color(0.35f, 0.85f, 0.35f));
            if (s.Contains("statue")  || s.Contains("monument"))       return (Shape.Star,     new Color(0.85f, 0.85f, 0.85f));
            if (s.Contains("tree")    || s.Contains("forest"))         return (Shape.Triangle, new Color(0.20f, 0.55f, 0.20f));
            return (Shape.Star, Color.white);
        }

        public static void DrawShape(Texture2D tex, int cx, int cy, int radius, Shape shape, Color color)
        {
            if (tex == null || shape == Shape.None || radius <= 0) return;
            int w = tex.width, h = tex.height;
            int x0 = Mathf.Max(0, cx - radius), x1 = Mathf.Min(w - 1, cx + radius);
            int y0 = Mathf.Max(0, cy - radius), y1 = Mathf.Min(h - 1, cy + radius);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - cx, dy = y - cy;
                bool inside = false;
                switch (shape)
                {
                    case Shape.Square:   inside = Mathf.Abs(dx) <= radius && Mathf.Abs(dy) <= radius; break;
                    case Shape.Circle:   inside = dx * dx + dy * dy <= radius * radius; break;
                    case Shape.Diamond:  inside = Mathf.Abs(dx) + Mathf.Abs(dy) <= radius; break;
                    case Shape.Triangle: inside = dy >= -radius && dy <= radius && Mathf.Abs(dx) <= (radius - dy) / 2 + 1; break;
                    case Shape.Cross:    inside = Mathf.Abs(dx) <= radius / 3 || Mathf.Abs(dy) <= radius / 3; break;
                    case Shape.Star:
                    {
                        int absDx = Mathf.Abs(dx), absDy = Mathf.Abs(dy);
                        inside = (absDx + absDy <= radius) || (absDx <= radius / 3 && absDy <= radius)
                              || (absDy <= radius / 3 && absDx <= radius);
                        break;
                    }
                }
                if (inside) tex.SetPixel(x, y, color);
            }
        }

        public static Texture2D MakeTile(string terrain, string feature, int size = 64)
        {
            var bg = ColorForTerrain(terrain);
            var tex = MakeSolid(bg, size);
            // light grid border so cells are easy to see
            for (int i = 0; i < size; i++)
            {
                tex.SetPixel(i, 0, Color.black);
                tex.SetPixel(i, size - 1, Color.black);
                tex.SetPixel(0, i, Color.black);
                tex.SetPixel(size - 1, i, Color.black);
            }
            var (shape, col) = BadgeForFeature(feature);
            if (shape != Shape.None) DrawShape(tex, size / 2, size / 2, size / 3, shape, col);
            tex.Apply();
            return tex;
        }

        public static Texture2D MakeToken(Color tint, Shape shape, int size = 64)
        {
            var tex = MakeSolid(new Color(0, 0, 0, 0), size);
            DrawShape(tex, size / 2, size / 2, size / 2 - 4, shape, tint);
            tex.Apply();
            return tex;
        }

        public static Texture2D MakeCharacterToken(int size = 64) =>
            MakeToken(new Color(0.20f, 0.55f, 0.95f), Shape.Circle, size);

        public static Texture2D MakeEntityToken(bool isEnemy, int size = 64) =>
            MakeToken(isEnemy ? new Color(0.90f, 0.20f, 0.20f) : new Color(0.35f, 0.85f, 0.35f),
                      isEnemy ? Shape.Triangle : Shape.Circle, size);

        public static Texture2D MakePortrait(string label, Color tint, int size = 256)
        {
            var tex = MakeSolid(new Color(0.15f, 0.12f, 0.10f), size);
            DrawShape(tex, size / 2, size / 2, size / 3, Shape.Circle, tint);
            tex.Apply();
            return tex;
        }
    }
}
