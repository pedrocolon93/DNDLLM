using UnityEngine;

namespace DNDLLM.Utils
{
    /// <summary>
    /// Removes the background from a generated sprite texture by flood-filling
    /// from all four corners and erasing pixels that match the background colour
    /// within a configurable tolerance.
    ///
    /// Works on any solid-background image (white, black, dark brown, etc.).
    /// Returns a new RGBA32 texture where background pixels have alpha = 0.
    /// </summary>
    public static class SpriteBackgroundRemover
    {
        /// <param name="source">Source texture. Must be readable (created via LoadImage or SetPixels).</param>
        /// <param name="tolerance">Euclidean RGB distance threshold (0-1). Default 0.32 handles most AI-generated backgrounds.</param>
        /// <param name="featherRadius">How many border pixels to also make semi-transparent (soft edge). 0 = sharp.</param>
        public static Texture2D RemoveBackground(Texture2D source,
                                                  float tolerance    = 0.32f,
                                                  int   featherRadius = 2)
        {
            if (source == null) return null;

            int w = source.width;
            int h = source.height;

            // ── Ensure readable pixels ──────────────────────────────────────
            Color[] src;
            try
            {
                src = source.GetPixels();
            }
            catch
            {
                // Texture is not readable — blit via RenderTexture
                src = CopyViaRenderTexture(source, w, h);
                if (src == null) return source; // can't process
            }

            // ── Sample background colour from all border pixels (median of each channel) ──
            // Using median instead of mean is more robust against bright/dark outliers
            // that occur at door edges or anti-aliased borders in AI-generated images.
            Color bg = SampleBorderMedian(src, w, h);

            // ── BFS flood-fill from every edge pixel ────────────────────────
            bool[] isBackground = new bool[w * h];
            bool[] visited      = new bool[w * h];
            var    queue        = new System.Collections.Generic.Queue<int>(w * h / 4);

            void TryEnqueue(int idx)
            {
                if ((uint)idx >= (uint)src.Length || visited[idx]) return;
                visited[idx] = true;
                if (ColorDist(src[idx], bg) <= tolerance)
                    queue.Enqueue(idx);
            }

            // Seed from all border pixels (not just corners) for robustness
            for (int x = 0; x < w; x++) { TryEnqueue(x);            TryEnqueue((h - 1) * w + x); }
            for (int y = 1; y < h - 1; y++) { TryEnqueue(y * w);    TryEnqueue(y * w + w - 1); }

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                isBackground[idx] = true;
                int x = idx % w, y = idx / w;
                if (x > 0)     TryEnqueue(idx - 1);
                if (x < w - 1) TryEnqueue(idx + 1);
                if (y > 0)     TryEnqueue(idx - w);
                if (y < h - 1) TryEnqueue(idx + w);
            }

            // ── Build output pixels ─────────────────────────────────────────
            Color[] dst = new Color[src.Length];
            System.Array.Copy(src, dst, src.Length);

            for (int i = 0; i < dst.Length; i++)
                if (isBackground[i])
                    dst[i] = Color.clear;

            // Optional feather: reduce alpha of pixels adjacent to the removed region
            if (featherRadius > 0)
            {
                for (int i = 0; i < dst.Length; i++)
                {
                    if (isBackground[i] || dst[i].a < 0.01f) continue;
                    int x = i % w, y = i / w;
                    float minDist = float.MaxValue;
                    for (int fy = -featherRadius; fy <= featherRadius; fy++)
                    for (int fx = -featherRadius; fx <= featherRadius; fx++)
                    {
                        int nx = x + fx, ny = y + fy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        if (isBackground[ny * w + nx])
                            minDist = Mathf.Min(minDist, Mathf.Sqrt(fx * fx + fy * fy));
                    }
                    if (minDist <= featherRadius)
                        dst[i].a = Mathf.Min(dst[i].a, minDist / featherRadius);
                }
            }

            // ── Create result texture ───────────────────────────────────────
            var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.SetPixels(dst);
            result.Apply();
            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the per-channel median colour of all pixels that lie on the texture's border.
        /// More robust than a corner average when the AI-generated background has a gradient.
        /// </summary>
        private static Color SampleBorderMedian(Color[] src, int w, int h)
        {
            var rs = new System.Collections.Generic.List<float>(w * 2 + h * 2);
            var gs = new System.Collections.Generic.List<float>(w * 2 + h * 2);
            var bs = new System.Collections.Generic.List<float>(w * 2 + h * 2);

            for (int x = 0; x < w; x++)
            {
                var bot = src[x];           rs.Add(bot.r); gs.Add(bot.g); bs.Add(bot.b);
                var top = src[(h-1)*w + x]; rs.Add(top.r); gs.Add(top.g); bs.Add(top.b);
            }
            for (int y = 1; y < h - 1; y++)
            {
                var lft = src[y*w];         rs.Add(lft.r); gs.Add(lft.g); bs.Add(lft.b);
                var rgt = src[y*w + w - 1]; rs.Add(rgt.r); gs.Add(rgt.g); bs.Add(rgt.b);
            }

            rs.Sort(); gs.Sort(); bs.Sort();
            int mid = rs.Count / 2;
            return new Color(rs[mid], gs[mid], bs[mid], 1f);
        }

        private static float ColorDist(Color a, Color b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static Color[] CopyViaRenderTexture(Texture2D source, int w, int h)
        {
            var rt  = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            try { return readable.GetPixels(); }
            catch { return null; }
        }
    }
}
