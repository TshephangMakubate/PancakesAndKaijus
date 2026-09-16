using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared construction for the co-op sequence mechanic's assets and UI, used by
/// both the standalone test scene and the upgrade pass over the real cooking
/// scene, so the two never drift apart.
/// </summary>
internal static class SequenceSceneFactory
{
    internal const string SettingsFolder = "Assets/Settings";
    internal const string PrefabFolder = "Assets/Prefabs";
    internal const string ConfigPath = SettingsFolder + "/SequenceConfig.asset";
    internal const string PalettePath = SettingsFolder + "/SequenceTokenPalette.asset";
    internal const string TokenPrefabPath = PrefabFolder + "/SequenceToken.prefab";
    internal const string CursorSpritePath = SettingsFolder + "/SequenceCursorTriangle.png";

    private const string UiSpritePath = "UI/Skin/UISprite.psd";
    private const float TokenSize = 96f;
    private const float IconSize = 62f;

    // The cursor marker: a triangle pointing down at the tile under it.
    private const int CursorTextureSize = 64;
    private static readonly Vector2 CursorSize = new Vector2(46f, 34f);

    // Plain tiles carry nothing at all, so an action icon sits dead centre.
    private static readonly Vector2 BadgeOffset = Vector2.zero;

    internal const float TokenSpacing = 120f;
    internal const float CursorHeight = 86f;

    // The round timer along the bottom of the screen, in canvas units.
    private static readonly Vector2 TimerBarSize = new Vector2(1000f, 46f);
    private const float TimerBarY = -104f;

    // Working area of a television's world-space canvas, in canvas units. Only
    // the canvas scale changes with the set's physical size.
    private const float ScreenCanvasWidth = 1200f;
    private const float ScreenCanvasHeight = 320f;
    private const float BezelThickness = 0.45f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // Plain tiles are colour and nothing else, so the channels carry no glyph.
    // Leaving the field in place means a shape cue can be restored later from
    // the palette without a code change.
    private const string OrangeGlyph = "";
    private const string BlueGlyph = "";

    // Text stand-ins, shown only until real action artwork is imported.
    private const string SwapGlyph = "<->";
    private const string ShuffleGlyph = "><";

    // Exactly two colours exist. Actions are badges drawn on top of a tile, so
    // they never introduce a colour of their own.
    private static readonly Color OrangeColor = new Color(0.95f, 0.55f, 0.15f);
    private static readonly Color BlueColor = new Color(0.25f, 0.6f, 0.95f);

    /// <summary>
    /// Creates every asset the mechanic needs, if it does not already exist.
    /// <para>
    /// Call this <em>before</em> opening or creating the scene, then use the
    /// Load* methods afterwards. References handed back by asset creation — the
    /// prefab's especially — are invalidated by the reimport that a scene change
    /// triggers, so they must be re-resolved once the scene is in place.
    /// </para>
    /// </summary>
    internal static void EnsureAssets()
    {
        EnsureFolder("Assets", "Settings");
        EnsureFolder("Assets", "Prefabs");

        LoadOrCreatePalette();
        LoadOrCreateConfig();
        LoadOrCreateTokenPrefab();
        LoadOrCreateCursorSprite();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>Loads the palette asset. Call after the scene is open.</summary>
    internal static SequenceTokenPalette LoadPalette()
    {
        return AssetDatabase.LoadAssetAtPath<SequenceTokenPalette>(PalettePath);
    }

    /// <summary>Loads the tuning asset. Call after the scene is open.</summary>
    internal static SequenceConfig LoadConfig()
    {
        return AssetDatabase.LoadAssetAtPath<SequenceConfig>(ConfigPath);
    }

    /// <summary>Loads the cursor's triangle sprite. Call after the scene is open.</summary>
    internal static Sprite LoadCursorSprite()
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(CursorSpritePath);
    }

