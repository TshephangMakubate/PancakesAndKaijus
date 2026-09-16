using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Retrofits the co-op sequence mechanic onto the real cooking scene: mounts a
/// television on the back wall per active team, puts that team's token row on
/// its screen, adds a <see cref="SequenceMatchRunner"/>, points
/// <see cref="GameManager"/> at it, and hides the leftover Simon-says arrows.
/// <para>
/// Safe to re-run: the sets, stations and runner are replaced rather than
/// duplicated, and nothing else in the scene is touched.
/// </para>
/// </summary>
public static class CookAndDecorateSequenceSetup
{
    private const string ScenePath = "Assets/Scenes/Cook And Decorate Scene.unity";
    private const string RunnerObjectName = "SequenceMatchRunner";
    private const string TeamAStationName = "TeamAStation";
    private const string TeamBStationName = "TeamBStation";
    private const string TeamATvName = "TeamATelevision";
    private const string TeamBTvName = "TeamBTelevision";

    // Framing. The authored camera was pitched 32 degrees down with a 68 degree
    // FOV, which foreshortened the back wall so hard that only about y 1.2-3.8
    // was visible up there, and filled the bottom third with empty table. Easing
    // the pitch back and pulling the camera away opens the wall up for the sets
    // and brings the table edges out to the frame.
    private static readonly Vector3 CameraPosition = new Vector3(0f, 2.6f, -8.5f);
    private static readonly Vector3 CameraEuler = new Vector3(18f, 0f, 0f);
    private const float CameraFieldOfView = 60f;

    // With that framing the wall reads from roughly y 1.8 up to y 6.2, and the
    // chefs' heads top out around y 2.1, so the sets sit above them.
    private const float TvHeight = 4f;
    private const float TvY = 3.8f;
    private const float TvZ = 8.6f;
    private const float WallCentreX = 0.6f;

    // One active team gets a single wide set; two teams get one each, pushed out
    // past the chefs so neither blocks its own row.
    private const float SingleTvWidth = 16f;
    private const float PairTvWidth = 13f;
    private const float PairTvOffsetX = 6.9f;

    // Leftovers from the Simon-says loop; hidden so the HUD reads clearly.
    private static readonly HashSet<string> LegacyArrowNames = new HashSet<string>
    {
        "UpArrow", "DownArrow", "LeftArrow", "RightArrow",
        "TeamBUp", "TeamBDown", "TeamBLeft", "TeamBRight",
        "TeamAArrow", "TeamBArrow"
    };

    [MenuItem("Waffle Party/Upgrade Cook And Decorate Scene To Sequences")]
    public static void UpgradeScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (!File.Exists(ScenePath))
        {
            Debug.LogError($"Waffle Party: '{ScenePath}' does not exist. Run Waffle Party > Create Cook And Decorate Scene first.");
            return;
        }

        SequenceSceneFactory.EnsureAssets();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Resolve the assets only once the scene is open: the reimport a scene
        // change triggers invalidates references taken before it.
        SequenceTokenPalette palette = SequenceSceneFactory.LoadPalette();
        SequenceConfig config = SequenceSceneFactory.LoadConfig();
        SequenceTokenView tokenPrefab = SequenceSceneFactory.LoadTokenPrefab();

        GameManager gameManager = DecoratingSceneSetup.FindInScene<GameManager>(scene);
        Canvas canvas = DecoratingSceneSetup.FindInScene<Canvas>(scene);

        if (gameManager == null || canvas == null)
        {
            Debug.LogError("Waffle Party: Expected a GameManager and a Canvas in the Cook And Decorate Scene.");
            return;
        }

        RemoveExisting(scene, canvas);
        HideLegacyArrows(canvas);
        FrameCamera(scene);
        MoveHudClearOfScreens(scene);

        // Each team's row lives on its own television on the back wall rather
        // than on the HUD overlay.
        bool teamBActive = config != null && config.IsTeamActive(1);
        float width = teamBActive ? PairTvWidth : SingleTvWidth;
        float offsetX = teamBActive ? PairTvOffsetX : 0f;

        RectTransform screenA = SequenceSceneFactory.BuildTelevision(
            TeamATvName, new Vector3(WallCentreX - offsetX, TvY, TvZ), width, TvHeight);
        RectTransform screenB = SequenceSceneFactory.BuildTelevision(
            TeamBTvName, new Vector3(WallCentreX + offsetX, TvY, TvZ), width, TvHeight);

        // A second set with nobody on it would just be clutter.
        screenB.parent.gameObject.SetActive(teamBActive);

        SequenceStationView stationA = SequenceSceneFactory.BuildStation(
            screenA, TeamAStationName, 0f, tokenPrefab, palette);
        SequenceStationView stationB = SequenceSceneFactory.BuildStation(
            screenB, TeamBStationName, 0f, tokenPrefab, palette);

        var runnerObject = new GameObject(RunnerObjectName);
        SequenceMatchRunner runner = runnerObject.AddComponent<SequenceMatchRunner>();

