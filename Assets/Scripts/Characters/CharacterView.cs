using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AftershockIsle.Characters
{
    /// <summary>
    /// Greybox kapsülün yerine geçen iskeletli karakter modeli. Oyun mantığı kapsülde (collider, hareket) kalır; model onun
    /// altına konur, boyu istenen değere göre ölçeklenir. Animasyon Animator Controller'sız, PlayableGraph ile sürülür:
    /// kök hareketi olan koşu klibi yerinde oynar, hızı karakterin gerçek hızına göre ayarlanır. Hareket, kökün konum
    /// farkından okunur; bu yüzden yol, doğrudan kontrol, merdiven ve saha yürüyüşü aynı kodla canlanır.
    /// </summary>
    public class CharacterView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        const float MaxSpeed = 9f;
        const float TeleportStep = 2.5f;
        const float MovingThreshold = 0.25f;
        const float BlendPerSecond = 8f;

        CharacterLibrary library;
        Transform speedSource;
        Renderer[] renderers;
        bool[][] hairSlots;
        Transform hips;
        Vector3 hipsRest;

        PlayableGraph graph;
        AnimationClipPlayable move;
        AnimationClipPlayable idle;
        AnimationMixerPlayable mixer;
        bool hasIdle;
        bool frozen;
        float moveWeight;

        MaterialPropertyBlock block;
        Color tint = Color.white;
        Vector3 lastPosition;
        float smoothedSpeed;

        /// <summary>Verilen renderer bu modele mi ait (kapsülün diğer görsellerini gizlerken ayırt etmek için).</summary>
        public bool Owns(Renderer renderer) => renderer != null && renderer.transform.IsChildOf(transform);

        /// <summary>
        /// Modeli parent'ın altına koyar. height: dünyada boy (m). feet: ayakların dünyadaki yeri.
        /// speedSource: hareketi ölçülecek nesne (kapsül değil, yerde duran kök). Kütüphane ya da prefab yoksa null.
        /// </summary>
        public static CharacterView Attach(CharacterLibrary library, Transform parent, Transform speedSource, float height, Vector3 feet)
        {
            if (library == null || library.prefab == null || parent == null) return null;

            var model = Instantiate(library.prefab, parent, false);
            model.name = "Karakter Modeli";
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Destroy(collider);

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Destroy(model);
                return null;
            }

            // Ebeveynin (kapsülün) eşitsiz ölçeğini iptal et ki model çarpılmasın; boy ölçeği üstüne gelir.
            var prefabScale = library.prefab.transform.localScale;
            var prefabRotation = library.prefab.transform.localRotation;
            var inverse = Inverse(parent.lossyScale);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, library.yawOffset, 0f) * prefabRotation;
            model.transform.localScale = Vector3.Scale(prefabScale, inverse);

            float natural = WorldBounds(renderers).size.y;
            if (natural < 0.0001f)
            {
                Destroy(model);
                return null;
            }
            model.transform.localScale = Vector3.Scale(prefabScale, inverse) * (height / natural);

            var bounds = WorldBounds(renderers);
            model.transform.position += feet - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            var view = model.AddComponent<CharacterView>();
            view.Initialize(library, renderers, speedSource != null ? speedSource : parent);
            return view;
        }

        void Initialize(CharacterLibrary source, Renderer[] modelRenderers, Transform source2)
        {
            library = source;
            renderers = modelRenderers;
            speedSource = source2;
            lastPosition = speedSource.position;

            AssignMaterials();
            foreach (var renderer in renderers)
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;

            hips = FindBone(transform, "Hips");
            if (hips != null) hipsRest = transform.InverseTransformPoint(hips.position);

            BuildGraph();
            ApplyTint();
        }

        // Dosyadaki malzemeler yerine kütüphanenin malzemeleri; saç ve gövde ayrı boyanır.
        void AssignMaterials()
        {
            hairSlots = new bool[renderers.Length][];
            for (int r = 0; r < renderers.Length; r++)
            {
                var original = renderers[r].sharedMaterials;
                var replaced = new Material[original.Length];
                hairSlots[r] = new bool[original.Length];
                for (int i = 0; i < original.Length; i++)
                {
                    bool hair = original[i] != null && original[i].name.ToLowerInvariant().Contains("hair");
                    hairSlots[r][i] = hair;
                    var material = hair ? library.hairMaterial : library.bodyMaterial;
                    replaced[i] = material != null ? material : original[i];
                }
                renderers[r].sharedMaterials = replaced;
            }
        }

        void BuildGraph()
        {
            if (library.moveClip == null) return;

            var animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;

            graph = PlayableGraph.Create($"Karakter {name}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Animasyon", animator);

            move = AnimationClipPlayable.Create(graph, library.moveClip);
            hasIdle = library.idleClip != null;
            if (hasIdle)
            {
                idle = AnimationClipPlayable.Create(graph, library.idleClip);
                mixer = AnimationMixerPlayable.Create(graph, 2);
                graph.Connect(idle, 0, mixer, 0);
                graph.Connect(move, 0, mixer, 1);
                mixer.SetInputWeight(0, 1f);
                mixer.SetInputWeight(1, 0f);
                output.SetSourcePlayable(mixer);
            }
            else
            {
                output.SetSourcePlayable(move);
                move.SetSpeed(0);
                move.SetTime(library.idlePoseTime * library.moveClip.length);
                frozen = true;
            }

            graph.Evaluate(0f);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || speedSource == null) return;

            MeasureSpeed(dt);
            if (!graph.IsValid()) return;

            bool moving = smoothedSpeed > MovingThreshold;
            float rate = Mathf.Clamp(smoothedSpeed / Mathf.Max(0.1f, library.moveSpeedReference), 0.5f, 1.4f);
            var clip = library.moveClip;

            if (hasIdle)
            {
                moveWeight = Mathf.MoveTowards(moveWeight, moving ? 1f : 0f, BlendPerSecond * dt);
                mixer.SetInputWeight(0, 1f - moveWeight);
                mixer.SetInputWeight(1, moveWeight);
                move.SetSpeed(rate);
                idle.SetSpeed(1f);
            }
            else if (moving)
            {
                move.SetSpeed(rate);
                frozen = false;
            }
            else if (!frozen)
            {
                move.SetSpeed(0);
                move.SetTime(library.idlePoseTime * clip.length);
                frozen = true;
            }

            graph.Evaluate(dt);
            Loop(move, library.moveClip);
            if (hasIdle) Loop(idle, library.idleClip);
        }

        // Klip PlayableGraph'te kendiliğinden başa sarmaz.
        static void Loop(AnimationClipPlayable playable, AnimationClip clip)
        {
            double length = clip.length;
            if (length <= 0.0001) return;
            double time = playable.GetTime();
            if (time >= length) playable.SetTime(time % length);
        }

        void MeasureSpeed(float dt)
        {
            var position = speedSource.position;
            float step = Vector3.Distance(position, lastPosition);
            lastPosition = position;

            // Işınlanma (kapıdan çıkış, sahne değişimi) koşu sayılmaz.
            float speed = step > TeleportStep ? 0f : Mathf.Min(MaxSpeed, step / dt);
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-12f * dt));
        }

        // Kök hareketli klip yerinde oynasın: kalçanın kök düzlemindeki (X-Z) kayması her kare başlangıç noktasına çekilir.
        void LateUpdate()
        {
            if (hips == null || !graph.IsValid()) return;

            var local = transform.InverseTransformPoint(hips.position);
            hips.position = transform.TransformPoint(new Vector3(hipsRest.x, local.y, hipsRest.z));
        }

        // ---------------------------------------------------------------- Renk

        /// <summary>Kazazedenin dalı, düşman türü ya da vuruş yanıp sönmesi için renk. Beyaz: vuruş (saç da beyaza döner).</summary>
        public void SetTint(Color color)
        {
            tint = color;
            ApplyTint();
        }

        void ApplyTint()
        {
            if (renderers == null) return;

            block ??= new MaterialPropertyBlock();
            bool flash = tint.r >= 0.99f && tint.g >= 0.99f && tint.b >= 0.99f;
            var body = Color.Lerp(Color.white, tint, library.hasTextures ? 0.4f : 1f);
            body.a = 1f;
            var hair = flash ? Color.white : library.hairColor;

            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null) continue;
                for (int i = 0; i < hairSlots[r].Length; i++)
                {
                    block.SetColor(BaseColorId, hairSlots[r][i] ? hair : body);
                    renderers[r].SetPropertyBlock(block, i);
                }
            }
        }

        // ---------------------------------------------------------------- Yardımcılar

        static Vector3 Inverse(Vector3 scale) => new Vector3(
            1f / Mathf.Max(0.0001f, scale.x), 1f / Mathf.Max(0.0001f, scale.y), 1f / Mathf.Max(0.0001f, scale.z));

        static Bounds WorldBounds(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static Transform FindBone(Transform root, string suffix)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>())
                if (child.name.EndsWith(suffix)) return child;
            return null;
        }
    }
}
