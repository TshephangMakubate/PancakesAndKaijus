using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Converts the copied cooking layout in the Decorating Scene into the decorating station, and wires Build Settings.</summary>
public static class DecoratingSceneSetup
{
    internal const string CookingScenePath = "Assets/Scenes/Cooking Scene.unity";
    internal const string DecoratingScenePath = "Assets/Scenes/Decorating Scene.unity";
    private const string PlateMaterialPath = "Assets/Materials/Plate.mat";
    private const string PlateRimMaterialPath = "Assets/Materials/PlateRim.mat";

    // World positions in the space freed up by removing the stove and pan.
    private static readonly Vector3 StationPosition = new Vector3(-3.4f, -0.91f, -1f);
    private static readonly Vector3 StructurePosition = new Vector3(-1.15f, -1.18f, -0.2f);

    private static readonly HashSet<string> CookingPropNames = new HashSet<string> { "Pan", "StoveTop", "PanSpawnPoint", "BatterSplash" };

    [MenuItem("Waffle Party/Add Scenes To Build Settings")]
    public static void AddScenesToBuildSettings()
    {
        EnsureInBuildSettings(CookingScenePath, DecoratingScenePath);
    }

    [MenuItem("Waffle Party/Set Up Decorating Scene")]
    public static void SetUpDecoratingScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        AddScenesToBuildSettings();
        Scene scene = EditorSceneManager.OpenScene(DecoratingScenePath, OpenSceneMode.Single);

        if (FindInScene<DecoratingManager>(scene) != null)
        {
            Debug.Log("Waffle Party: Decorating Scene is already set up.");
            return;
        }

        GameManager gameManager = FindInScene<GameManager>(scene);
        WaffleStack stack = FindInScene<WaffleStack>(scene);
        if (gameManager == null || stack == null)
        {
            Debug.LogError("Waffle Party: Expected the copied cooking layout (a GameManager and a WaffleStack) in the Decorating Scene.");
            return;
        }

        var cooking = new SerializedObject(gameManager);
        Object config = cooking.FindProperty("_config").objectReferenceValue;
        Object inputActions = cooking.FindProperty("_inputActions").objectReferenceValue;
        Object sequenceDisplay = cooking.FindProperty("_sequenceDisplay").objectReferenceValue;
        Object hud = cooking.FindProperty("_hud").objectReferenceValue;
        Object cameraShake = cooking.FindProperty("_cameraShake").objectReferenceValue;

        int removed = RemoveCookingProps(scene);

        GameObject managerObject = gameManager.gameObject;
        Object.DestroyImmediate(gameManager);
        managerObject.name = "DecoratingManager";
        DecoratingManager manager = managerObject.AddComponent<DecoratingManager>();
        PancakeDecorator decorator = managerObject.AddComponent<PancakeDecorator>();

        Transform team = stack.transform.parent;
        Transform station = CreateAnchor("DecorateStation", team, StationPosition);
        Transform structure = CreateAnchor("PlateStructure", team, StructurePosition);

        var decorating = new SerializedObject(manager);
        Assign(decorating, "_config", config);
        Assign(decorating, "_inputActions", inputActions);
        Assign(decorating, "_sequenceDisplay", sequenceDisplay);
        Assign(decorating, "_hud", hud);
        Assign(decorating, "_cameraShake", cameraShake);
        Assign(decorating, "_decorator", decorator);
        Assign(decorating, "_sourceStack", stack);
        Assign(decorating, "_stationAnchor", station);
        Assign(decorating, "_structureAnchor", structure);
        Assign(decorating, "_plateMaterial", AssetDatabase.LoadAssetAtPath<Material>(PlateMaterialPath));
        Assign(decorating, "_plateRimMaterial", AssetDatabase.LoadAssetAtPath<Material>(PlateRimMaterialPath));
        decorating.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = managerObject;

        string teamName = team != null ? team.name : "the scene root";
        Debug.Log($"Waffle Party: Decorating Scene set up ({removed} cooking props removed). Move DecorateStation and PlateStructure under {teamName} to tune the layout.");
    }

    /// <summary>Adds any missing scenes to the end of Build Settings, dropping entries whose files no longer exist.</summary>
    internal static void EnsureInBuildSettings(params string[] paths)
    {
        var scenes = new List<EditorBuildSettingsScene>();
        var present = new HashSet<string>();

        foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
        {
            if (File.Exists(existing.path) && present.Add(existing.path))
            {
                scenes.Add(existing);
            }
        }

        foreach (string path in paths)
        {
            if (present.Add(path))
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>Moves the given scenes to the front of Build Settings (the first one is the scene the game starts in).</summary>
    internal static void PutScenesFirst(params string[] paths)
    {
        var scenes = new List<EditorBuildSettingsScene>();
        var placed = new HashSet<string>(paths);

        foreach (string path in paths)
        {
            scenes.Add(new EditorBuildSettingsScene(path, true));
        }

        foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
        {
            if (File.Exists(existing.path) && placed.Add(existing.path))
            {
                scenes.Add(existing);
            }
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>Deletes a scene object, or its whole prefab instance when it belongs to one that holds nothing we still need.</summary>
    internal static bool RemoveProp(GameObject prop)
    {
        GameObject target = prop;
        if (PrefabUtility.IsPartOfPrefabInstance(prop))
        {
            target = PrefabUtility.GetOutermostPrefabInstanceRoot(prop);

            // Never delete a prefab that also holds gameplay pieces we still need.
            if (target.GetComponentInChildren<WaffleStack>(true) != null || target.GetComponentInChildren<HUDController>(true) != null)
            {
                Debug.LogWarning($"Waffle Party: '{prop.name}' is inside prefab '{target.name}', so it was deactivated instead of deleted.");
                prop.SetActive(false);
                return false;
            }
        }

        Object.DestroyImmediate(target);
        return true;
    }

    internal static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    internal static void Assign(SerializedObject target, string propertyName, Object value)
    {
        SerializedProperty property = target.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogError($"Waffle Party: {target.targetObject.GetType().Name} has no field '{propertyName}'.");
            return;
        }

        property.objectReferenceValue = value;
    }

    private static int RemoveCookingProps(Scene scene)
    {
        var targets = new List<GameObject>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (PanController pan in root.GetComponentsInChildren<PanController>(true))
            {
                targets.Add(pan.gameObject);
            }

            foreach (WaffleController waffle in root.GetComponentsInChildren<WaffleController>(true))
            {
                targets.Add(waffle.gameObject);
            }

            // The opponent's looping toss has no pan left to toss from.
            foreach (PanLaunchAnimation toss in root.GetComponentsInChildren<PanLaunchAnimation>(true))
            {
                if (new SerializedObject(toss).FindProperty("_pancake").objectReferenceValue is Transform tossedPancake)
                {
                    targets.Add(tossedPancake.gameObject);
                }

                Object.DestroyImmediate(toss);
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (CookingPropNames.Contains(child.name))
                {
                    targets.Add(child.gameObject);
                }
            }
        }

        int removed = 0;
        foreach (GameObject target in targets)
        {
            if (target != null && RemoveProp(target))
            {
                removed++;
            }
        }

        return removed;
    }

    private static Transform CreateAnchor(string name, Transform parent, Vector3 worldPosition)
    {
        Transform anchor = parent != null ? parent.Find(name) : null;
        if (anchor == null)
        {
            anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
        }

        anchor.position = worldPosition;
        anchor.rotation = Quaternion.identity;
        anchor.localScale = Vector3.one;
        return anchor;
    }
}
