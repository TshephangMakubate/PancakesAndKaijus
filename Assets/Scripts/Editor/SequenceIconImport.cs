using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports the swap and shuffle artwork and wires it into the token palette.
/// <para>
/// Drop the two images into <c>Assets/Textures/SequenceIcons</c> named
/// <c>swap</c> and <c>shuffle</c> (png, jpg or svg), then run the menu item.
/// Each is re-imported as a sprite and assigned to its slot; the text glyph is
/// only used while a slot has no icon.
/// </para>
/// </summary>
public static class SequenceIconImport
{
    private const string IconFolder = "Assets/Textures/SequenceIcons";

    // "switch" is accepted too, since that is what the swap arrows are often called.
    private static readonly string[] SwapNames = { "swap", "switch" };
    private static readonly string[] ShuffleNames = { "shuffle" };

    private static readonly string[] Extensions = { ".png", ".psd", ".jpg", ".jpeg", ".svg" };

    [MenuItem("Waffle Party/Import Sequence Token Icons")]
    public static void ImportIcons()
    {
        EnsureFolder();

        Sprite swap = LoadIcon(SwapNames);
        Sprite shuffle = LoadIcon(ShuffleNames);

        if (swap == null && shuffle == null)
        {
            EditorUtility.DisplayDialog(
                "No icons found",
                $"Save your swap and shuffle images into:\n\n{IconFolder}\n\n" +
                "named 'swap' and 'shuffle' (png works fine), then run this again.\n\n" +
                "Until then the tokens fall back to their text glyphs.",
                "OK");
            return;
        }

        var palette = AssetDatabase.LoadAssetAtPath<SequenceTokenPalette>(SequenceSceneFactory.PalettePath);
        if (palette == null)
        {
            Debug.LogError($"Waffle Party: No palette at '{SequenceSceneFactory.PalettePath}'. Run a scene setup first.");
            return;
        }

        var settings = new SerializedObject(palette);
        int assigned = 0;

        assigned += AssignIcon(settings, "_swap", swap) ? 1 : 0;
        assigned += AssignIcon(settings, "_shuffle", shuffle) ? 1 : 0;

        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(palette);
        AssetDatabase.SaveAssets();

        Selection.activeObject = palette;
        Debug.Log($"Waffle Party: Assigned {assigned} sequence token icon(s) into the palette.");
    }

    private static bool AssignIcon(SerializedObject target, string field, Sprite sprite)
    {
        if (sprite == null)
        {
            return false;
        }

        SerializedProperty style = target.FindProperty(field);
        if (style == null)
        {
            Debug.LogError($"Waffle Party: SequenceTokenPalette has no field '{field}'.");
            return false;
        }

        style.FindPropertyRelative("_sprite").objectReferenceValue = sprite;
        return true;
    }

    /// <summary>Finds an image by any accepted base name and imports it as a sprite.</summary>
    private static Sprite LoadIcon(string[] baseNames)
    {
        foreach (string baseName in baseNames)
        {
            foreach (string extension in Extensions)
            {
                string path = $"{IconFolder}/{baseName}{extension}";
                if (!File.Exists(path))
                {
                    continue;
                }

                ConfigureAsSprite(path);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                {
                    return sprite;
                }

                Debug.LogWarning($"Waffle Party: '{path}' exists but did not import as a sprite.");
            }
        }

        return null;
    }

    /// <summary>Switches a texture to sprite import settings if it is not already.</summary>
    private static void ConfigureAsSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            // SVGs import through the vector package and already yield sprites.
            return;
        }

        if (importer.textureType == TextureImporterType.Sprite && importer.alphaIsTransparency)
        {
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Textures"))
        {
            AssetDatabase.CreateFolder("Assets", "Textures");
        }

        if (!AssetDatabase.IsValidFolder(IconFolder))
        {
            AssetDatabase.CreateFolder("Assets/Textures", "SequenceIcons");
        }
    }
}