    /// <summary>Loads the token prefab. Call after the scene is open.</summary>
    internal static SequenceTokenView LoadTokenPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<SequenceTokenView>(TokenPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"Waffle Party: Could not load the token prefab at '{TokenPrefabPath}'. The station will have no tokens.");
        }

        return prefab;
    }

    /// <summary>Loads the palette asset, creating it with placeholder art on first run.</summary>
    private static SequenceTokenPalette LoadOrCreatePalette()
    {
        var palette = AssetDatabase.LoadAssetAtPath<SequenceTokenPalette>(PalettePath);
        if (palette != null)
        {
            MigrateStalePalette(palette);
            return palette;
        }

        palette = ScriptableObject.CreateInstance<SequenceTokenPalette>();
        AssetDatabase.CreateAsset(palette, PalettePath);

        // Badge icons start empty so their text stand-ins show; Import Sequence
        // Token Icons fills them in once the art exists.
        var settings = new SerializedObject(palette);
        ApplyDefaultStyles(settings);
        settings.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(palette);
        return palette;
    }

    /// <summary>
    /// Draws the cursor's downward triangle into a sprite on first run. Rendering
    /// it once as a texture keeps the marker a plain <see cref="Image"/>, so it
    /// still tints from the palette and still slides between slots the way the
    /// old box did.
    /// </summary>
    private static Sprite LoadOrCreateCursorSprite()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(CursorSpritePath);
        if (existing != null)
        {
            return existing;
        }

        const int size = CursorTextureSize;
        var pixels = new Color[size * size];
        float centre = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            // Texture rows run bottom-up, so row 0 is the tip of the arrow and
            // the triangle widens towards the top.
            float halfWidth = (y / (float)(size - 1)) * centre;

            for (int x = 0; x < size; x++)
            {
                // Coverage rather than a hard test, so the sloped edges are not
                // a staircase once the marker is scaled onto the screen.
                float coverage = Mathf.Clamp01(halfWidth - Mathf.Abs(x - centre) + 0.5f);
                pixels[(y * size) + x] = new Color(1f, 1f, 1f, coverage);
            }
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(CursorSpritePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(CursorSpritePath, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(CursorSpritePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(CursorSpritePath);
    }

    /// <summary>Loads the tuning asset, creating it with defaults on first run.</summary>
    private static SequenceConfig LoadOrCreateConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<SequenceConfig>(ConfigPath);
        if (config != null)
        {
            return config;
        }

        config = ScriptableObject.CreateInstance<SequenceConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);
        EditorUtility.SetDirty(config);
        return config;
    }

    /// <summary>
    /// Builds the placeholder token prefab: a tinted box carrying either an icon
    /// or a text glyph. Rebuilds an older prefab that predates the icon layer.
    /// </summary>
    private static void LoadOrCreateTokenPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<SequenceTokenView>(TokenPrefabPath);
        if (existing != null)
        {
            if (HasBadgeLayer(existing))
            {
                return;
            }

            // Generated placeholder art, so replacing it loses nothing authored.
            Debug.Log("Waffle Party: Rebuilding the token prefab to add the action badge layer.");
            AssetDatabase.DeleteAsset(TokenPrefabPath);
        }

        var root = new GameObject("SequenceToken", typeof(RectTransform), typeof(Image), typeof(SequenceTokenView));
        var rect = (RectTransform)root.transform;
        CentreAnchors(rect);
        rect.sizeDelta = new Vector2(TokenSize, TokenSize);

        Image image = root.GetComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(UiSpritePath);
        image.type = Image.Type.Sliced;

        TextMeshProUGUI glyph = CreateText(rect, "Glyph", 34f, TextAlignmentOptions.Center);
        RectTransform glyphRect = glyph.rectTransform;
        glyphRect.anchorMin = Vector2.zero;
        glyphRect.anchorMax = Vector2.one;
        glyphRect.offsetMin = Vector2.zero;
        glyphRect.offsetMax = Vector2.zero;
        glyph.color = new Color(0f, 0f, 0f, 0.75f);

        // Only action tiles show anything, so the icon owns the centre.
        var iconObject = new GameObject("ActionIcon", typeof(RectTransform), typeof(Image));
        var iconRect = (RectTransform)iconObject.transform;
        iconRect.SetParent(rect, false);
        CentreAnchors(iconRect);
        iconRect.sizeDelta = new Vector2(IconSize, IconSize);
        iconRect.anchoredPosition = BadgeOffset;

        Image icon = iconObject.GetComponent<Image>();

        // White leaves the artwork exactly as authored.
        icon.color = Color.white;
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        icon.enabled = false;

        TextMeshProUGUI badge = CreateText(rect, "ActionBadge", 30f, TextAlignmentOptions.Center);
        badge.color = new Color(0.1f, 0.1f, 0.12f);
        PlaceLabel(badge.rectTransform, BadgeOffset, new Vector2(IconSize, IconSize));

        var view = root.GetComponent<SequenceTokenView>();
        var viewSettings = new SerializedObject(view);
        DecoratingSceneSetup.Assign(viewSettings, "_image", image);
        DecoratingSceneSetup.Assign(viewSettings, "_icon", icon);
        DecoratingSceneSetup.Assign(viewSettings, "_glyphText", glyph);
        DecoratingSceneSetup.Assign(viewSettings, "_badgeText", badge);
        viewSettings.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, TokenPrefabPath);
        Object.DestroyImmediate(root);
    }

    /// <summary>
    /// Builds a wall-mounted television and returns the world-space canvas on
    /// its screen, ready to hold a station.
    /// <para>
    /// The canvas keeps the same 1200x320 working area whatever the set's
    /// physical size, so station layout constants stay in canvas units and only
    /// the scale changes.
    /// </para>
    /// </summary>
    internal static RectTransform BuildTelevision(string name, Vector3 centre, float width, float height)
    {
        var tv = new GameObject(name);
        tv.transform.position = centre;

        Material bezelMaterial = SaveMaterial("SequenceTvBezel", new Color(0.09f, 0.09f, 0.11f));
        Material screenMaterial = SaveMaterial("SequenceTvScreen", new Color(0.04f, 0.05f, 0.09f));

        // Bezel, then a slightly smaller screen face sitting just in front of it.
        CreateBox("Bezel", tv.transform, new Vector3(0f, 0f, 0.06f),
            new Vector3(width + BezelThickness, height + BezelThickness, 0.25f), bezelMaterial);
        CreateBox("Screen", tv.transform, new Vector3(0f, 0f, -0.08f),
            new Vector3(width, height, 0.05f), screenMaterial);

        var canvasObject = new GameObject("ScreenCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(tv.transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = new Vector2(ScreenCanvasWidth, ScreenCanvasHeight);

        // Scale the working area onto the screen and sit just in front of it.
        float scale = width / ScreenCanvasWidth;
        canvasRect.localScale = new Vector3(scale, scale, scale);
        canvasRect.localPosition = new Vector3(0f, 0f, -0.12f);

        // Identity rotation, deliberately. The camera looks down +Z, so canvas
        // +X already maps to screen-right; rotating the canvas to "face" the
        // camera would mirror the row and put the first token on the right.
        canvasRect.localRotation = Quaternion.identity;

        return canvasRect;
    }

    /// <summary>Creates a cube part with its collider stripped, as scene dressing.</summary>
    private static Transform CreateBox(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        var renderer = part.GetComponent<MeshRenderer>();
        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }

        return part.transform;
    }

    /// <summary>Loads or creates a flat URP material, matching how the other setup scripts make props.</summary>
    private static Material SaveMaterial(string name, Color color)
    {
        string path = $"Assets/Materials/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader);
        material.SetColor(BaseColorId, color);
        material.color = color;

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>
    /// Builds one team's station under <paramref name="parent"/> and wires its
    /// <see cref="SequenceStationView"/> references.
    /// </summary>
    internal static SequenceStationView BuildStation(
        Transform parent,
        string name,
        float verticalOffset,
        SequenceTokenView tokenPrefab,
        SequenceTokenPalette palette)
    {
        var stationObject = new GameObject(name, typeof(RectTransform), typeof(SequenceStationView));
        var station = (RectTransform)stationObject.transform;
        station.SetParent(parent, false);
        station.anchorMin = new Vector2(0.5f, 0.5f);
        station.anchorMax = new Vector2(0.5f, 0.5f);
        station.pivot = new Vector2(0.5f, 0.5f);
        station.anchoredPosition = new Vector2(0f, verticalOffset);
        station.sizeDelta = new Vector2(1100f, 260f);

        // The token row is the origin the view lays tokens and the cursor out from.
        var rowObject = new GameObject("TokenRow", typeof(RectTransform));
        var row = (RectTransform)rowObject.transform;
        row.SetParent(station, false);
        CentreAnchors(row);
        row.anchoredPosition = Vector2.zero;
        row.sizeDelta = Vector2.zero;

        Image cursor = CreateImage(row, "Cursor", CursorSize, palette != null ? palette.CursorColor : Color.yellow);
        cursor.rectTransform.anchoredPosition = new Vector2(0f, CursorHeight);

        // A triangle, not a box: it points down at the tile it is marking.
        // Simple rather than the sliced fill CreateImage hands out, which would
        // chop the sloped edges into corner and edge regions.
        cursor.sprite = LoadCursorSprite();
        cursor.type = Image.Type.Simple;

        // A thick, square-edged bar: no sprite, so there are no rounded corners.
        // The station view sizes the fill by its anchors from the round clock.
        Image timerBackground = CreateImage(station, "TimerBackground", TimerBarSize, new Color(0f, 0f, 0f, 0.55f));
        timerBackground.sprite = null;
        timerBackground.type = Image.Type.Simple;
        timerBackground.rectTransform.anchoredPosition = new Vector2(0f, TimerBarY);

        Image timerBar = CreateImage(timerBackground.rectTransform, "TimerFill", TimerBarSize, new Color(0.4f, 0.9f, 0.5f));
        timerBar.sprite = null;
        timerBar.type = Image.Type.Simple;
        RectTransform timerFill = timerBar.rectTransform;
        timerFill.anchorMin = Vector2.zero;
        timerFill.anchorMax = Vector2.one;
        timerFill.pivot = new Vector2(0f, 0.5f);
        timerFill.offsetMin = Vector2.zero;
        timerFill.offsetMax = Vector2.zero;

        GameObject indicator = BuildSwappedIndicator(station);

        var view = stationObject.GetComponent<SequenceStationView>();
        var settings = new SerializedObject(view);
        DecoratingSceneSetup.Assign(settings, "_tokenRow", row);
        DecoratingSceneSetup.Assign(settings, "_tokenPrefab", tokenPrefab);
        DecoratingSceneSetup.Assign(settings, "_cursor", cursor.rectTransform);
        DecoratingSceneSetup.Assign(settings, "_timerBar", timerBar);
        DecoratingSceneSetup.Assign(settings, "_swappedIndicator", indicator);
        DecoratingSceneSetup.Assign(settings, "_palette", palette);
        settings.FindProperty("_tokenSpacing").floatValue = TokenSpacing;
        settings.FindProperty("_cursorHeight").floatValue = CursorHeight;
        settings.ApplyModifiedPropertiesWithoutUndo();

        return view;
    }

    private static GameObject BuildSwappedIndicator(Transform parent)
    {
        var indicatorObject = new GameObject("SwappedIndicator", typeof(RectTransform));
        var indicatorRect = (RectTransform)indicatorObject.transform;
        indicatorRect.SetParent(parent, false);

        TextMeshProUGUI indicatorText = CreateText(indicatorRect, "Text", 24f, TextAlignmentOptions.Center);
        indicatorText.text = "SWAPPED";
        indicatorText.color = new Color(1f, 0.75f, 0.2f);
        PlaceLabel(indicatorText.rectTransform, Vector2.zero, new Vector2(220f, 36f));
        PlaceLabel(indicatorRect, new Vector2(160f, 108f), new Vector2(220f, 36f));

        indicatorObject.SetActive(false);
        return indicatorObject;
    }

    internal static Image CreateImage(Transform parent, string name, Vector2 size, Color color)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)imageObject.transform;
        rect.SetParent(parent, false);
        CentreAnchors(rect);
        rect.sizeDelta = size;

        Image image = imageObject.GetComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(UiSpritePath);
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;

        return image;
    }

    internal static TextMeshProUGUI CreateText(Transform parent, string name, float size, TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        var text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;

        return text;
    }

    internal static void PlaceLabel(RectTransform rect, Vector2 position, Vector2 size)
    {
        CentreAnchors(rect);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    /// <summary>
    /// Anchors a rect to its parent's centre. The token row's slot maths treats
    /// anchoredPosition as an offset from the centre, so every piece it lays out
    /// must be anchored the same way.
    /// </summary>
    private static void CentreAnchors(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    /// <summary>Writes the placeholder two-colour styling into a palette.</summary>
    private static void ApplyDefaultStyles(SerializedObject target)
    {
        ApplyChannel(target, "_orange", OrangeColor, OrangeGlyph);
        ApplyChannel(target, "_blue", BlueColor, BlueGlyph);
        ApplyBadge(target, "_swap", SwapGlyph);
        ApplyBadge(target, "_shuffle", ShuffleGlyph);
    }

    private static void ApplyChannel(SerializedObject target, string field, Color color, string glyph)
    {
        SerializedProperty style = Style(target, field);
        if (style == null)
        {
            return;
        }

        style.FindPropertyRelative("_color").colorValue = color;
        style.FindPropertyRelative("_glyph").stringValue = glyph;
    }

    private static void ApplyBadge(SerializedObject target, string field, string glyph)
    {
        SerializedProperty style = Style(target, field);
        if (style == null)
        {
            return;
        }

        style.FindPropertyRelative("_sprite").objectReferenceValue = null;
        style.FindPropertyRelative("_glyph").stringValue = glyph;
    }

    private static SerializedProperty Style(SerializedObject target, string field)
    {
        SerializedProperty style = target.FindProperty(field);
        if (style == null)
        {
            Debug.LogError($"Waffle Party: SequenceTokenPalette has no field '{field}'.");
        }

        return style;
    }

    // Placeholder values written by earlier versions of this factory. Finding
    // one means the palette is still generated art, never hand-authored.
    private static readonly string[] LegacyChannelGlyphs = { "<>", "[]" };

    /// <summary>
    /// Clears leftovers from earlier versions of the placeholder palette: badge
    /// slots pointing at the built-in UI box (they were tile backgrounds once,
    /// and would now be stamped on top of every action tile), and the channel
    /// shape glyphs that plain tiles no longer show.
    /// <para>
    /// Each is cleared independently and nothing else is touched, so running
    /// this after icons have been imported cannot destroy them.
    /// </para>
    /// </summary>
    private static void MigrateStalePalette(SequenceTokenPalette palette)
    {
        var settings = new SerializedObject(palette);
        bool changed = false;

        Sprite uiBox = AssetDatabase.GetBuiltinExtraResource<Sprite>(UiSpritePath);
        if (uiBox != null)
        {
            changed |= ClearBadgeSprite(settings, "_swap", uiBox);
            changed |= ClearBadgeSprite(settings, "_shuffle", uiBox);
        }

        changed |= ClearLegacyGlyph(settings, "_orange");
        changed |= ClearLegacyGlyph(settings, "_blue");

        if (!changed)
        {
            return;
        }

        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(palette);
        Debug.Log("Waffle Party: Cleaned up leftovers in the token palette from an earlier layout.");
    }

    private static bool ClearBadgeSprite(SerializedObject target, string field, Sprite uiBox)
    {
        SerializedProperty sprite = target.FindProperty(field)?.FindPropertyRelative("_sprite");
        if (sprite == null || sprite.objectReferenceValue != uiBox)
        {
            return false;
        }

        sprite.objectReferenceValue = null;
        return true;
    }

    private static bool ClearLegacyGlyph(SerializedObject target, string field)
    {
        SerializedProperty glyph = target.FindProperty(field)?.FindPropertyRelative("_glyph");
        if (glyph == null || System.Array.IndexOf(LegacyChannelGlyphs, glyph.stringValue) < 0)
        {
            return false;
        }

        glyph.stringValue = string.Empty;
        return true;
    }

    /// <summary>True when a prefab already carries both halves of the action badge.</summary>
    private static bool HasBadgeLayer(SequenceTokenView view)
    {
        var settings = new SerializedObject(view);
        return IsAssigned(settings, "_icon") && IsAssigned(settings, "_badgeText");
    }

    private static bool IsAssigned(SerializedObject target, string field)
    {
        SerializedProperty property = target.FindProperty(field);
        return property != null && property.objectReferenceValue != null;
    }

    private static void EnsureFolder(string parent, string folder)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{folder}"))
        {
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