        var runnerSettings = new SerializedObject(runner);
        DecoratingSceneSetup.Assign(runnerSettings, "_config", config);
        DecoratingSceneSetup.Assign(runnerSettings, "_teamAStation", stationA);
        DecoratingSceneSetup.Assign(runnerSettings, "_teamBStation", stationB);

        // The GameManager announces rounds and paces the pan, so it deals.
        runnerSettings.FindProperty("_autoDeal").boolValue = false;
        runnerSettings.FindProperty("_startOnPlay").boolValue = false;
        runnerSettings.ApplyModifiedPropertiesWithoutUndo();

        var managerSettings = new SerializedObject(gameManager);
        DecoratingSceneSetup.Assign(managerSettings, "_sequenceRunner", runner);
        managerSettings.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = runnerObject;

        Debug.Log("Waffle Party: Cook And Decorate Scene now runs the co-op token sequences. " +
                  "Team A plays on F and J; tick Team B Active on the SequenceConfig once you have four players.");
    }

    /// <summary>Clears anything a previous run of this upgrade left behind.</summary>
    private static void RemoveExisting(Scene scene, Canvas canvas)
    {
        var stale = new List<GameObject>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == RunnerObjectName || root.name == TeamATvName || root.name == TeamBTvName)
            {
                stale.Add(root);
            }
        }

        // Stations from an earlier run sat on the HUD canvas rather than a set.
        foreach (SequenceStationView station in canvas.GetComponentsInChildren<SequenceStationView>(true))
        {
            stale.Add(station.gameObject);
        }

        for (int i = 0; i < stale.Count; i++)
        {
            if (stale[i] != null)
            {
                Object.DestroyImmediate(stale[i]);
            }
        }
    }

    /// <summary>
    /// Re-frames the gameplay camera so the sets on the back wall are large
    /// enough to read at a glance and the table reaches the edges of the frame.
    /// <para>
    /// The values live on <see cref="GameplayCameraRig"/>, which pushes them
    /// onto the camera in both edit and play mode, so tuning them by hand
    /// afterwards works exactly as before — re-running this menu item is what
    /// puts them back.
    /// </para>
    /// </summary>
    private static void FrameCamera(Scene scene)
    {
        GameplayCameraRig rig = DecoratingSceneSetup.FindInScene<GameplayCameraRig>(scene);
        if (rig == null)
        {
            Debug.LogWarning("Waffle Party: No GameplayCameraRig found, so the framing was left alone.");
            return;
        }

        var settings = new SerializedObject(rig);
        settings.FindProperty("_position").vector3Value = CameraPosition;
        settings.FindProperty("_eulerRotation").vector3Value = CameraEuler;
        settings.FindProperty("_fieldOfView").floatValue = CameraFieldOfView;
        settings.ApplyModifiedPropertiesWithoutUndo();

        // Push the new values onto the camera straight away so the scene view
        // and a saved scene agree without needing a domain reload.
        rig.Apply();
    }

    /// <summary>
    /// Drops the HUD readouts to the bottom of the screen. They sat across the
    /// top, which is exactly the band the televisions now occupy, and a score
    /// line overlapping the sequence would defeat the point of the sets.
    /// </summary>
    private static void MoveHudClearOfScreens(Scene scene)
    {
        HUDController hud = DecoratingSceneSetup.FindInScene<HUDController>(scene);
        if (hud == null)
        {
            Debug.LogWarning("Waffle Party: No HUDController found, so the readouts were left where they were.");
            return;
        }

        var settings = new SerializedObject(hud);

        // Left, centre and right along the bottom edge, in canvas units.
        Anchor(settings, "_roundText", new Vector2(0f, 0f), new Vector2(210f, 58f));
        Anchor(settings, "_timerText", new Vector2(0.5f, 0f), new Vector2(0f, 58f));
        Anchor(settings, "_waffleCountText", new Vector2(1f, 0f), new Vector2(-210f, 96f));
        Anchor(settings, "_accuracyText", new Vector2(1f, 0f), new Vector2(-210f, 44f));
    }

    /// <summary>Re-anchors one HUD element referenced by a private field.</summary>
    private static void Anchor(SerializedObject hudSettings, string field, Vector2 anchor, Vector2 position)
    {
        SerializedProperty property = hudSettings.FindProperty(field);
        if (property == null || property.objectReferenceValue == null)
        {
            return;
        }

        var component = property.objectReferenceValue as Component;
        if (component == null)
        {
            return;
        }

        var rect = component.transform as RectTransform;
        if (rect == null)
        {
            return;
        }

        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(anchor.x, 0f);
        rect.anchoredPosition = position;
        EditorUtility.SetDirty(rect);
    }

    /// <summary>Deactivates the Simon-says arrow images the token row replaces.</summary>
    private static void HideLegacyArrows(Canvas canvas)
    {
        foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
        {
            if (LegacyArrowNames.Contains(child.name))
            {
                child.gameObject.SetActive(false);
            }
        }
    }
}
