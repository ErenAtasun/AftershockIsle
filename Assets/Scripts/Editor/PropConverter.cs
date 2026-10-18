using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AftershockIsle.EditorTools
{
    /// <summary>
    /// Gri kutu eşyayı (Furniture, RubblePile, WaterSource...) gerçek modele çevirir: model sahneye prefab olarak konur,
    /// kutudaki oyun scriptleri bütün ayarlarıyla (isim, çalışma süresi, kazı sayısı, ganimet...) modelin köküne taşınır,
    /// modelin boyunda bir collider eklenir, sahnedeki başka objelerin kutuya verdiği referanslar modele çevrilir ve kutu
    /// silinir. Sonrası tamamen sahnenin: eşyayı istediğin yere sürükle, ayarlarını Inspector'dan değiştir.
    /// Hierarchy'de kutuya sağ tık → Aftershock → Kutuyu Modelle Değiştir (ya da scriptin ⋮ menüsü).
    /// </summary>
    public class PropConverter : EditorWindow
    {
        const string PropsRootName = "Eşyalar";

        GameObject box;
        GameObject model;
        bool fitToBoxHeight = true;
        float yaw = 180f;

        [MenuItem("GameObject/Aftershock/Kutuyu Modelle Değiştir", false, 0)]
        static void OpenFromHierarchy(MenuCommand command)
        {
            var target = command.context as GameObject ?? Selection.activeGameObject;
            Open(target);
        }

        [MenuItem("CONTEXT/Interactable/Kutuyu Modelle Değiştir")]
        static void OpenFromComponent(MenuCommand command) => Open(((Component)command.context).gameObject);

        [MenuItem("Aftershock/Eşya Dönüştürücü")]
        static void OpenEmpty() => Open(Selection.activeGameObject);

        static void Open(GameObject target)
        {
            var window = GetWindow<PropConverter>(true, "Eşya Dönüştürücü");
            window.minSize = new Vector2(380, 230);
            window.SetBox(target);
            window.Show();
        }

        void SetBox(GameObject target)
        {
            box = target != null && target.GetComponent<Interactable>() != null ? target : null;
            model = box != null ? FindExistingModel(box) : null;
        }

        /// <summary>Kutuya daha önce giydirilmiş model (ModelManager'ın CustomModelSkin'i ya da sahne kurucunun Model'i).</summary>
        static GameObject FindExistingModel(GameObject target)
        {
            foreach (var holderName in new[] { "CustomModelSkin", "Model" })
            {
                var holder = target.transform.Find(holderName);
                if (holder == null) continue;
                foreach (Transform child in holder.GetComponentsInChildren<Transform>(true))
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                    if (source != null) return source.transform.root.gameObject;
                }
            }
            return null;
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Kutudaki oyun scriptleri (isim, çalışma süresi, kazı sayısı, ganimet...) ayarlarıyla modele taşınır, " +
                "modele collider eklenir ve kutu silinir. Sonra eşyayı istediğin yere sürükleyebilirsin. Ctrl+Z ile geri alınır.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            var newBox = (GameObject)EditorGUILayout.ObjectField("Gri kutu (sahnede)", box, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) SetBox(newBox);

            model = (GameObject)EditorGUILayout.ObjectField("Model (FBX / prefab)", model, typeof(GameObject), false);
            fitToBoxHeight = EditorGUILayout.Toggle(new GUIContent("Kutunun boyuna ölçekle",
                "Model kutuyla aynı yükseklikte olsun. Kapalıysa modelin kendi ölçüsü kullanılır."), fitToBoxHeight);
            yaw = EditorGUILayout.FloatField(new GUIContent("Y dönüşü", "Blender modelleri kameraya baksın diye genelde 180"), yaw);

            string problem = Validate();
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);

            using (new EditorGUI.DisabledScope(problem != null || Application.isPlaying))
            {
                GUILayout.Space(6);
                if (GUILayout.Button("Dönüştür", GUILayout.Height(30)))
                {
                    var result = Convert(box, model, fitToBoxHeight, yaw);
                    if (result != null)
                    {
                        Selection.activeGameObject = result;
                        Close();
                    }
                }
            }
        }

        string Validate()
        {
            if (box == null) return "Sahneden oyun scripti (Furniture, RubblePile...) olan bir kutu seç.";
            if (EditorUtility.IsPersistent(box)) return "Kutu sahnedeki obje olmalı, Project'teki dosya değil.";
            if (box.GetComponent<SupportColumn>() != null) return "Kolonların modelini seviyesine göre ModelManager değiştirir; kolon dönüştürülmez.";
            if (box.GetComponent<SceneProp>() != null) return "Bu zaten dönüştürülmüş bir eşya.";
            if (model == null) return "Project'ten bir model (FBX ya da prefab) sürükle.";
            if (!EditorUtility.IsPersistent(model)) return "Model Project'teki bir dosya olmalı (FBX ya da prefab).";
            return null;
        }

        public static GameObject Convert(GameObject box, GameObject modelAsset, bool fitToBoxHeight, float yaw)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Kutuyu Modelle Değiştir");

            // Kutunun dünya ölçüsü: taban ortası ve yüksekliği (görünen modelin yerine konur).
            var boxBounds = BoxBounds(box);
            var existingSkin = box.transform.Find("CustomModelSkin");
            Transform existingModel = existingSkin != null && existingSkin.childCount > 0 ? existingSkin.GetChild(0) : null;

            var parent = FindOrCreatePropsRoot(box.scene);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, box.scene);
            Undo.RegisterCreatedObjectUndo(root, "Kutuyu Modelle Değiştir");
            root.name = box.name;
            root.transform.SetParent(parent, true);

            bool sameModel = existingModel != null &&
                             PrefabUtility.GetCorrespondingObjectFromSource(existingModel.gameObject) == modelAsset;
            if (sameModel)
            {
                // Kutuya zaten bu model giydirilmişti: tam olarak görünen yerinde ve ölçüsünde kalsın.
                root.transform.SetPositionAndRotation(existingModel.position, existingModel.rotation);
                root.transform.localScale = existingModel.lossyScale;
            }
            else
            {
                root.transform.SetPositionAndRotation(box.transform.position, Quaternion.Euler(0f, box.transform.eulerAngles.y + yaw, 0f));
                root.transform.localScale = Vector3.one;
                var bounds = RendererBounds(root);
                if (fitToBoxHeight && bounds.size.y > 0.001f)
                {
                    root.transform.localScale = Vector3.one * (boxBounds.size.y / bounds.size.y);
                    bounds = RendererBounds(root);
                }
                var bottomTarget = new Vector3(boxBounds.center.x, boxBounds.min.y, boxBounds.center.z);
                root.transform.position += bottomTarget - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            }

            // Oyun scriptleri ayarlarıyla taşınır (Transform, mesh, renderer ve collider kutuda kalır).
            var remap = new Dictionary<Object, Object> { { box, root }, { box.transform, root.transform } };
            foreach (var component in box.GetComponents<MonoBehaviour>())
            {
                if (component == null) continue;
                if (!UnityEditorInternal.ComponentUtility.CopyComponent(component)) continue;
                if (!UnityEditorInternal.ComponentUtility.PasteComponentAsNew(root)) continue;
                var components = root.GetComponents(component.GetType());
                remap[component] = components[components.Length - 1];
            }
            Undo.AddComponent<SceneProp>(root);

            // Kutunun kendi alt objeleri (ışık, efekt, işaret noktası) modelin altına geçer; eski model kabukları gider.
            var children = new List<Transform>();
            foreach (Transform child in box.transform) children.Add(child);
            foreach (var child in children)
            {
                if (child.name == "CustomModelSkin" || child.name == "Model" || child.name.StartsWith("CustomModelSkin")) continue;
                Undo.SetTransformParent(child, root.transform, "Kutuyu Modelle Değiştir");
            }

            var interactable = root.GetComponent<Interactable>();
            if (interactable != null) interactable.FitColliderToModel();

            RedirectReferences(box, remap);
            Undo.DestroyObjectImmediate(box);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[Eşya Dönüştürücü] '{root.name}' modele çevrildi: scriptler ve ayarları taşındı, collider eklendi.", root);
            return root;
        }

        /// <summary>Sahnedeki tüm scriptlerde kutuya (ya da kutunun scriptlerine) verilen referansları yeni objeye çevirir.</summary>
        static void RedirectReferences(GameObject box, Dictionary<Object, Object> remap)
        {
            foreach (var component in Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (component == null || component.gameObject == box || component is Transform) continue;
                var so = new SerializedObject(component);
                var property = so.GetIterator();
                bool changed = false;
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = property.objectReferenceValue;
                    if (value == null || !remap.TryGetValue(value, out var replacement) || replacement == value) continue;
                    property.objectReferenceValue = replacement;
                    changed = true;
                }
                if (changed) so.ApplyModifiedProperties();
            }
        }

        static Transform FindOrCreatePropsRoot(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == PropsRootName) return go.transform;
            var root = new GameObject(PropsRootName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Kutuyu Modelle Değiştir");
            return root.transform;
        }

        static Bounds BoxBounds(GameObject box)
        {
            if (box.TryGetComponent<Collider>(out var collider) && collider.enabled) return collider.bounds;
            if (box.TryGetComponent<Renderer>(out var renderer)) return renderer.bounds;
            return new Bounds(box.transform.position, box.transform.lossyScale);
        }

        static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
