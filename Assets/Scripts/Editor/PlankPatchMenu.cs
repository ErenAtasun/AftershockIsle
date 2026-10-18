using AftershockIsle.Building;
using AftershockIsle.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AftershockIsle.EditorTools
{
    /// <summary>
    /// Hierarchy sağ tık → Aftershock → Kalasla Kapatılacak Döşeme Deliği / Duvar Gediği. Scene görünümünün baktığı yere,
    /// en yakın katın zeminine konur; sonra taşı, Inspector'dan maliyet ve süreyi ayarla, "Kalaslar"ı açıp düzenleyebilirsin.
    /// </summary>
    static class PlankPatchMenu
    {
        const string RootName = "Kalasla Kapatılacaklar";

        [MenuItem("GameObject/Aftershock/Kalasla Kapatılacak Döşeme Deliği", false, 10)]
        static void CreateFloorHole() => Create(PatchKind.FloorHole, new Vector3(1.4f, 0f, 1.4f));

        [MenuItem("GameObject/Aftershock/Kalasla Kapatılacak Duvar Gediği", false, 11)]
        static void CreateWallBreach() => Create(PatchKind.WallBreach, new Vector3(1.4f, 1.2f, 0f));

        static void Create(PatchKind kind, Vector3 size)
        {
            if (Application.isPlaying) return;

            var building = Object.FindFirstObjectByType<BuildingLayout>();
            var view = SceneView.lastActiveSceneView;
            var point = view != null ? view.pivot : Vector3.zero;

            if (building != null)
            {
                int floor = building.FloorAtHeight(point.y + 0.1f);
                float floorY = building.FloorY(floor);
                if (kind == PatchKind.FloorHole)
                {
                    point.y = floorY;
                    point.z = building.ClampZ(point.z);
                }
                else
                {
                    // Gedik arka duvarın iç yüzünde, zeminden biraz yukarıda.
                    point.y = floorY + 1.2f;
                    point.z = building.transform.position.z + building.Depth * 0.5f - 0.05f;
                }
            }

            var scene = EditorSceneManager.GetActiveScene();
            Transform parent = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == RootName) parent = go.transform;
            if (parent == null)
            {
                parent = new GameObject(RootName).transform;
                Undo.RegisterCreatedObjectUndo(parent.gameObject, "Kalasla Kapatılacak Delik");
            }

            string name = kind == PatchKind.FloorHole ? "Döşeme Deliği" : "Duvar Gediği";
            name = GameObjectUtility.GetUniqueNameForSibling(parent, name);
            var patch = PlankPatch.CreateInEditor(name, kind, point, size, parent);
            Selection.activeGameObject = patch.gameObject;
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
