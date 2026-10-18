using System.Collections.Generic;
using AftershockIsle.CameraControl;
using AftershockIsle.Interaction;
using AftershockIsle.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Etkileşilebilir eşyaların etrafına beyazımsı kontur çizer; eşyaların üstündeki yazı işaretlerinin yerini alır.
    /// Üstüne gelinen eşya parlak, karakterin yanındaki (F) eşya belirgin, acil işler yavaşça yanıp söner, önemli işler soluk.
    /// Kontur, eşyanın görünen mesh'lerinin kopyası olan ters kabuktur (Resources/Shaders/InteractOutline).
    /// </summary>
    public class InteractableHighlighter : MonoBehaviour
    {
        public const string ShellName = "Etkileşim Vurgusu";

        const float ListSeconds = 0.5f;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int WidthId = Shader.PropertyToID("_Width");

        [SerializeField] Color outlineColor = new Color(0.95f, 0.94f, 0.88f, 1f);
        [SerializeField, Range(0.5f, 8f)] float hoverWidth = 3f;
        [SerializeField, Range(0.5f, 8f)] float nearbyWidth = 2.5f;
        [SerializeField, Range(0.5f, 8f)] float alertWidth = 2.2f;

        class Shell
        {
            public readonly List<(Renderer source, MeshRenderer shell)> Parts = new List<(Renderer, MeshRenderer)>();
            public Material Material;
        }

        readonly Dictionary<Interactable, Shell> shells = new Dictionary<Interactable, Shell>();
        readonly Dictionary<Interactable, (float alpha, float width)> wanted = new Dictionary<Interactable, (float, float)>();
        readonly List<Interactable> things = new List<Interactable>();
        readonly List<Interactable> stale = new List<Interactable>();
        CommandController commands;
        Shader shader;
        Material hiddenMaterial;
        float listAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<InteractableHighlighter>() == null)
                new GameObject("[Etkileşim Vurgusu]").AddComponent<InteractableHighlighter>();
        }

        void Awake()
        {
            shader = Resources.Load<Shader>("Shaders/InteractOutline");
            if (shader == null) { enabled = false; return; }
            hiddenMaterial = new Material(shader) { name = "Vurgu_Gizli" };
            hiddenMaterial.SetColor(ColorId, new Color(0f, 0f, 0f, 0f));
            hiddenMaterial.SetFloat(WidthId, 0f);
        }

        void LateUpdate()
        {
            if (commands == null) commands = FindFirstObjectByType<CommandController>();

            if (Time.unscaledTime >= listAt)
            {
                listAt = Time.unscaledTime + ListSeconds;
                things.Clear();
                things.AddRange(FindObjectsByType<Interactable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            }

            wanted.Clear();
            bool hidden = SideViewCamera.ViewingField || ModalMenu.IsOpen || PauseMenu.IsOpen;
            if (!hidden)
            {
                float pulse = 0.55f + 0.3f * Mathf.Sin(Time.unscaledTime * 3.2f);
                foreach (var thing in things)
                {
                    if (thing == null || !thing.isActiveAndEnabled || !thing.IsInteractive) continue;
                    var kind = InteractableGuide.Of(thing).Kind;
                    if (kind == MarkerKind.Urgent) wanted[thing] = (pulse, alertWidth);
                    else if (kind == MarkerKind.Important) wanted[thing] = (0.32f, alertWidth);
                }

                var nearby = commands != null ? commands.Nearby : null;
                if (nearby != null && nearby.IsInteractive) wanted[nearby] = (0.8f, nearbyWidth);
                var hovered = commands != null ? commands.Hovered : null;
                if (hovered != null && hovered.IsInteractive) wanted[hovered] = (0.95f, hoverWidth);
            }

            foreach (var pair in wanted)
            {
                if (!shells.TryGetValue(pair.Key, out var shell) || !IsValid(shell)) shell = Build(pair.Key);
                if (shell == null) continue;
                var c = outlineColor;
                c.a = outlineColor.a * pair.Value.alpha;
                shell.Material.SetColor(ColorId, c);
                shell.Material.SetFloat(WidthId, pair.Value.width);
                foreach (var (source, part) in shell.Parts)
                    if (part != null) part.enabled = source != null && source.enabled && source.gameObject.activeInHierarchy;
            }

            stale.Clear();
            foreach (var pair in shells)
            {
                if (pair.Key == null) { stale.Add(pair.Key); continue; }
                if (wanted.ContainsKey(pair.Key)) continue;
                foreach (var (_, part) in pair.Value.Parts)
                    if (part != null && part.enabled) part.enabled = false;
            }
            foreach (var key in stale) Release(key);
        }

        static bool IsValid(Shell shell)
        {
            if (shell.Parts.Count == 0) return false;
            foreach (var (source, part) in shell.Parts)
                if (source == null || part == null) return false;
            return true;
        }

        /// <summary>Eşyanın görünen her mesh'i için aynı mesh'i kontur malzemesiyle çizen bir kabuk kurar.</summary>
        Shell Build(Interactable thing)
        {
            if (shells.TryGetValue(thing, out var old)) DestroyShell(old);

            var shell = new Shell { Material = new Material(shader) { name = "Vurgu_Kabuk" } };
            foreach (var source in thing.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (source.gameObject.name == ShellName || !source.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
                // Gri kutu (modeli giydirilmiş ve görünmez) da kabuk alır ama kendi açık/kapalı durumunu izler.
                var go = new GameObject(ShellName) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(source.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var part = go.AddComponent<MeshRenderer>();
                part.shadowCastingMode = ShadowCastingMode.Off;
                part.receiveShadows = false;
                part.lightProbeUsage = LightProbeUsage.Off;

                // Modelin kendi siyah dış hat kabuğu (ters normalli alt mesh) beyaz konturu içe iterdi: o alt mesh gizli kalır.
                var mats = source.sharedMaterials;
                var shellMats = new Material[Mathf.Max(1, mats.Length)];
                for (int i = 0; i < shellMats.Length; i++)
                {
                    string n = i < mats.Length && mats[i] != null ? mats[i].name : "";
                    bool isInk = n.Contains("Dis_Hat") || n.Contains("Outline");
                    shellMats[i] = isInk ? hiddenMaterial : shell.Material;
                }
                part.sharedMaterials = shellMats;
                part.enabled = false;
                shell.Parts.Add((source, part));
            }

            if (shell.Parts.Count == 0)
            {
                Destroy(shell.Material);
                return null;
            }
            shells[thing] = shell;
            return shell;
        }

        void Release(Interactable key)
        {
            if (shells.TryGetValue(key, out var shell)) DestroyShell(shell);
            shells.Remove(key);
        }

        static void DestroyShell(Shell shell)
        {
            foreach (var (_, part) in shell.Parts)
                if (part != null) Destroy(part.gameObject);
            if (shell.Material != null) Destroy(shell.Material);
        }
    }
}
