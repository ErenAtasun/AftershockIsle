using AftershockIsle.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AftershockIsle.EditorTools
{
    /// <summary>
    /// Yangın tüplerine FireExtinguisher scriptini ekler: Yangin_Tupu prefabına (bütün kopyaları alır) ve sahneye doğrudan
    /// konan yangıntup.fbx modellerine. Script ya da collider zaten varsa dokunmaz. Editör açılınca, sahne açılınca ve
    /// menüden (Aftershock → Yangın Tüplerine Script Ekle) çalışır.
    /// </summary>
    [InitializeOnLoad]
    static class FireExtinguisherSetup
    {
        const string PrefabPath = "Assets/Prefabs/Props/Yangin_Tupu.prefab";
        // Assets/Models/sandalye/modeller/yangıntüp/yangıntüp.fbx
        const string ModelGuid = "39eeba4fa2556574f93ecf465adfd501";

        static FireExtinguisherSetup()
        {
            EditorApplication.delayCall += Run;
            EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += Run;
        }

        [MenuItem("Aftershock/Yangın Tüplerine Script Ekle")]
        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            FixPrefab();
            FixScene();
        }

        static void FixPrefab()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (asset == null || asset.GetComponent<FireExtinguisher>() != null) return;

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var extinguisher = root.AddComponent<FireExtinguisher>();
                extinguisher.ConfigureRuntime("Yangın Tüpü", 2f);
                if (root.GetComponentInChildren<Collider>(true) == null) extinguisher.FitColliderToModel();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[Yangın Tüpü] Yangin_Tupu prefabına FireExtinguisher eklendi.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Prefab olmadan doğrudan sahneye sürüklenmiş yangın tüpü modelleri (yangıntup.fbx).
        static void FixScene()
        {
            bool changed = false;
            foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(go.gameObject);
                if (root != go.gameObject) continue;
                var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (source == null) continue;
                // Dosya adında birleşik Türkçe harf var (yangıntüp); adla değil GUID'le tanınır.
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                if (guid != ModelGuid) continue;
                if (root.GetComponentInParent<FireExtinguisher>(true) != null) continue;

                var extinguisher = Undo.AddComponent<FireExtinguisher>(root);
                extinguisher.ConfigureRuntime("Yangın Tüpü", 2f);
                if (root.GetComponentInChildren<Collider>(true) == null) extinguisher.FitColliderToModel();
                EditorUtility.SetDirty(root);
                changed = true;
                Debug.Log($"[Yangın Tüpü] Sahnedeki '{root.name}' modeline FireExtinguisher eklendi.", root);
            }

            if (changed)
                foreach (var scene in new[] { UnityEngine.SceneManagement.SceneManager.GetActiveScene() })
                    EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
