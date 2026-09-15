using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Builds (or brings up to date) the single cook-and-decorate scene from a copy of the Cooking Scene, and makes it the starting scene.</summary>
public static class CombinedSceneSetup
{
    private const string CombinedScenePath = "Assets/Scenes/Cook And Decorate Scene.unity";

    [MenuItem("Waffle Party/Create Cook And Decorate Scene")]
    public static void CreateCombinedScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        bool isNew = !File.Exists(CombinedScenePath);
        if (isNew && !AssetDatabase.CopyAsset(DecoratingSceneSetup.CookingScenePath, CombinedScenePath))
        {
            Debug.LogError($"Waffle Party: Could not copy '{DecoratingSceneSetup.CookingScenePath}'.");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(CombinedScenePath, OpenSceneMode.Single);
        GameManager gameManager = DecoratingSceneSetup.FindInScene<GameManager>(scene);
        WaffleStack stack = DecoratingSceneSetup.FindInScene<WaffleStack>(scene);
        if (gameManager == null || stack == null)
        {
            Debug.LogError("Waffle Party: Expected a GameManager and a WaffleStack in the Cook And Decorate Scene.");
            return;
        }

        // Each sequence spawns its own pancake in the pan, so the static pan waffle goes.
        var managerSettings = new SerializedObject(gameManager);
        if (managerSettings.FindProperty("_waffle").objectReferenceValue is WaffleController panWaffle)
        {
            DecoratingSceneSetup.RemoveProp(panWaffle.gameObject);
        }

        PancakeDecorator decorator = gameManager.GetComponent<PancakeDecorator>();
        if (decorator == null)
        {
            decorator = gameManager.gameObject.AddComponent<PancakeDecorator>();
        }

        managerSettings.Update();
        DecoratingSceneSetup.Assign(managerSettings, "_decorator", decorator);
        managerSettings.ApplyModifiedPropertiesWithoutUndo();

        AddStackCameraZoom(scene, stack);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        DecoratingSceneSetup.PutScenesFirst(CombinedScenePath);
        Selection.activeGameObject = gameManager.gameObject;

        Debug.Log(isNew
            ? "Waffle Party: Created 'Cook And Decorate Scene' and made it the first scene in Build Settings."
            : "Waffle Party: Brought 'Cook And Decorate Scene' up to date and kept it first in Build Settings.");
    }

    private static void AddStackCameraZoom(Scene scene, WaffleStack stack)
    {
        Camera camera = FindMainCamera(scene);
        if (camera == null)
        {
            Debug.LogWarning("Waffle Party: No camera found, so the stack zoom was not added.");
            return;
        }

        StackCameraZoom zoom = camera.GetComponent<StackCameraZoom>();
        if (zoom == null)
        {
            zoom = camera.gameObject.AddComponent<StackCameraZoom>();
        }

        var zoomSettings = new SerializedObject(zoom);
        DecoratingSceneSetup.Assign(zoomSettings, "_stack", stack);
        zoomSettings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Camera FindMainCamera(Scene scene)
    {
        Camera fallback = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (camera.CompareTag("MainCamera"))
                {
                    return camera;
                }

                if (fallback == null)
                {
                    fallback = camera;
                }
            }
        }

        return fallback;
    }
}
