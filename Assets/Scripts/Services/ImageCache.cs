using UnityEngine;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DNDLLM.Services
{
    public static class ImageCache
    {
        private static string CacheDirectory => Path.Combine(Application.persistentDataPath, "ImageCache");

        public static void Init()
        {
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }

        public static Texture2D Load(string prompt)
        {
            Init();
            string filename = GetFilename(prompt);
            string path = Path.Combine(CacheDirectory, filename);

            if (File.Exists(path))
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes))
                {
                    return tex;
                }
            }
            return null;
        }

        public static void Save(string prompt, Texture2D texture)
        {
            Init();
            string filename = GetFilename(prompt);
            string path = Path.Combine(CacheDirectory, filename);

            byte[] bytes = texture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
        }

        private static string GetFilename(string prompt)
        {
            // Use Hash of the prompt to avoid filesystem issues with long/special char prompts
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(prompt));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString() + ".png";
            }
        }
    }
}
