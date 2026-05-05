using UnityEngine;

namespace DNDLLM.Map
{
    /// <summary>
    /// Bakes a NxN grid overlay onto a base map texture. Mirrors the Python
    /// draw_grid_overlay routine: 3px white outer stroke + 1px black inner stroke per line.
    /// </summary>
    public static class MapImageOverlay
    {
        public static Texture2D DrawGridOverlay(Texture2D source, int size)
        {
            if (source == null || size <= 1) return source;

            int w = source.width;
            int h = source.height;

            // Copy into a fresh writable RGBA32 texture so we don't mutate the cached one.
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try { copy.SetPixels(source.GetPixels()); }
            catch (UnityException)
            {
                // Source not readable — skip overlay rather than crash. Caller should mark as readable upstream.
                Debug.LogWarning("[MapImageOverlay] Source texture is not readable; returning original.");
                Object.Destroy(copy);
                return source;
            }

            int tileW = w / size;
            int tileH = h / size;

            // N-1 internal vertical lines
            for (int i = 1; i < size; i++)
            {
                int xPx = i * tileW;
                FillRect(copy, xPx - 1, 0, 3, h, Color.white);  // 3px white outer
                FillRect(copy, xPx,     0, 1, h, Color.black);  // 1px black inner
            }
            // N-1 internal horizontal lines
            for (int i = 1; i < size; i++)
            {
                int yPx = i * tileH;
                FillRect(copy, 0, yPx - 1, w, 3, Color.white);
                FillRect(copy, 0, yPx,     w, 1, Color.black);
            }

            copy.Apply();
            return copy;
        }

        private static void FillRect(Texture2D tex, int x, int y, int width, int height, Color c)
        {
            int x0 = Mathf.Clamp(x, 0, tex.width);
            int y0 = Mathf.Clamp(y, 0, tex.height);
            int x1 = Mathf.Clamp(x + width,  0, tex.width);
            int y1 = Mathf.Clamp(y + height, 0, tex.height);
            int rw = x1 - x0;
            int rh = y1 - y0;
            if (rw <= 0 || rh <= 0) return;

            var pixels = new Color[rw * rh];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
            tex.SetPixels(x0, y0, rw, rh, pixels);
        }
    }
}
