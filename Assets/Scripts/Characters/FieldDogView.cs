using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AftershockIsle.Characters
{
    /// <summary>
    /// RSG_DogsPack köpeğinin 3B görsel temsili.
    /// Kapsül/küp düşmanın yerine geçer; PlayableGraph ile bekleme (A_Idle_Breathing),
    /// yürüme (A_Walk) ve koşma (A_Run_Loop) animasyonlarını düşmanın anlık hızına göre harmanlar.
    /// </summary>
    public class FieldDogView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        const float MovingThreshold = 0.2f;
        const float WalkRunThreshold = 2.4f;
        const float BlendPerSecond = 8f;

        CharacterLibrary library;
        Transform speedSource;
        Renderer[] renderers;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable idlePlayable;
        AnimationClipPlayable walkPlayable;
        AnimationClipPlayable runPlayable;
        readonly float[] weights = new float[3];
        readonly float[] targetWeights = new float[3];

        MaterialPropertyBlock block;
        Color tint = Color.white;
        Vector3 lastPosition;
        float smoothedSpeed;
        bool isDead;
        float flashTimer;

        public bool Owns(Renderer r) => r != null && r.transform.IsChildOf(transform);

        public static FieldDogView Attach(CharacterLibrary library, Transform parent, Transform speedSource, float height, Vector3 feet)
        {
            if (library == null || library.dogPrefab == null || parent == null) return null;

            var model = Instantiate(library.dogPrefab, parent, false);
            model.name = "Köpek Modeli";
            foreach (var col in model.GetComponentsInChildren<Collider>()) Destroy(col);

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Destroy(model);
                return null;
            }

            var prefabScale = library.dogPrefab.transform.localScale;
            var prefabRotation = library.dogPrefab.transform.localRotation;
            var inverse = Inverse(parent.lossyScale);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, library.dogYawOffset, 0f) * prefabRotation;
            model.transform.localScale = Vector3.Scale(prefabScale, inverse);

            float natural = WorldBounds(renderers).size.y;
            if (natural > 0.0001f)
            {
                model.transform.localScale = Vector3.Scale(prefabScale, inverse) * (height / natural);
            }

            var bounds = WorldBounds(renderers);
            model.transform.position += feet - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            var view = model.AddComponent<FieldDogView>();
            view.Initialize(library, renderers, speedSource != null ? speedSource : parent, feet);
            return view;
        }

        void Initialize(CharacterLibrary source, Renderer[] dogRenderers, Transform source2, Vector3 feet)
        {
            library = source;
            renderers = dogRenderers;
            speedSource = source2;
            lastPosition = speedSource.position;

            foreach (var r in renderers)
            {
                if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }

            BuildGraph();

            var bounds = WorldBounds(renderers);
            transform.position += new Vector3(0f, feet.y - bounds.min.y, 0f);

            ApplyTint();
        }

        void BuildGraph()
        {
            var animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;

            graph = PlayableGraph.Create($"Köpek {name}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Animasyon", animator);

            mixer = AnimationMixerPlayable.Create(graph, 3);

            if (library.dogIdleClip != null)
            {
                idlePlayable = AnimationClipPlayable.Create(graph, library.dogIdleClip);
                graph.Connect(idlePlayable, 0, mixer, 0);
            }
            if (library.dogWalkClip != null)
            {
                walkPlayable = AnimationClipPlayable.Create(graph, library.dogWalkClip);
                graph.Connect(walkPlayable, 0, mixer, 1);
            }
            if (library.dogRunClip != null)
            {
                runPlayable = AnimationClipPlayable.Create(graph, library.dogRunClip);
                graph.Connect(runPlayable, 0, mixer, 2);
            }

            weights[0] = 1f;
            mixer.SetInputWeight(0, 1f);
            output.SetSourcePlayable(mixer);
            graph.Evaluate(0f);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (flashTimer > 0f)
            {
                flashTimer -= dt;
                if (flashTimer <= 0f) ApplyTint();
            }

            if (speedSource == null) return;

            MeasureSpeed(dt);
            if (!graph.IsValid()) return;

            if (isDead)
            {
                for (int i = 0; i < 3; i++) targetWeights[i] = 0f;
                targetWeights[0] = 0.1f;
            }
            else if (smoothedSpeed < MovingThreshold)
            {
                targetWeights[0] = 1f;
                targetWeights[1] = 0f;
                targetWeights[2] = 0f;
            }
            else if (smoothedSpeed < WalkRunThreshold)
            {
                float t = Mathf.InverseLerp(MovingThreshold, WalkRunThreshold, smoothedSpeed);
                targetWeights[0] = 0f;
                targetWeights[1] = 1f - t * 0.4f;
                targetWeights[2] = t * 0.4f;
            }
            else
            {
                float t = Mathf.InverseLerp(WalkRunThreshold, 5f, smoothedSpeed);
                targetWeights[0] = 0f;
                targetWeights[1] = (1f - t) * 0.3f;
                targetWeights[2] = 0.7f + t * 0.3f;
            }

            float sum = 0f;
            for (int i = 0; i < 3; i++)
            {
                weights[i] = Mathf.MoveTowards(weights[i], targetWeights[i], BlendPerSecond * dt);
                sum += weights[i];
            }
            float norm = sum > 0.0001f ? 1f / sum : 0f;
            for (int i = 0; i < 3; i++)
            {
                mixer.SetInputWeight(i, weights[i] * norm);
            }

            if (idlePlayable.IsValid()) idlePlayable.SetSpeed(1f);
            if (walkPlayable.IsValid()) walkPlayable.SetSpeed(Mathf.Clamp(smoothedSpeed / 1.6f, 0.7f, 1.5f));
            if (runPlayable.IsValid()) runPlayable.SetSpeed(Mathf.Clamp(smoothedSpeed / 4.4f, 0.7f, 1.5f));

            graph.Evaluate(dt);

            if (idlePlayable.IsValid() && library.dogIdleClip != null) Loop(idlePlayable, library.dogIdleClip);
            if (walkPlayable.IsValid() && library.dogWalkClip != null) Loop(walkPlayable, library.dogWalkClip);
            if (runPlayable.IsValid() && library.dogRunClip != null) Loop(runPlayable, library.dogRunClip);
        }

        static void Loop(AnimationClipPlayable playable, AnimationClip clip)
        {
            double len = clip.length;
            if (len <= 0.0001) return;
            double time = playable.GetTime();
            if (time >= len) playable.SetTime(time % len);
        }

        void MeasureSpeed(float dt)
        {
            var pos = speedSource.position;
            var delta = pos - lastPosition;
            float step = delta.magnitude;
            lastPosition = pos;

            float speed = step > 2.5f ? 0f : Mathf.Min(9f, step / dt);
            float blend = 1f - Mathf.Exp(-12f * dt);
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, blend);
        }

        public void PlayHit()
        {
            flashTimer = 0.09f;
            ApplyFlash();
        }

        public void SetDead()
        {
            isDead = true;
            if (graph.IsValid()) graph.Stop();
        }

        public void SetTint(Color color)
        {
            tint = color;
            if (flashTimer <= 0f) ApplyTint();
        }

        void ApplyFlash()
        {
            if (renderers == null) return;
            block ??= new MaterialPropertyBlock();
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null) continue;
                for (int m = 0; m < renderers[r].sharedMaterials.Length; m++)
                {
                    block.SetColor(BaseColorId, Color.white);
                    renderers[r].SetPropertyBlock(block, m);
                }
            }
        }

        void ApplyTint()
        {
            if (renderers == null) return;
            block ??= new MaterialPropertyBlock();
            Color finalColor = Color.Lerp(Color.white, tint, 0.35f);

            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null) continue;
                for (int m = 0; m < renderers[r].sharedMaterials.Length; m++)
                {
                    block.SetColor(BaseColorId, finalColor);
                    renderers[r].SetPropertyBlock(block, m);
                }
            }
        }

        static Vector3 Inverse(Vector3 s) => new Vector3(
            1f / Mathf.Max(0.0001f, s.x), 1f / Mathf.Max(0.0001f, s.y), 1f / Mathf.Max(0.0001f, s.z));

        static Bounds WorldBounds(Renderer[] renderers)
        {
            Bounds b = default;
            bool init = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!init) { b = r.bounds; init = true; }
                else { b.Encapsulate(r.bounds); }
            }
            if (init) return b;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    if (!init) { b = renderers[i].bounds; init = true; }
                    else { b.Encapsulate(renderers[i].bounds); }
                }
            }
            return init ? b : new Bounds(Vector3.zero, Vector3.one);
        }
    }
}
