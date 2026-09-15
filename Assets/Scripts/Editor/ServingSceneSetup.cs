using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Builds the Round 3 serving scene: a long gingham table with a halfway bar, four customers, your launch spot, the idle opponent plates, and the HUD.</summary>
public static class ServingSceneSetup
{
    private const string ServingScenePath = "Assets/Scenes/Serving Scene.unity";
    private const string MaterialFolder = "Assets/Materials/Serving";
    private const string TextureFolder = "Assets/Textures/Serving";
    private const string PancakePrefabPath = "Assets/Prefabs/Waffle.prefab";
    private const string GameConfigPath = "Assets/Settings/GameConfig.asset";
    private const string LitShaderName = "Universal Render Pipeline/Lit";

    private const float TableHalfWidth = 4.5f;
    private const float TableHalfLength = 12f;
    private const float HalfwayHalfDepth = 0.75f;
    private const float CustomerZoneDepth = 3.5f;
    private const float GinghamCell = 0.75f;
    private const float SurfaceLift = 0.01f;
    private const float CameraFieldOfView = 55f;
    private const int SlothIndex = 3;

    private static readonly Vector3 LaunchPosition = new Vector3(-1.4f, 0f, -10f);
    private static readonly Vector3[] OpponentPlatePositions = { new Vector3(1.4f, 0f, -10f), new Vector3(1.4f, 0f, 2.5f) };
    private static readonly Vector3 CameraPosition = new Vector3(0f, 19f, -19.5f);
    private static readonly Vector3 CameraTarget = new Vector3(0f, 0f, 1f);

    private static readonly string[] CustomerNames = { "Maya", "Leo", "Sam", "Sloth" };
    private static readonly Color[] ZoneColors =
    {
        new Color(0.38f, 0.16f, 0.9f),
        new Color(0.9f, 0.49f, 0.22f),
        new Color(0.6f, 0.85f, 0.4f),
        new Color(0.96f, 0.87f, 0.45f)
    };
    private static readonly Color[] ShirtColors =
    {
        new Color(0.86f, 0.25f, 0.35f),
        new Color(0.35f, 0.6f, 0.9f),
        new Color(0.5f, 0.55f, 1f),
        new Color(0.78f, 0.7f, 0.6f)
    };
    private static readonly Color[] HairColors =
    {
        new Color(0.35f, 0.18f, 0.1f),
        new Color(0.08f, 0.08f, 0.1f),
        new Color(0.45f, 0.3f, 0.15f),
        new Color(0.55f, 0.5f, 0.45f)
    };
    private static readonly Color SkinColor = new Color(1f, 0.84f, 0.7f);
    private static readonly Color SlothColor = new Color(0.78f, 0.72f, 0.64f);
    private static readonly Color DarkColor = new Color(0.12f, 0.08f, 0.08f);

    [MenuItem("Waffle Party/Create Serving Scene")]
    public static void CreateServingScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (File.Exists(ServingScenePath) && !EditorUtility.DisplayDialog(
                "Rebuild Serving Scene?",
                "'Serving Scene' already exists. Rebuild it from scratch? Any changes you made in it will be lost.",
                "Rebuild",
                "Cancel"))
        {
            return;
        }

        EnsureFolder("Assets/Materials", "Serving");
        EnsureFolder("Assets", "Textures");
        EnsureFolder("Assets/Textures", "Serving");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        SetUpCameraAndLight(scene);

        Transform table = BuildTable();
        CustomerZone[] customers = BuildCustomers(table);

        Transform launchSpot = CreateSpot("LaunchSpot", table, LaunchPosition, SaveMaterial("LaunchPadRed", new Color(0.95f, 0.55f, 0.55f)));
        Material bluePad = SaveMaterial("LaunchPadBlue", new Color(0.55f, 0.65f, 0.95f));
        var opponentSpots = new Transform[OpponentPlatePositions.Length];
        for (int i = 0; i < OpponentPlatePositions.Length; i++)
        {
            opponentSpots[i] = CreateSpot($"OpponentSpot{i + 1}", table, OpponentPlatePositions[i], bluePad);
        }

        HUDController hud = BuildHud();

        var managerObject = new GameObject("ServingManager");
        ServingManager manager = managerObject.AddComponent<ServingManager>();
        PancakeDecorator decorator = managerObject.AddComponent<PancakeDecorator>();

