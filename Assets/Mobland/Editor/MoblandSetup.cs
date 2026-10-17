using System.Collections.Generic;
using System.IO;
using Mobland.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mobland.EditorTools
{
    /// <summary>
    /// First-open convenience: creates Assets/Mobland/Scenes/Main.unity with a GameRoot,
    /// opens it and adds it to Build Settings. Also available from the "Mobland" menu.
    /// </summary>
    [InitializeOnLoad]
    static class MoblandSetup
    {
        const string ScenePath = "Assets/Mobland/Scenes/Main.unity";
        const string SessionKey = "Mobland.SetupChecked";

        static MoblandSetup()
        {
            EditorApplication.delayCall += AutoSetup;
        }

        static void AutoSetup()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (File.Exists(ScenePath))
            {
                EnsureInBuildSettings();
                return;
            }
            CreateMainScene();
        }

        [MenuItem("Mobland/Create Main Scene")]
        static void CreateMainSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateMainScene();
        }

        [MenuItem("Mobland/Open Main Scene")]
        static void OpenMainScene()
        {
            if (!File.Exists(ScenePath)) { CreateMainSceneMenu(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        static void CreateMainScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Mobland").AddComponent<GameRoot>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureInBuildSettings();
            Debug.Log($"[Mobland] Created {ScenePath}. Press Play to generate the city.");
        }

        static void EnsureInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
                if (s.path == ScenePath) return;
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
