using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AftershockIsle.Characters
{
    public enum ToolModelKind { None, Hammer, Shovel }

    /// <summary>
    /// Greybox kapsülün yerine geçen iskeletli karakter modeli ve elindeki 3B silah görünümü.
    /// Oyun mantığı kapsülde (collider, hareket) kalır; model onun altına konur, boyu istenen değere göre ölçeklenir.
    /// Animasyon Animator Controller'sız, PlayableGraph ile sürülür: bekleme (rifle aiming idle), yürüyüş (walking),
    /// koşu (rifle run), geri yürüme/koşma, yana adım (strafe) ve tek seferlik aksiyonlar (ateş, şarjör, hasar, sıçrama)
    /// karakterin gerçek hız ve yönünden okunarak harmanlanır.
    /// </summary>
    public class CharacterView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        const float MaxSpeed = 9f;
        const float TeleportStep = 2.5f;
        const float MovingThreshold = 0.2f;
        const float BlendPerSecond = 9f;
        const int LocomotionSlots = 6;
        const int ActionSlot = 6;
        const int TotalSlots = 7;

        CharacterLibrary library;
        Transform speedSource;
        Renderer[] renderers;
        bool[][] hairSlots;
        Transform hips;
        Transform rightHand;
        Transform leftHand;
        Vector3 hipsRest;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] locoPlayables;
        AnimationClip[] locoClips;
        AnimationClipPlayable actionPlayable;
        AnimationClip activeActionClip;
        readonly float[] locoWeights = new float[LocomotionSlots];
        readonly float[] targetWeights = new float[LocomotionSlots];
        float actionDuration;
        float actionElapsed;
        float actionWeight;
        bool hasIdle;
        bool frozen;

        Transform gunHolder;
        GunModelKind currentGunKind = GunModelKind.None;
        int currentGunVariant = -1;
        float currentGunLength = 0.5f;
        bool gunVisible = true;
        float fireKickTimer;

        Transform toolHolder;
        ToolModelKind currentToolKind = ToolModelKind.None;
        float workSwingTimer;

        MaterialPropertyBlock block;
        Color tint = Color.white;
        Vector3 lastPosition;
        float smoothedSpeed;
        Vector3 smoothedLocalVel;

        /// <summary>Verilen renderer bu modele (veya elindeki silaha) mı ait.</summary>
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
            view.Initialize(library, renderers, speedSource != null ? speedSource : parent, feet);
            return view;
        }

        void Initialize(CharacterLibrary source, Renderer[] modelRenderers, Transform source2, Vector3 feet)
        {
            library = source;
            renderers = modelRenderers;
            speedSource = source2;
            lastPosition = speedSource.position;

            AssignMaterials();
            foreach (var renderer in renderers)
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;

            var animator = GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            }

            if (hips == null) hips = FindBone(transform, "Hips") ?? FindBone(transform, "pelvis") ?? FindBone(transform, "Pelvis");
            if (rightHand == null) rightHand = FindBone(transform, "RightHand") ?? FindBone(transform, "hand_r") ?? FindBone(transform, "Hand_R");
            if (leftHand == null) leftHand = FindBone(transform, "LeftHand") ?? FindBone(transform, "hand_l") ?? FindBone(transform, "Hand_L");

            if (animator != null && !animator.isHuman)
                Debug.LogWarning($"[{name}] Animator Humanoid değil: avatar geçersiz ya da modelin Rig ayarı Humanoid değil. " +
                                 "Modelin FBX'inde Rig > Animation Type = Humanoid, Configure ile kemik eşlemesini kontrol et (animasyonlar bozuk görünür).", this);

            // Duruş klibi (nişan alma duruşu) dizleri büker: duran karakterin bacakları ve kalçası modelin düz duruşuna kilitlenir,
            // üst gövde (nişan, silah) animasyonda kalır. Düz duruş, animasyon henüz oynamadan kaydedilir.
            FindSpanBones(animator);
            CaptureLegRest(animator);
            float soleOffset = Mathf.Max(0f, LowestFoot() - WorldBounds(renderers).min.y);

            BuildGraph();

            if (hips != null) hipsRest = transform.InverseTransformPoint(hips.position);

            // Kilit tam uygulanmışken ayakları kemik konumundan zemine oturt (animasyon sonrası render sınırı güncel olmayabilir).
            if (legBones != null)
            {
                idleLock = 1f;
                ApplyIdleLock(0f);
                transform.position += new Vector3(0f, feet.y - (LowestFoot() - soleOffset), 0f);
            }
            else
            {
                var bounds = WorldBounds(renderers);
                transform.position += new Vector3(0f, feet.y - bounds.min.y, 0f);
            }

            ApplyTint();
        }

        // Dosyadaki malzemeler yerine kütüphanenin malzemeleri; gövde (1001) ve ikincil/saç (1002 / body1) ayrı atanır.
        // Modelin kendine ait dokulu PBR malzemeleri varsa (Adventure Character vb.) onları korur.
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
                    string matName = original[i] != null ? original[i].name.ToLowerInvariant() : string.Empty;
                    bool secondary = matName.Contains("hair") || matName.Contains("body1") || matName.Contains("1002") ||
                                     (i > 0 && library.hairMaterial != null && !matName.Contains("cloth"));
                    hairSlots[r][i] = secondary;

                    bool hasExistingTexture = original[i] != null &&
                        ((original[i].HasProperty("_BaseMap") && original[i].GetTexture("_BaseMap") != null) ||
                         (original[i].HasProperty("_MainTex") && original[i].GetTexture("_MainTex") != null));

                    if (hasExistingTexture)
                    {
                        replaced[i] = original[i];
                    }
                    else
                    {
                        var material = secondary ? library.hairMaterial : library.bodyMaterial;
                        replaced[i] = material != null ? material : original[i];
                    }
                }
                renderers[r].sharedMaterials = replaced;
            }
        }

        void BuildGraph()
        {
            var primaryMove = library.moveClip != null ? library.moveClip : library.walkClip;
            if (primaryMove == null) return;

            var animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;

            graph = PlayableGraph.Create($"Karakter {name}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Animasyon", animator);

            locoClips = new AnimationClip[LocomotionSlots];
            locoPlayables = new AnimationClipPlayable[LocomotionSlots];
            locoClips[0] = library.idleClip;
            locoClips[1] = library.walkClip != null ? library.walkClip : primaryMove;
            locoClips[2] = primaryMove;
            locoClips[3] = library.runBackClip != null ? library.runBackClip : library.walkBackClip;
            locoClips[4] = library.strafeLeftClip;
            locoClips[5] = library.strafeRightClip;

            hasIdle = locoClips[0] != null;
            mixer = AnimationMixerPlayable.Create(graph, TotalSlots);

            for (int i = 0; i < LocomotionSlots; i++)
            {
                if (locoClips[i] == null) continue;
                locoPlayables[i] = AnimationClipPlayable.Create(graph, locoClips[i]);
                graph.Connect(locoPlayables[i], 0, mixer, i);
                mixer.SetInputWeight(i, 0f);
            }

            if (hasIdle)
            {
                locoWeights[0] = 1f;
                mixer.SetInputWeight(0, 1f);
            }
            else
            {
                locoWeights[2] = 1f;
                mixer.SetInputWeight(2, 1f);
                locoPlayables[2].SetSpeed(0);
                locoPlayables[2].SetTime(library.idlePoseTime * primaryMove.length);
                frozen = true;
            }

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
            if (dt <= 0f || speedSource == null) return;

            fireKickTimer = Mathf.Max(0f, fireKickTimer - dt);
            MeasureSpeed(dt);
            if (!graph.IsValid()) return;

            bool moving = smoothedSpeed > MovingThreshold;
            isMoving = moving;
            float runRate = Mathf.Clamp(smoothedSpeed / Mathf.Max(0.1f, library.moveSpeedReference), 0.55f, 1.45f);
            float walkRate = Mathf.Clamp(smoothedSpeed / Mathf.Max(0.1f, library.walkSpeedReference), 0.65f, 1.45f);

            for (int i = 0; i < LocomotionSlots; i++) targetWeights[i] = 0f;

            if (!hasIdle)
            {
                targetWeights[2] = 1f;
                if (moving)
                {
                    locoPlayables[2].SetSpeed(runRate);
                    frozen = false;
                }
                else if (!frozen)
                {
                    locoPlayables[2].SetSpeed(0);
                    locoPlayables[2].SetTime(library.idlePoseTime * locoClips[2].length);
                    frozen = true;
                }
            }
            else if (!moving)
            {
                targetWeights[0] = 1f;
            }
            else
            {
                bool backward = smoothedLocalVel.z < -0.3f * smoothedSpeed && locoClips[3] != null;
                bool strafeL = !backward && smoothedLocalVel.x < -Mathf.Abs(smoothedLocalVel.z) * 1.05f && locoClips[4] != null;
                bool strafeR = !backward && smoothedLocalVel.x > Mathf.Abs(smoothedLocalVel.z) * 1.05f && locoClips[5] != null;

                if (backward)
                {
                    targetWeights[3] = 1f;
                }
                else if (strafeL)
                {
                    targetWeights[4] = 1f;
                }
                else if (strafeR)
                {
                    targetWeights[5] = 1f;
                }
                else if (locoClips[1] != null && locoClips[1] != locoClips[2])
                {
                    float runBlend = Mathf.InverseLerp(2.1f, 3.7f, smoothedSpeed);
                    targetWeights[1] = 1f - runBlend;
                    targetWeights[2] = runBlend;
                }
                else
                {
                    targetWeights[2] = 1f;
                }
            }

            if (locoPlayables[0].IsValid()) locoPlayables[0].SetSpeed(1f);
            if (locoPlayables[1].IsValid()) locoPlayables[1].SetSpeed(walkRate);
            if (locoPlayables[2].IsValid() && hasIdle) locoPlayables[2].SetSpeed(runRate);
            if (locoPlayables[3].IsValid()) locoPlayables[3].SetSpeed(walkRate);
            if (locoPlayables[4].IsValid()) locoPlayables[4].SetSpeed(walkRate);
            if (locoPlayables[5].IsValid()) locoPlayables[5].SetSpeed(walkRate);

            // Tek seferlik aksiyon (ateş, şarjör, hasar, sıçrama) ağırlığı.
            UpdateActionSlot(dt, moving);

            float locoScale = 1f - actionWeight;
            float sum = 0f;
            for (int i = 0; i < LocomotionSlots; i++)
            {
                locoWeights[i] = Mathf.MoveTowards(locoWeights[i], targetWeights[i], BlendPerSecond * dt);
                sum += locoWeights[i];
            }
            float norm = sum > 0.0001f ? locoScale / sum : 0f;
            for (int i = 0; i < LocomotionSlots; i++)
            {
                if (locoPlayables[i].IsValid())
                    mixer.SetInputWeight(i, locoWeights[i] * norm);
            }

            graph.Evaluate(dt);
            for (int i = 0; i < LocomotionSlots; i++)
                if (locoPlayables[i].IsValid() && locoClips[i] != null)
                    Loop(locoPlayables[i], locoClips[i]);
        }

        void UpdateActionSlot(float dt, bool moving)
        {
            if (!actionPlayable.IsValid() || activeActionClip == null)
            {
                actionWeight = 0f;
                return;
            }

            actionElapsed += dt;
            if (actionElapsed >= actionDuration)
            {
                mixer.SetInputWeight(ActionSlot, 0f);
                graph.Disconnect(mixer, ActionSlot);
                actionPlayable.Destroy();
                activeActionClip = null;
                actionWeight = 0f;
                return;
            }

            float fadeIn = Mathf.Clamp01(actionElapsed / 0.06f);
            float fadeOut = Mathf.Clamp01((actionDuration - actionElapsed) / 0.12f);
            float maxWeight = moving ? 0.78f : 0.95f;
            actionWeight = Mathf.Min(fadeIn, fadeOut) * maxWeight;
            mixer.SetInputWeight(ActionSlot, actionWeight);
        }

        void PlayActionClip(AnimationClip clip, float speed = 1f, float maxSeconds = 1.2f)
        {
            if (clip == null || !graph.IsValid()) return;

            if (actionPlayable.IsValid())
            {
                mixer.SetInputWeight(ActionSlot, 0f);
                graph.Disconnect(mixer, ActionSlot);
                actionPlayable.Destroy();
            }

            activeActionClip = clip;
            actionPlayable = AnimationClipPlayable.Create(graph, clip);
            actionPlayable.SetSpeed(speed);
            actionPlayable.SetTime(0);
            graph.Connect(actionPlayable, 0, mixer, ActionSlot);
            actionDuration = Mathf.Min(maxSeconds, clip.length / Mathf.Max(0.1f, speed));
            actionElapsed = 0f;
        }

        /// <summary>Ateş etme animasyonunu ve silah geri tepmesini tetikler.</summary>
        public void PlayFire()
        {
            fireKickTimer = 0.12f;
            if (library != null && library.fireClip != null)
                PlayActionClip(library.fireClip, 1.35f, 0.45f);
        }

        /// <summary>Şarjör değiştirme / silah hazırlama animasyonunu tetikler.</summary>
        public void PlayReload()
        {
            if (library != null && library.reloadClip != null)
                PlayActionClip(library.reloadClip, 1.35f, 1.4f);
        }

        /// <summary>Hasar alma (hit reaction) animasyonunu tetikler.</summary>
        public void PlayHit()
        {
            if (library != null && library.hitClip != null)
                PlayActionClip(library.hitClip, 1.25f, 0.65f);
        }

        /// <summary>Kaçınma / sıçrama (rifle jump) animasyonunu tetikler.</summary>
        public void PlayJump()
        {
            if (library != null && library.jumpClip != null)
                PlayActionClip(library.jumpClip, 1.35f, 0.55f);
        }

        /// <summary>Fırlatma / etkileşim animasyonunu tetikler.</summary>
        public void PlayToss()
        {
            if (library != null && library.tossClip != null)
                PlayActionClip(library.tossClip, 1.25f, 0.9f);
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
            var delta = position - lastPosition;
            float step = delta.magnitude;
            lastPosition = position;

            // Işınlanma (kapıdan çıkış, sahne değişimi) koşu sayılmaz.
            float speed = step > TeleportStep ? 0f : Mathf.Min(MaxSpeed, step / dt);
            float blend = 1f - Mathf.Exp(-12f * dt);
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, blend);

            Vector3 worldVel = step > TeleportStep ? Vector3.zero : delta / dt;
            Vector3 localVel = speedSource.InverseTransformDirection(worldVel);
            smoothedLocalVel = Vector3.Lerp(smoothedLocalVel, localVel, blend);
        }

        // Kök hareketli klip yerinde oynasın ve silah/alet ellerin konumuna hizalansın.
        // Oturma: model sandalyeye doğru yumuşakça alçalır (animasyon gelene kadar basit duruş).
        const float SeatDrop = 0.5f;
        bool seated;
        float seatBlend;
        bool seatRestCaptured;
        Vector3 seatRestPosition;

        public void SetSeated(bool value) => seated = value;

        void UpdateSeat()
        {
            if (!seatRestCaptured)
            {
                seatRestCaptured = true;
                seatRestPosition = transform.localPosition;
            }

            float target = seated ? 1f : 0f;
            if (Mathf.Approximately(seatBlend, target) && !seated) return;
            seatBlend = Mathf.MoveTowards(seatBlend, target, Time.deltaTime * 4f);
            transform.localPosition = seatRestPosition + Vector3.down * (SeatDrop * seatBlend);
        }

        void LateUpdate()
        {
            UpdateSeat();

            if (hips != null && graph.IsValid())
            {
                var local = transform.InverseTransformPoint(hips.position);
                hips.position = transform.TransformPoint(new Vector3(hipsRest.x, local.y, hipsRest.z));
            }

            if (graph.IsValid()) ApplyIdleLock(Time.deltaTime);

            UpdateGunPose();
            UpdateToolPose();
        }

        // ---------------------------------------------------------------- 3B Aletler (Çekiç, Kürek)

        public void TickWorkSwing(float dt) => workSwingTimer += dt;

        /// <summary>Karakterin eline tamir (çekiç) veya kazma (kürek) aleti verir. Alet varken silah gizlenir.</summary>
        public void SetTool(ToolModelKind kind)
        {
            if (currentToolKind == kind && (kind == ToolModelKind.None || toolHolder != null)) return;
            currentToolKind = kind;

            if (toolHolder != null)
            {
                Destroy(toolHolder.gameObject);
                toolHolder = null;
            }

            if (kind == ToolModelKind.None)
            {
                if (gunHolder != null && gunVisible) gunHolder.gameObject.SetActive(true);
                return;
            }

            if (gunHolder != null) gunHolder.gameObject.SetActive(false);

            toolHolder = new GameObject($"Alet_{kind}").transform;
            toolHolder.SetParent(transform, false);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            if (kind == ToolModelKind.Hammer)
            {
                var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(handle.GetComponent<Collider>());
                handle.transform.SetParent(toolHolder, false);
                handle.transform.localScale = new Vector3(0.024f, 0.16f, 0.024f);
                handle.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                var handleMat = new Material(shader) { color = new Color(0.42f, 0.28f, 0.16f) };
                handle.GetComponent<Renderer>().sharedMaterial = handleMat;

                var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(head.GetComponent<Collider>());
                head.transform.SetParent(toolHolder, false);
                head.transform.localScale = new Vector3(0.06f, 0.07f, 0.14f);
                head.transform.localPosition = new Vector3(0f, 0.22f, 0.01f);
                var headMat = new Material(shader) { color = new Color(0.24f, 0.26f, 0.30f) };
                head.GetComponent<Renderer>().sharedMaterial = headMat;
            }
            else if (kind == ToolModelKind.Shovel)
            {
                var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(handle.GetComponent<Collider>());
                handle.transform.SetParent(toolHolder, false);
                handle.transform.localScale = new Vector3(0.022f, 0.30f, 0.022f);
                handle.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                var handleMat = new Material(shader) { color = new Color(0.48f, 0.34f, 0.20f) };
                handle.GetComponent<Renderer>().sharedMaterial = handleMat;

                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(blade.GetComponent<Collider>());
                blade.transform.SetParent(toolHolder, false);
                blade.transform.localScale = new Vector3(0.14f, 0.18f, 0.024f);
                blade.transform.localPosition = new Vector3(0f, 0.44f, 0f);
                var bladeMat = new Material(shader) { color = new Color(0.34f, 0.37f, 0.42f) };
                blade.GetComponent<Renderer>().sharedMaterial = bladeMat;
            }

            UpdateToolPose();
        }

        void UpdateToolPose()
        {
            if (toolHolder == null) return;
            Transform basis = speedSource != null ? speedSource : transform;
            if (rightHand == null)
            {
                toolHolder.position = basis.position + basis.up * 1f + basis.forward * 0.3f;
                toolHolder.rotation = basis.rotation;
                return;
            }

            float swing = Mathf.Sin(workSwingTimer * 14f) * 24f;
            toolHolder.position = rightHand.position + basis.forward * 0.04f + basis.up * 0.02f;
            toolHolder.rotation = rightHand.rotation * Quaternion.Euler(70f + swing, 0f, -25f);
        }

        // ---------------------------------------------------------------- 3B Silah (Low Poly Guns Pack)

        /// <summary>Karakterin eline Low Poly Guns paketinden uygun 3B silah modelini verir.</summary>
        public void SetGun(GunModelKind kind, int variant = 0)
        {
            if (currentGunKind == kind && currentGunVariant == variant && (kind == GunModelKind.None || gunHolder != null))
                return;

            currentGunKind = kind;
            currentGunVariant = variant;

            if (gunHolder != null)
            {
                Destroy(gunHolder.gameObject);
                gunHolder = null;
            }

            if (library == null || kind == GunModelKind.None || !library.TryGetGun(kind, variant, out var entry))
                return;

            gunHolder = new GameObject($"Silah ({entry.id})").transform;
            gunHolder.SetParent(transform, false);

            var gunModel = Instantiate(entry.prefab, gunHolder, false);
            gunModel.transform.localPosition = Vector3.zero;
            gunModel.transform.localRotation = Quaternion.identity;
            gunModel.transform.localScale = Vector3.one;

            foreach (var col in gunModel.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var mb in gunModel.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);

            var gunRenderers = gunModel.GetComponentsInChildren<Renderer>();
            if (gunRenderers.Length == 0)
            {
                Destroy(gunHolder.gameObject);
                gunHolder = null;
                return;
            }

            if (entry.material != null)
            {
                foreach (var r in gunRenderers)
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = entry.material;
                    r.sharedMaterials = mats;
                }
            }

            // Silahı hedef dünya uzunluğuna (m) göre ölçekle ve merkezini gunHolder'a hizala.
            gunHolder.rotation = Quaternion.identity;
            var rawBounds = WorldBounds(gunRenderers);
            float rawLength = Mathf.Max(0.001f, Mathf.Max(rawBounds.size.x, Mathf.Max(rawBounds.size.y, rawBounds.size.z)));
            currentGunLength = Mathf.Max(0.2f, entry.worldLength);
            float scaleFactor = currentGunLength / rawLength;
            gunModel.transform.localScale = Vector3.one * scaleFactor;

            var scaledBounds = WorldBounds(gunRenderers);
            gunModel.transform.position += gunHolder.position - scaledBounds.center;

            gunHolder.gameObject.SetActive(gunVisible && currentToolKind == ToolModelKind.None);
            UpdateGunPose();
        }

        /// <summary>Karakter kutu/malzeme taşırken veya alet kullanırken elindeki silahı geçici olarak gizler.</summary>
        public void SetGunVisible(bool visible)
        {
            gunVisible = visible;
            bool shouldShow = visible && currentToolKind == ToolModelKind.None;
            if (gunHolder != null && gunHolder.gameObject.activeSelf != shouldShow)
                gunHolder.gameObject.SetActive(shouldShow);
        }

        void UpdateGunPose()
        {
            if (gunHolder == null || !gunVisible) return;

            Transform basis = speedSource != null ? speedSource : transform;
            Vector3 up = basis.up;
            Vector3 forward = basis.forward;

            if (rightHand == null)
            {
                gunHolder.position = basis.position + up * 1.15f + forward * 0.35f;
                gunHolder.rotation = Quaternion.LookRotation(forward, up);
                return;
            }

            Vector3 barrelDir = forward;
            if (leftHand != null)
            {
                Vector3 handToHand = leftHand.position - rightHand.position;
                if (handToHand.sqrMagnitude > 0.004f)
                {
                    Vector3 handDir = handToHand.normalized;
                    if (Vector3.Dot(handDir, forward) > -0.15f)
                    {
                        barrelDir = currentGunKind == GunModelKind.Pistol
                            ? Vector3.Slerp(handDir, forward, 0.55f).normalized
                            : handDir;
                    }
                }
            }

            float kick = fireKickTimer > 0f ? (fireKickTimer / 0.12f) : 0f;
            float forwardOffset = currentGunLength * (currentGunKind == GunModelKind.Pistol ? 0.14f : 0.22f) - kick * 0.04f;
            gunHolder.position = rightHand.position + barrelDir * forwardOffset + up * 0.015f;
            gunHolder.rotation = Quaternion.LookRotation(barrelDir, up) * Quaternion.Euler(-kick * 9f, 0f, 0f);
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
            var body = Color.Lerp(Color.white, tint, library.hasTextures ? 0.35f : 1f);
            body.a = 1f;
            var secondary = flash
                ? Color.white
                : (library.hasSecondaryTexture ? Color.Lerp(Color.white, tint, 0.25f) : library.hairColor);
            secondary.a = 1f;

            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null) continue;
                for (int i = 0; i < hairSlots[r].Length; i++)
                {
                    block.SetColor(BaseColorId, hairSlots[r][i] ? secondary : body);
                    renderers[r].SetPropertyBlock(block, i);
                }
            }
        }

        // ---------------------------------------------------------------- Yardımcılar

        static Vector3 Inverse(Vector3 scale) => new Vector3(
            1f / Mathf.Max(0.0001f, scale.x), 1f / Mathf.Max(0.0001f, scale.y), 1f / Mathf.Max(0.0001f, scale.z));

        Transform footLeftBone, footRightBone;

        void FindSpanBones(Animator animator)
        {
            if (animator != null && animator.isHuman)
            {
                footLeftBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                footRightBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            if (footLeftBone == null) footLeftBone = FindBone(transform, "LeftFoot") ?? FindBone(transform, "foot_l") ?? FindBone(transform, "Foot_L");
            if (footRightBone == null) footRightBone = FindBone(transform, "RightFoot") ?? FindBone(transform, "foot_r") ?? FindBone(transform, "Foot_R");
        }

        float LowestFoot()
        {
            float low = float.MaxValue;
            if (footLeftBone != null) low = Mathf.Min(low, footLeftBone.position.y);
            if (footRightBone != null) low = Mathf.Min(low, footRightBone.position.y);
            return low == float.MaxValue ? 0f : low;
        }

        // Bacaklar: duran karakterde modelin düz duruşuna kilitlenir; yürürken klibin diz/uyluk bükülmesi kısmen silinir.
        // Klibin kalçası bu modelin bacak oranlarında yere gömülüyorsa, ayaklar zemin hizasına geri çıkarılır.
        const float IdleLockBlendPerSecond = 6f;
        enum LegRole { Thigh, Calf, Foot, Toes }
        Transform[] legBones;
        LegRole[] legRoles;
        Quaternion[] legRest;
        float hipsRestHeight;
        float idleLock;
        bool isMoving;

        void CaptureLegRest(Animator animator)
        {
            var bones = new System.Collections.Generic.List<Transform>();
            var roles = new System.Collections.Generic.List<LegRole>();
            void Add(LegRole role, HumanBodyBones bone, params string[] names)
            {
                Transform found = animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
                for (int i = 0; found == null && i < names.Length; i++) found = FindBone(transform, names[i]);
                if (found == null || bones.Contains(found)) return;
                bones.Add(found);
                roles.Add(role);
            }

            Add(LegRole.Thigh, HumanBodyBones.LeftUpperLeg, "LeftUpLeg", "thigh_l", "Thigh_L");
            Add(LegRole.Calf, HumanBodyBones.LeftLowerLeg, "LeftLeg", "calf_l", "Calf_L");
            Add(LegRole.Foot, HumanBodyBones.LeftFoot, "LeftFoot", "foot_l", "Foot_L");
            Add(LegRole.Toes, HumanBodyBones.LeftToes, "LeftToeBase", "ball_l", "Ball_L");
            Add(LegRole.Thigh, HumanBodyBones.RightUpperLeg, "RightUpLeg", "thigh_r", "Thigh_R");
            Add(LegRole.Calf, HumanBodyBones.RightLowerLeg, "RightLeg", "calf_r", "Calf_R");
            Add(LegRole.Foot, HumanBodyBones.RightFoot, "RightFoot", "foot_r", "Foot_R");
            Add(LegRole.Toes, HumanBodyBones.RightToes, "RightToeBase", "ball_r", "Ball_R");

            // En az uyluk-baldır-ayak (6 kemik) yoksa kullanılmaz; eksik eşleme animasyonu bozmasın.
            if (bones.Count < 6 || hips == null) return;

            legBones = bones.ToArray();
            legRoles = roles.ToArray();
            legRest = new Quaternion[legBones.Length];
            for (int i = 0; i < legBones.Length; i++) legRest[i] = legBones[i].localRotation;
            hipsRestHeight = transform.InverseTransformPoint(hips.position).y;
        }

        /// <summary>Yalnızca duran karakterde bacak kemiklerini ve kalça yüksekliğini düz duruşa çeker; yürürken animasyona dokunmaz.</summary>
        void ApplyIdleLock(float dt)
        {
            if (legBones == null) return;

            float target = !isMoving && !seated ? 1f : 0f;
            idleLock = Mathf.MoveTowards(idleLock, target, IdleLockBlendPerSecond * dt);
            if (idleLock <= 0f) return;

            for (int i = 0; i < legBones.Length; i++)
                legBones[i].localRotation = Quaternion.Slerp(legBones[i].localRotation, legRest[i], idleLock);

            if (hips != null)
            {
                var local = transform.InverseTransformPoint(hips.position);
                local.y = Mathf.Lerp(local.y, hipsRestHeight, idleLock);
                hips.position = transform.TransformPoint(local);
            }
        }

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

        static Transform FindBone(Transform root, string suffix)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>())
            {
                if (child.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(child.name, suffix, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
            return null;
        }
    }
}
