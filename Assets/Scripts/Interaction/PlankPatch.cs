using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.Interaction
{
    public enum PatchKind { FloorHole, WallBreach }

    /// <summary>
    /// Kalasla kapatılan delik. Döşeme deliği açıkken üstünden yürünmez (WalkBlocker, karakterler etrafından dolaşır);
    /// duvar gediği açıkken gece baskınlarında savunmayı düşürür ve ayazda o katta çalışanı daha çok yıpratır.
    /// Karakter depodan kalas harcayıp kapatır: kalaslar görünür, engel kalkar, bina bütünlüğü biraz artar.
    /// Sahneye elle konur (GameObject → Aftershock → Kalasla Kapatılacak ...); ayarları Inspector'dan.
    /// </summary>
    public class PlankPatch : Interactable
    {
        [SerializeField] PatchKind kind = PatchKind.FloorHole;
        [Tooltip("Kapatmak için depodan harcanan kalas")]
        [SerializeField] ResourceAmount cost = new ResourceAmount(ResourceType.Scrap, 2);
        [Tooltip("Kapatınca bina bütünlüğüne eklenen puan")]
        [SerializeField] float integrityBonus = 2f;
        [Tooltip("Duvar gediği açıkken gece savunmasından düşülen puan")]
        [SerializeField] float defensePenalty = 10f;
        [Tooltip("Kapatılınca görünen kalaslar (bu objenin altında; editörde düzenlemek için geçici açabilirsin)")]
        [SerializeField] GameObject planks;
        [Tooltip("Döşeme deliği açıkken üstünden yürünmesin diye engel (kapatılınca kapanır)")]
        [SerializeField] WalkBlocker holeBlocker;
        [Tooltip("Oyun bu delik kapalı olarak başlasın")]
        [SerializeField] bool patched;

        static readonly List<PlankPatch> all = new List<PlankPatch>();

        public static IReadOnlyList<PlankPatch> All => all;
        public PatchKind Kind => kind;
        public bool IsPatched => patched;

        public override string ActionLabel => kind == PatchKind.FloorHole ? "Deliği Kalasla Kapat" : "Gediği Kalasla Kapat";
        public override WorkCategory Category => WorkCategory.Repair;
        // Engeli (açık delik) WalkBlocker verir; tıklama kutusu yürüyüşü kesmesin.
        public override bool BlocksMovement => false;
        public override bool IsInteractive => !patched;

        public override string StatusText
        {
            get
            {
                if (patched) return "Kalasla kapatıldı.";
                string effect = kind == PatchKind.FloorHole
                    ? "Üstünden yürünemez, etrafından dolaşılır."
                    : $"Gece savunması -{defensePenalty:0}; ayazda bu katta çalışmak daha yorucu.";
                return $"{effect}  — kapat: {cost}";
            }
        }

        /// <summary>Açık duvar gediklerinin gece savunmasından düşürdüğü toplam puan.</summary>
        public static float OpenBreachDefensePenalty
        {
            get
            {
                float total = 0f;
                foreach (var patch in all)
                    if (patch.kind == PatchKind.WallBreach && !patch.patched) total += patch.defensePenalty;
                return total;
            }
        }

        public static int OpenWallBreachCount
        {
            get
            {
                int count = 0;
                foreach (var patch in all)
                    if (patch.kind == PatchKind.WallBreach && !patch.patched) count++;
                return count;
            }
        }

        /// <summary>Bu katta açık duvar gediği var mı (soğuk içeri giriyor).</summary>
        public static bool HasOpenBreach(int floor)
        {
            foreach (var patch in all)
                if (patch.kind == PatchKind.WallBreach && !patch.patched && patch.Floor == floor) return true;
            return false;
        }

        protected override void Awake()
        {
            base.Awake();
            ApplyVisual();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (patched) reason = "Zaten kalasla kapatıldı.";
            else reason = ResourceStorage.Instance.Has(cost) ? null : $"Depoda yeterli kalas yok: {cost} gerekli.";
            return reason == null;
        }

        public override bool CanContinue(Survivor worker) => !patched;

        public override void Complete(Survivor worker)
        {
            // Çalışma sırasında kalas başka işe harcanmış olabilir.
            if (patched || !ResourceStorage.Instance.TrySpend(cost)) return;
            SetPatched(true);
            if (integrityBonus > 0f) StructuralIntegrity.Instance?.Repair(integrityBonus);
        }

        public void RestoreState(bool isPatched) => SetPatched(isPatched);

        void SetPatched(bool value)
        {
            patched = value;
            ApplyVisual();
        }

        void ApplyVisual()
        {
            if (planks != null) planks.SetActive(patched);
            if (holeBlocker != null) holeBlocker.enabled = kind == PatchKind.FloorHole && !patched;
            FloorObstacles.Invalidate();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editörde kalasla kapatılacak bir delik kurar: tıklama kutusu, kalas modeli (kapalı) ve döşeme deliğiyse yürüme
        /// engeli. center: deliğin dünya merkezi (döşemede zemin seviyesi, duvarda gediğin ortası); size: delik ölçüsü.
        /// </summary>
        public static PlankPatch CreateInEditor(string name, PatchKind kind, Vector3 center, Vector3 size, Transform parent,
            float yaw = 0f)
        {
            var go = new GameObject(name);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Kalasla Kapatılacak Delik");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = center;
            // Yan duvardaki gedik: kalaslar odanın içine (yerel -Z) baksın diye döndürülür.
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            bool floor = kind == PatchKind.FloorHole;
            // Döşeme deliği: zeminde ince tıklama kutusu. Duvar gediği: gediğin önünde ince dikey kutu.
            var box = go.AddComponent<BoxCollider>();
            box.center = floor ? new Vector3(0f, 0.05f, 0f) : Vector3.zero;
            box.size = floor ? new Vector3(size.x, 0.1f, size.z) : new Vector3(size.x, size.y, 0.15f);

            var patch = go.AddComponent<PlankPatch>();
            patch.kind = kind;
            patch.ConfigureRuntime(floor ? "Döşeme Deliği" : "Duvar Gediği", 5f);
            patch.planks = CreatePlanksVisual(go.transform, kind, size);

            if (floor)
            {
                var blockerGo = new GameObject("Delik Engeli");
                blockerGo.transform.SetParent(go.transform, false);
                var blocker = blockerGo.AddComponent<WalkBlocker>();
                // WalkBlocker katı alt yüzünden bulur: zeminden başlayan 1 m'lik kutu.
                blocker.Configure(new Vector3(0f, 0.5f, 0f), new Vector3(size.x, 1f, size.z), false);
                patch.holeBlocker = blocker;
            }

            patch.ApplyVisual();
            UnityEditor.EditorUtility.SetDirty(patch);
            return patch;
        }

        const string PlanksModelPath = "Assets/Models/tahtalar/tahtalar.fbx";

        static GameObject CreatePlanksVisual(Transform parent, PatchKind kind, Vector3 size)
        {
            var holder = new GameObject("Kalaslar").transform;
            holder.SetParent(parent, false);

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlanksModelPath);
            if (prefab == null)
            {
                // Model yoksa düz tahta kutular.
                for (int i = 0; i < 3; i++)
                {
                    var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(plank.GetComponent<Collider>());
                    plank.name = $"Kalas_{i}";
                    plank.transform.SetParent(holder, false);
                    float t = (i - 1) / 3f;
                    plank.transform.localPosition = kind == PatchKind.FloorHole
                        ? new Vector3(0f, 0.04f, t * size.z)
                        : new Vector3(0f, t * size.y, -0.08f);
                    plank.transform.localScale = kind == PatchKind.FloorHole
                        ? new Vector3(size.x * 1.15f, 0.05f, size.z / 3.3f)
                        : new Vector3(size.x * 1.15f, size.y / 3.3f, 0.05f);
                }
                holder.gameObject.SetActive(false);
                return holder.gameObject;
            }

            var model = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, holder);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            // Kalas yığınının en ince ekseni kalınlıktır: döşemede yukarı, duvarda kameraya (-Z) baksın.
            var bounds = LocalBounds(model, holder);
            var s = bounds.size;
            Quaternion lay = Quaternion.identity;
            if (s.x <= s.y && s.x <= s.z) lay = Quaternion.Euler(0f, 0f, 90f);
            else if (s.z <= s.x && s.z <= s.y) lay = Quaternion.Euler(90f, 0f, 0f);
            if (kind == PatchKind.WallBreach) lay = Quaternion.Euler(-90f, 0f, 0f) * lay;
            model.transform.localRotation = lay;

            // Deliği her yönde biraz taşacak şekilde ölçekle; kalınlık ortalama ölçekle.
            bounds = LocalBounds(model, holder);
            s = bounds.size;
            Vector3 target = kind == PatchKind.FloorHole
                ? new Vector3(size.x * 1.15f, 0f, size.z * 1.15f)
                : new Vector3(size.x * 1.15f, size.y * 1.15f, 0f);
            float sx = target.x / Mathf.Max(0.001f, s.x);
            float sy = kind == PatchKind.FloorHole ? 0f : target.y / Mathf.Max(0.001f, s.y);
            float sz = kind == PatchKind.FloorHole ? target.z / Mathf.Max(0.001f, s.z) : 0f;
            float thickness = kind == PatchKind.FloorHole ? (sx + sz) * 0.5f : (sx + sy) * 0.5f;
            var scaleInHolder = kind == PatchKind.FloorHole ? new Vector3(sx, thickness, sz) : new Vector3(sx, sy, thickness);
            holder.localScale = scaleInHolder;

            // Döşemede zemine otursun (2 cm yukarı), duvarda gediğin önünde dursun.
            bounds = LocalBounds(model, parent);
            var offset = kind == PatchKind.FloorHole
                ? new Vector3(-bounds.center.x, 0.02f - bounds.min.y, -bounds.center.z)
                : new Vector3(-bounds.center.x, -bounds.center.y, -0.06f - bounds.max.z);
            holder.localPosition += offset;

            holder.gameObject.SetActive(false);
            return holder.gameObject;
        }

        static Bounds LocalBounds(GameObject model, Transform space)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            var result = new Bounds(space.InverseTransformPoint(model.transform.position), Vector3.zero);
            bool first = true;
            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var local = space.InverseTransformPoint(corner);
                    if (first) { result = new Bounds(local, Vector3.zero); first = false; }
                    else result.Encapsulate(local);
                }
            }
            return result;
        }
#endif
    }
}