        var settings = new SerializedObject(manager);
        DecoratingSceneSetup.Assign(settings, "_config", AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath));
        DecoratingSceneSetup.Assign(settings, "_hud", hud);
        DecoratingSceneSetup.Assign(settings, "_decorator", decorator);
        DecoratingSceneSetup.Assign(settings, "_launchSpot", launchSpot);
        DecoratingSceneSetup.Assign(settings, "_tableCenter", table);
        DecoratingSceneSetup.Assign(settings, "_pancakePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PancakePrefabPath));
        DecoratingSceneSetup.Assign(settings, "_teamPlateMaterial", SaveMaterial("PlateRed", new Color(0.82f, 0.22f, 0.3f)));
        DecoratingSceneSetup.Assign(settings, "_opponentPlateMaterial", SaveMaterial("PlateBlue", new Color(0.2f, 0.3f, 0.75f)));
        DecoratingSceneSetup.Assign(settings, "_plateWellMaterial", SaveMaterial("PlateWell", new Color(0.97f, 0.95f, 0.9f)));
        settings.FindProperty("_tableHalfSize").vector2Value = new Vector2(TableHalfWidth, TableHalfLength);
        AssignArray(settings, "_customers", customers);
        AssignArray(settings, "_opponentPlateSpots", opponentSpots);
        settings.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ServingScenePath);
        DecoratingSceneSetup.EnsureInBuildSettings(ServingScenePath);
        Selection.activeGameObject = managerObject;

        Debug.Log("Waffle Party: Built 'Serving Scene' (Round 3) and added it to Build Settings.");
    }

    private static void SetUpCameraAndLight(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Camera camera = root.GetComponent<Camera>();
            if (camera != null)
            {
                camera.transform.position = CameraPosition;
                camera.transform.LookAt(CameraTarget);
                camera.fieldOfView = CameraFieldOfView;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.3f, 0.56f, 0.6f);
            }

            Light light = root.GetComponent<Light>();
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
            }
        }
    }

    private static Transform BuildTable()
    {
        Transform table = new GameObject("Table").transform;
        Material wood = SaveMaterial("TableWood", new Color(0.55f, 0.36f, 0.22f));

        CreatePart(PrimitiveType.Cube, "TableBody", table, new Vector3(0f, -0.5f, 0f), Quaternion.identity,
            new Vector3(TableHalfWidth * 2f, 1f, TableHalfLength * 2f), wood, keepCollider: true);

        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                CreatePart(PrimitiveType.Cube, "Leg", table, new Vector3(x * (TableHalfWidth - 0.5f), -2.5f, z * (TableHalfLength - 0.5f)),
                    Quaternion.identity, new Vector3(0.6f, 3f, 0.6f), wood);
            }
        }

        Texture2D green = SaveGingham("GinghamGreen", new Color(0.55f, 0.8f, 0.5f), new Color(0.75f, 0.9f, 0.7f), new Color(0.95f, 0.98f, 0.93f));
        Texture2D pink = SaveGingham("GinghamPink", new Color(0.86f, 0.5f, 0.52f), new Color(0.93f, 0.72f, 0.72f), new Color(0.99f, 0.95f, 0.95f));
        CreateSurface("P1Half", table, -TableHalfLength, -HalfwayHalfDepth, green, "GinghamGreen");
        CreateSurface("P2Half", table, HalfwayHalfDepth, TableHalfLength - CustomerZoneDepth, pink, "GinghamPink");

        CreatePart(PrimitiveType.Cube, "HalfwayPoint", table, new Vector3(0f, 0.012f, 0f), Quaternion.identity,
            new Vector3(TableHalfWidth * 2f + 2f, 0.01f, HalfwayHalfDepth * 2f), SaveMaterial("HalfwayBar", new Color(0.8f, 0.84f, 0.88f)));
        CreateFlatLabel("HalfwayLabel", table, new Vector3(0f, 0.03f, 0f), "HALFWAY POINT", 9f, DarkColor);

        CreatePart(PrimitiveType.Cube, "Floor", null, new Vector3(0f, -4.5f, 0f), Quaternion.identity,
            new Vector3(80f, 1f, 80f), SaveMaterial("Floor", new Color(0.62f, 0.52f, 0.4f)), keepCollider: true);

        return table;
    }

    private static CustomerZone[] BuildCustomers(Transform table)
    {
        float zoneWidth = TableHalfWidth * 2f / CustomerNames.Length;
        float zoneZ = TableHalfLength - CustomerZoneDepth * 0.5f;
        var zones = new CustomerZone[CustomerNames.Length];

        for (int i = 0; i < CustomerNames.Length; i++)
        {
            float x = -TableHalfWidth + zoneWidth * (i + 0.5f);

            GameObject zone = CreatePart(PrimitiveType.Quad, $"Zone_{CustomerNames[i]}", table, new Vector3(x, SurfaceLift * 1.5f, zoneZ),
                Quaternion.Euler(90f, 0f, 0f), new Vector3(zoneWidth, CustomerZoneDepth, 1f), SaveMaterial($"Zone{i}", ZoneColors[i]));

            Color labelColor = ZoneColors[i].grayscale > 0.6f ? DarkColor : Color.white;
            CreateFlatLabel($"{CustomerNames[i]}Label", table, new Vector3(x, 0.03f, TableHalfLength - 0.6f), CustomerNames[i], 6f, labelColor);

            Transform customer = BuildCustomer(i, table, new Vector3(x, -0.6f, TableHalfLength + 1.2f));

            CustomerZone customerZone = zone.AddComponent<CustomerZone>();
            var settings = new SerializedObject(customerZone);
            DecoratingSceneSetup.Assign(settings, "_customer", customer);
            settings.FindProperty("_size").vector2Value = new Vector2(zoneWidth, CustomerZoneDepth);
            settings.ApplyModifiedPropertiesWithoutUndo();
            zones[i] = customerZone;
        }

        return zones;
    }

    /// <summary>A chunky primitive diner: capsule body, round head, hair (or sloth eye patches), eyes, and a smile, facing the players.</summary>
    private static Transform BuildCustomer(int index, Transform parent, Vector3 position)
    {
        bool isSloth = index == SlothIndex;
        Transform root = new GameObject($"Customer_{CustomerNames[index]}").transform;
        root.SetParent(parent, false);
        root.localPosition = position;

        Material eyes = SaveMaterial("CustomerEyes", DarkColor);
        Material mouth = SaveMaterial("CustomerMouth", new Color(0.75f, 0.2f, 0.25f));
        Material head = isSloth ? SaveMaterial("SlothFur", SlothColor) : SaveMaterial("CustomerSkin", SkinColor);

        CreatePart(PrimitiveType.Capsule, "Body", root, new Vector3(0f, 0.75f, 0f), Quaternion.identity, new Vector3(1.2f, 0.75f, 0.9f), SaveMaterial($"Shirt{index}", ShirtColors[index]));
        CreatePart(PrimitiveType.Sphere, "Head", root, new Vector3(0f, 1.85f, 0f), Quaternion.identity, Vector3.one, head);

        if (isSloth)
        {
            Material patch = SaveMaterial("SlothPatch", new Color(0.35f, 0.27f, 0.22f));
            CreatePart(PrimitiveType.Sphere, "EyePatchL", root, new Vector3(-0.22f, 1.9f, -0.4f), Quaternion.Euler(0f, 0f, 20f), new Vector3(0.34f, 0.22f, 0.12f), patch);
            CreatePart(PrimitiveType.Sphere, "EyePatchR", root, new Vector3(0.22f, 1.9f, -0.4f), Quaternion.Euler(0f, 0f, -20f), new Vector3(0.34f, 0.22f, 0.12f), patch);
        }
        else
        {
            CreatePart(PrimitiveType.Sphere, "Hair", root, new Vector3(0f, 2.2f, 0.08f), Quaternion.identity, new Vector3(1.05f, 0.55f, 1.05f), SaveMaterial($"Hair{index}", HairColors[index]));
        }

        CreatePart(PrimitiveType.Sphere, "EyeL", root, new Vector3(-0.2f, 1.95f, -0.46f), Quaternion.identity, Vector3.one * 0.14f, eyes);
        CreatePart(PrimitiveType.Sphere, "EyeR", root, new Vector3(0.2f, 1.95f, -0.46f), Quaternion.identity, Vector3.one * 0.14f, eyes);
        CreatePart(PrimitiveType.Sphere, "Smile", root, new Vector3(0f, 1.66f, -0.47f), Quaternion.identity, new Vector3(0.3f, 0.07f, 0.1f), mouth);

        return root;
    }

    private static Transform CreateSpot(string name, Transform table, Vector3 position, Material padMaterial)
    {
        Transform spot = new GameObject(name).transform;
        spot.SetParent(table, false);
        spot.localPosition = position;

        CreatePart(PrimitiveType.Cylinder, "Pad", spot, new Vector3(0f, SurfaceLift * 1.5f, 0f), Quaternion.identity, new Vector3(1.9f, 0.004f, 1.9f), padMaterial);
        return spot;
    }

    private static void CreateSurface(string name, Transform table, float zStart, float zEnd, Texture2D texture, string materialName)
    {
        float length = zEnd - zStart;
        float width = TableHalfWidth * 2f;
        Vector2 tiling = new Vector2(width / (GinghamCell * 2f), length / (GinghamCell * 2f));

        CreatePart(PrimitiveType.Quad, name, table, new Vector3(0f, SurfaceLift, (zStart + zEnd) * 0.5f), Quaternion.Euler(90f, 0f, 0f),
            new Vector3(width, length, 1f), SaveMaterial(materialName, Color.white, texture, tiling));
    }

    private static void CreateFlatLabel(string name, Transform parent, Vector3 localPosition, string text, float fontSize, Color color)
    {
        var labelObject = new GameObject(name);
        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = localPosition;
        labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.rectTransform.sizeDelta = new Vector2(10f, 2f);
    }

    private static GameObject CreatePart(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Quaternion localRotation,
        Vector3 localScale, Material material, bool keepCollider = false)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;

        if (!keepCollider)
        {
            Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }

    private static HUDController BuildHud()
    {
        var canvasObject = new GameObject("HUDCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Transform canvas = canvasObject.transform;
        TextMeshProUGUI roundText = CreateHudText("RoundText", canvas, new Vector2(0f, 1f), new Vector2(40f, -30f), 48f, TextAlignmentOptions.TopLeft);
        TextMeshProUGUI timerText = CreateHudText("TimerText", canvas, new Vector2(0.5f, 1f), new Vector2(0f, -20f), 72f, TextAlignmentOptions.Top);
        TextMeshProUGUI platesText = CreateHudText("PlatesText", canvas, new Vector2(1f, 1f), new Vector2(-40f, -30f), 48f, TextAlignmentOptions.TopRight);
        TextMeshProUGUI scoreText = CreateHudText("ScoreText", canvas, new Vector2(1f, 1f), new Vector2(-40f, -100f), 44f, TextAlignmentOptions.TopRight);

        var panel = new GameObject("ResultsPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas, false);
        ((RectTransform)panel.transform).sizeDelta = new Vector2(1100f, 620f);
        panel.GetComponent<Image>().color = new Color(0.12f, 0.08f, 0.06f, 0.8f);

        TextMeshProUGUI resultsText = CreateHudText("ResultsText", panel.transform, new Vector2(0.5f, 0.5f), Vector2.zero, 56f, TextAlignmentOptions.Center);
        resultsText.rectTransform.sizeDelta = new Vector2(1040f, 580f);
        panel.SetActive(false);

        HUDController hud = canvasObject.AddComponent<HUDController>();
        var settings = new SerializedObject(hud);
        DecoratingSceneSetup.Assign(settings, "_roundText", roundText);
        DecoratingSceneSetup.Assign(settings, "_timerText", timerText);
        DecoratingSceneSetup.Assign(settings, "_waffleCountText", platesText);
        DecoratingSceneSetup.Assign(settings, "_accuracyText", scoreText);
        DecoratingSceneSetup.Assign(settings, "_resultsPanel", panel);
        DecoratingSceneSetup.Assign(settings, "_resultsText", resultsText);
        settings.ApplyModifiedPropertiesWithoutUndo();
        return hud;
    }

    private static TextMeshProUGUI CreateHudText(string name, Transform parent, Vector2 anchor, Vector2 anchoredPosition, float fontSize, TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = string.Empty;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(900f, 120f);
        return text;
    }

    private static Material SaveMaterial(string name, Color color, Texture2D texture = null, Vector2 tiling = default)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find(LitShaderName));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.25f);
        if (texture != null)
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", tiling);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Writes a 2x2 gingham tile (dark, two mids, light) as a crisp, repeating point-filtered texture.</summary>
    private static Texture2D SaveGingham(string name, Color dark, Color mid, Color light)
    {
        string path = $"{TextureFolder}/{name}.png";

        var tile = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tile.SetPixels(new[] { dark, mid, mid, light });
        tile.Apply();
        File.WriteAllBytes(path, tile.EncodeToPNG());
        Object.DestroyImmediate(tile);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    private static void AssignArray(SerializedObject target, string propertyName, Object[] values)
    {
        SerializedProperty property = target.FindProperty(propertyName);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
