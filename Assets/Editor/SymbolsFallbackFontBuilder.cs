// Assets/Editor/SymbolsFallbackFontBuilder.cs
//
// Creates a TMP_FontAsset from a Unicode-rich source font (Apple Symbols on macOS,
// otherwise prompts the user) and adds it to LiberationSans SDF's fallback table.
// One-time setup so glyphs in Dingbats, Geometric Shapes, and other symbol blocks
// render at runtime without "character not found" warnings.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;

public static class SymbolsFallbackFontBuilder
{
    private const string PrimaryFontAssetPath  = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string FallbackFontAssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Symbols SDF.asset";
    private const string CopiedTtfPath         = "Assets/Fonts/AppleSymbols.ttf";

    private static readonly string[] CandidateSystemFonts =
    {
        "/System/Library/Fonts/Apple Symbols.ttf",
        "/System/Library/Fonts/Supplemental/Symbol.ttf",
    };

    [MenuItem("DnD/Build Symbols Fallback Font")]
    public static void Build()
    {
        // 1. Ensure a usable TTF lives under Assets/.
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(CopiedTtfPath);
        if (sourceFont == null)
        {
            string sysFont = null;
            foreach (var p in CandidateSystemFonts)
                if (File.Exists(p)) { sysFont = p; break; }

            if (sysFont == null)
            {
                EditorUtility.DisplayDialog(
                    "Symbols font not found",
                    $"Couldn't find a system symbol font in any of:\n  {string.Join("\n  ", CandidateSystemFonts)}\n\n" +
                    "Drop a Unicode-rich TTF (e.g. Noto Sans Symbols 2) at\n  " + CopiedTtfPath +
                    "\nthen run this menu item again.",
                    "OK");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CopiedTtfPath));
            File.Copy(sysFont, CopiedTtfPath, overwrite: true);
            AssetDatabase.ImportAsset(CopiedTtfPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[SymbolsFallback] Copied {sysFont} to {CopiedTtfPath}.");

            sourceFont = AssetDatabase.LoadAssetAtPath<Font>(CopiedTtfPath);
            if (sourceFont == null)
            {
                Debug.LogError("[SymbolsFallback] Imported the TTF but Unity didn't expose it as a Font asset. Aborting.");
                return;
            }
        }

        // 2. Create the TMP font asset in Dynamic mode (atlas grows on demand at runtime).
        var fallback = TMP_FontAsset.CreateFontAsset(
            font:                  sourceFont,
            samplingPointSize:     90,
            atlasPadding:          9,
            renderMode:            GlyphRenderMode.SDFAA,
            atlasWidth:            1024,
            atlasHeight:           1024,
            atlasPopulationMode:   AtlasPopulationMode.Dynamic,
            enableMultiAtlasSupport: true);

        if (fallback == null)
        {
            Debug.LogError("[SymbolsFallback] TMP_FontAsset.CreateFontAsset returned null.");
            return;
        }

        // 3. Save (overwrite if it already exists).
        Directory.CreateDirectory(Path.GetDirectoryName(FallbackFontAssetPath));
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontAssetPath);
        if (existing != null) AssetDatabase.DeleteAsset(FallbackFontAssetPath);
        AssetDatabase.CreateAsset(fallback, FallbackFontAssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SymbolsFallback] Created TMP font asset at {FallbackFontAssetPath} (Dynamic, source={sourceFont.name}).");

        // 4. Wire it into LiberationSans SDF's fallback table (skip if already there).
        var primary = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PrimaryFontAssetPath);
        if (primary == null)
        {
            Debug.LogError($"[SymbolsFallback] Primary font asset not found at {PrimaryFontAssetPath}. Add the fallback manually.");
            return;
        }

        if (primary.fallbackFontAssetTable == null)
            primary.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();

        if (!primary.fallbackFontAssetTable.Contains(fallback))
        {
            primary.fallbackFontAssetTable.Add(fallback);
            EditorUtility.SetDirty(primary);
            AssetDatabase.SaveAssets();
            Debug.Log("[SymbolsFallback] Added Symbols SDF to LiberationSans SDF fallback table.");
        }
        else
        {
            Debug.Log("[SymbolsFallback] Symbols SDF was already in the fallback table.");
        }

        EditorUtility.DisplayDialog(
            "Symbols fallback ready",
            "Created Symbols SDF and wired it into LiberationSans SDF fallback table.\n\n" +
            "Restart Play mode (or run a scene rebuild) — Dingbats / Geometric Shapes / " +
            "other symbol glyphs should now render without warnings.",
            "OK");
    }
}
