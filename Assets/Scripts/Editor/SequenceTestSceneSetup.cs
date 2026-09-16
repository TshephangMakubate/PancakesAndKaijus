using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds a standalone scene for exercising the co-op sequence mechanic: two
/// team stations, placeholder token art, and debug keys that force a swap or a
/// shuffle on demand. Built from scratch in code, following the same approach
/// as <see cref="ServingSceneSetup"/>, so it can always be regenerated.
/// </summary>
public static class SequenceTestSceneSetup
{
    private const string ScenePath = "Assets/Scenes/Sequence Test Scene.unity";

    [MenuItem("Waffle Party/Create Sequence Test Scene")]
    public static void CreateSequenceTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog(
                "Rebuild Sequence Test Scene?",
                "'Sequence Test Scene' already exists. Rebuild it from scratch?",
                "Rebuild",
                "Cancel"))
        {
            return;
        }

        SequenceSceneFactory.EnsureAssets();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Resolve the assets only once the new scene exists: the reimport a
        // scene change triggers invalidates references taken before it.
        SequenceTokenPalette palette = SequenceSceneFactory.LoadPalette();
        SequenceConfig config = SequenceSceneFactory.LoadConfig();
        SequenceTokenView tokenPrefab = SequenceSceneFactory.LoadTokenPrefab();

        Canvas canvas = BuildCanvas();
        CreateTitle(canvas.transform);

        SequenceStationView stationA = SequenceSceneFactory.BuildStation(
            canvas.transform, "TeamAStation", 150f, tokenPrefab, palette);
        SequenceStationView stationB = SequenceSceneFactory.BuildStation(
            canvas.transform, "TeamBStation", -190f, tokenPrefab, palette);

        var runnerObject = new GameObject("SequenceMatchRunner");
        SequenceMatchRunner runner = runnerObject.AddComponent<SequenceMatchRunner>();
        SequenceDebugKeys debugKeys = runnerObject.AddComponent<SequenceDebugKeys>();

        var runnerSettings = new SerializedObject(runner);
        DecoratingSceneSetup.Assign(runnerSettings, "_config", config);
        DecoratingSceneSetup.Assign(runnerSettings, "_teamAStation", stationA);
        DecoratingSceneSetup.Assign(runnerSettings, "_teamBStation", stationB);
        runnerSettings.FindProperty("_autoDeal").boolValue = true;
        runnerSettings.FindProperty("_startOnPlay").boolValue = true;
        runnerSettings.ApplyModifiedPropertiesWithoutUndo();

        var debugSettings = new SerializedObject(debugKeys);
        DecoratingSceneSetup.Assign(debugSettings, "_runner", runner);
        debugSettings.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        DecoratingSceneSetup.EnsureInBuildSettings(ScenePath);
        Selection.activeGameObject = runnerObject;

        Debug.Log("Waffle Party: Built 'Sequence Test Scene'. Team B stays idle until you tick Team B Active on the SequenceConfig.");
    }

    private static Canvas BuildCanvas()
    {
        var canvasObject = new GameObject("SequenceCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private static void CreateTitle(Transform parent)
    {
        TextMeshProUGUI title = SequenceSceneFactory.CreateText(parent, "Title", 40f, TextAlignmentOptions.Center);
        title.text = "SEQUENCE TEST";

        RectTransform rect = title.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -40f);
        rect.sizeDelta = new Vector2(800f, 60f);
    }
}
