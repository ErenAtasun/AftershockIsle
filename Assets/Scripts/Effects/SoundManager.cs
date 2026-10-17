using System;
using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Endgame;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using AftershockIsle.Player;
using AftershockIsle.Save;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Effects
{
    public enum SoundId
    {
        UiHover,
        UiClick,
        SlingFire,
        PistolFire,
        ShotgunFire,
        SmgFire,
        AssaultRifleFire,
        SniperRifleFire,
        Reload,
        EmptyClick,
        BulletHit,
        PlayerHurt,
        Dodge,
        MeleeSwing,
        AftershockWarning,
        AftershockImpact,
        ColumnBroken,
        ColumnRepaired,
        CeilingCollapsed,
        RubbleCleared,
        PipeBurst,
        Electrocution,
        FireStarted,
        FireExtinguished,
        GasLeak,
        GasExplosion,
        BreakerToggle,
        CraftSuccess,
        PlaceStructure,
        DoorKnock,
        RadioSignal,
        HelicopterInbound,
        DayPhaseChange,
    }

    /// <summary>
    /// Merkezi ses yöneticisi.
    /// Assets/Resources/Audio/&lt;SoundId&gt; altında özel ses dosyası (.wav/.ogg/.mp3) varsa onu yükler;
    /// yoksa çalışma anında katmanlı prosedürel ses dalgaları sentezler. Sahneyi yeniden kurmaya gerek kalmadan
    /// açılışta kendini oluşturur ve EventBus olaylarını otomatik dinler.
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        const int SampleRate = 44100;

        public static SoundManager Instance { get; private set; }

        readonly Dictionary<SoundId, AudioClip> clips = new Dictionary<SoundId, AudioClip>();
        AudioSource sfxSource;
        AudioSource uiSource;
        AudioSource ambientSource;
        AudioSource radioLoopSource;
        float lastHoverTime;
        float radioBeepTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var existing = FindFirstObjectByType<SoundManager>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var go = new GameObject("[SoundManager]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SoundManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            sfxSource = CreateSource("SFX", false, 0.85f);
            uiSource = CreateSource("UI", false, 0.7f);
            ambientSource = CreateSource("Ambient", true, 0.11f);
            radioLoopSource = CreateSource("Radio", false, 0.35f);

            BuildClipLibrary();

            var ambientClip = Resources.Load<AudioClip>("Audio/AmbientLoop") ?? SynthesizeAmbientWind(6.0f);
            ambientSource.clip = ambientClip;
            ambientSource.Play();
        }

        AudioSource CreateSource(string label, bool loop, float baseVol)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;
            src.volume = baseVol;
            return src;
        }

        void OnEnable()
        {
            EventBus.Subscribe<PowerChanged>(OnPowerChanged);
            EventBus.Subscribe<StructurePlaced>(OnStructurePlaced);
            EventBus.Subscribe<BuildingCollapsed>(OnBuildingCollapsed);
            EventBus.Subscribe<ColumnStateChanged>(OnColumnStateChanged);
            EventBus.Subscribe<SurvivorDied>(OnSurvivorDied);
            EventBus.Subscribe<SurvivorSelected>(OnSurvivorSelected);
            EventBus.Subscribe<DayPhaseChanged>(OnDayPhaseChanged);
            EventBus.Subscribe<RecipeCrafted>(OnRecipeCrafted);
            EventBus.Subscribe<AftershockWarning>(OnAftershockWarning);
            EventBus.Subscribe<AftershockImpact>(OnAftershockImpact);
            EventBus.Subscribe<FireStarted>(OnFireStarted);
            EventBus.Subscribe<FireExtinguished>(OnFireExtinguished);
            EventBus.Subscribe<CeilingCollapsed>(OnCeilingCollapsed);
            EventBus.Subscribe<ElectrocutionStarted>(OnElectrocution);
            EventBus.Subscribe<GasLeakStarted>(OnGasLeakStarted);
            EventBus.Subscribe<GasExploded>(OnGasExploded);
            EventBus.Subscribe<HelicopterInbound>(OnHelicopterInbound);
            EventBus.Subscribe<RadioRepaired>(OnRadioRepaired);
            EventBus.Subscribe<SignalCompleted>(OnSignalCompleted);
            EventBus.Subscribe<PipeBurst>(OnPipeBurst);
            EventBus.Subscribe<FurnitureDismantled>(OnFurnitureDismantled);
            EventBus.Subscribe<RubbleCleared>(OnRubbleCleared);
            EventBus.Subscribe<SkillUnlocked>(OnSkillUnlocked);
            EventBus.Subscribe<SaveCompleted>(OnSaveCompleted);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<PowerChanged>(OnPowerChanged);
            EventBus.Unsubscribe<StructurePlaced>(OnStructurePlaced);
            EventBus.Unsubscribe<BuildingCollapsed>(OnBuildingCollapsed);
            EventBus.Unsubscribe<ColumnStateChanged>(OnColumnStateChanged);
            EventBus.Unsubscribe<SurvivorDied>(OnSurvivorDied);
            EventBus.Unsubscribe<SurvivorSelected>(OnSurvivorSelected);
            EventBus.Unsubscribe<DayPhaseChanged>(OnDayPhaseChanged);
            EventBus.Unsubscribe<RecipeCrafted>(OnRecipeCrafted);
            EventBus.Unsubscribe<AftershockWarning>(OnAftershockWarning);
            EventBus.Unsubscribe<AftershockImpact>(OnAftershockImpact);
            EventBus.Unsubscribe<FireStarted>(OnFireStarted);
            EventBus.Unsubscribe<FireExtinguished>(OnFireExtinguished);
            EventBus.Unsubscribe<CeilingCollapsed>(OnCeilingCollapsed);
            EventBus.Unsubscribe<ElectrocutionStarted>(OnElectrocution);
            EventBus.Unsubscribe<GasLeakStarted>(OnGasLeakStarted);
            EventBus.Unsubscribe<GasExploded>(OnGasExploded);
            EventBus.Unsubscribe<HelicopterInbound>(OnHelicopterInbound);
            EventBus.Unsubscribe<RadioRepaired>(OnRadioRepaired);
            EventBus.Unsubscribe<SignalCompleted>(OnSignalCompleted);
            EventBus.Unsubscribe<PipeBurst>(OnPipeBurst);
            EventBus.Unsubscribe<FurnitureDismantled>(OnFurnitureDismantled);
            EventBus.Unsubscribe<RubbleCleared>(OnRubbleCleared);
            EventBus.Unsubscribe<SkillUnlocked>(OnSkillUnlocked);
            EventBus.Unsubscribe<SaveCompleted>(OnSaveCompleted);
        }

        void Update()
        {
            // Çatıdaki telsiz sinyal gönderirken periyodik mors/telemetri sesi çalar.
            var radio = RadioStation.Instance;
            if (radio != null && radio.IsRepaired && !radio.SignalComplete && radio.Signal01 > 0.005f)
            {
                radioBeepTimer -= Time.deltaTime;
                if (radioBeepTimer <= 0f)
                {
                    radioBeepTimer = 1.6f;
                    if (clips.TryGetValue(SoundId.RadioSignal, out var clip) && clip != null)
                    {
                        radioLoopSource.pitch = UnityEngine.Random.Range(0.95f, 1.06f);
                        radioLoopSource.PlayOneShot(clip, 0.45f);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- Genel Çağrılar

        public static void Play(SoundId id, float volumeScale = 1f, float pitchJitter = 0.05f)
        {
            if (Instance == null) return;
            Instance.PlayInternal(id, volumeScale, pitchJitter);
        }

        public static void PlayUiHover()
        {
            if (Instance == null) return;
            if (Time.unscaledTime - Instance.lastHoverTime < 0.045f) return;
            Instance.lastHoverTime = Time.unscaledTime;
            if (Instance.clips.TryGetValue(SoundId.UiHover, out var clip) && clip != null)
            {
                Instance.uiSource.pitch = UnityEngine.Random.Range(0.97f, 1.03f);
                Instance.uiSource.PlayOneShot(clip, 0.4f);
            }
        }

        public static void PlayUiClick()
        {
            if (Instance == null) return;
            if (Instance.clips.TryGetValue(SoundId.UiClick, out var clip) && clip != null)
            {
                Instance.uiSource.pitch = UnityEngine.Random.Range(0.96f, 1.04f);
                Instance.uiSource.PlayOneShot(clip, 0.65f);
            }
        }

        public static void PlayWeaponFire(WeaponKind kind, float volumeScale = 1f)
        {
            SoundId id = kind switch
            {
                WeaponKind.Slingshot => SoundId.SlingFire,
                WeaponKind.Pistol => SoundId.PistolFire,
                WeaponKind.Shotgun => SoundId.ShotgunFire,
                WeaponKind.Smg => SoundId.SmgFire,
                WeaponKind.AssaultRifle => SoundId.AssaultRifleFire,
                WeaponKind.SniperRifle => SoundId.SniperRifleFire,
                _ => SoundId.PistolFire,
            };
            Play(id, volumeScale, 0.06f);
        }

        void PlayInternal(SoundId id, float volumeScale, float pitchJitter)
        {
            if (!clips.TryGetValue(id, out var clip) || clip == null) return;
            sfxSource.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        }

        // ---------------------------------------------------------------- Olay Dinleyicileri

        void OnPowerChanged(PowerChanged evt) => Play(SoundId.BreakerToggle, 0.85f, 0.03f);
        void OnStructurePlaced(StructurePlaced evt) => Play(SoundId.PlaceStructure, 0.85f, 0.04f);
        void OnBuildingCollapsed(BuildingCollapsed evt) => Play(SoundId.CeilingCollapsed, 1f, 0.02f);

        void OnColumnStateChanged(ColumnStateChanged evt)
        {
            if (evt.Column.State == ColumnState.Broken) Play(SoundId.ColumnBroken, 0.95f, 0.04f);
            else Play(SoundId.ColumnRepaired, 0.8f, 0.04f);
        }

        void OnSurvivorDied(SurvivorDied evt) => Play(SoundId.PlayerHurt, 0.95f, 0.03f);
        void OnSurvivorSelected(SurvivorSelected evt)
        {
            if (evt.Survivor != null) PlayUiHover();
        }

        void OnDayPhaseChanged(DayPhaseChanged evt) => Play(SoundId.DayPhaseChange, 0.55f, 0.02f);
        void OnRecipeCrafted(RecipeCrafted evt) => Play(SoundId.CraftSuccess, 0.85f, 0.03f);
        void OnAftershockWarning(AftershockWarning evt) => Play(SoundId.AftershockWarning, 0.85f, 0.02f);
        void OnAftershockImpact(AftershockImpact evt) => Play(SoundId.AftershockImpact, 1f, 0.03f);
        void OnFireStarted(FireStarted evt) => Play(SoundId.FireStarted, 0.85f, 0.04f);
        void OnFireExtinguished(FireExtinguished evt) => Play(SoundId.FireExtinguished, 0.8f, 0.04f);
        void OnCeilingCollapsed(CeilingCollapsed evt) => Play(SoundId.CeilingCollapsed, 0.95f, 0.03f);
        void OnElectrocution(ElectrocutionStarted evt) => Play(SoundId.Electrocution, 0.9f, 0.05f);
        void OnGasLeakStarted(GasLeakStarted evt) => Play(SoundId.GasLeak, 0.8f, 0.04f);
        void OnGasExploded(GasExploded evt) => Play(SoundId.GasExplosion, 1f, 0.03f);
        void OnHelicopterInbound(HelicopterInbound evt) => Play(SoundId.HelicopterInbound, 0.95f, 0.02f);
        void OnRadioRepaired(RadioRepaired evt) => Play(SoundId.CraftSuccess, 0.9f, 0.02f);
        void OnSignalCompleted(SignalCompleted evt) => Play(SoundId.RadioSignal, 0.9f, 0.01f);
        void OnPipeBurst(PipeBurst evt) => Play(SoundId.PipeBurst, 0.9f, 0.04f);
        void OnFurnitureDismantled(FurnitureDismantled evt) => Play(SoundId.RubbleCleared, 0.75f, 0.05f);
        void OnRubbleCleared(RubbleCleared evt) => Play(SoundId.RubbleCleared, 0.85f, 0.05f);
        void OnSkillUnlocked(SkillUnlocked evt) => Play(SoundId.CraftSuccess, 0.8f, 0.02f);
        void OnSaveCompleted(SaveCompleted evt)
        {
            if (evt.Success) PlayUiClick();
        }

        // ---------------------------------------------------------------- Ses Kütüphanesi ve Sentezleyici

        void BuildClipLibrary()
        {
            foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
            {
                var custom = Resources.Load<AudioClip>($"Audio/{id}");
                clips[id] = custom != null ? custom : Synthesize(id);
            }
        }

        static AudioClip Synthesize(SoundId id)
        {
            switch (id)
            {
                case SoundId.UiHover:
                    return MakeClip("UiHover", 0.032f, (t, u) =>
                         Env(u, 0.08f, 14f) * Sin(t, Mathf.Lerp(520f, 340f, u)) * 0.25f);

                case SoundId.UiClick:
                    return MakeClip("UiClick", 0.065f, (t, u) =>
                        (Env(u, 0.04f, 22f) * Sin(t, Mathf.Lerp(680f, 220f, u)) * 0.45f) +
                        (Env(u, 0.02f, 45f) * Noise(t * 9f) * 0.18f));

                case SoundId.SlingFire:
                    return MakeClip("SlingFire", 0.18f, (t, u) =>
                    {
                        float freq = Mathf.Lerp(160f, 420f, Mathf.Sin(u * Mathf.PI));
                        return Env(u, 0.06f, 8f) * (Sin(t, freq) * 0.45f + Noise(t * 2.5f) * 0.3f);
                    });

                case SoundId.PistolFire:
                    return SynthesizeGunshot("PistolFire", 0.26f, 165f, 48f, 14f, 0.75f, 0.25f);

                case SoundId.ShotgunFire:
                    return SynthesizeGunshot("ShotgunFire", 0.44f, 105f, 30f, 8.5f, 0.95f, 0.55f, pumpClackAt: 0.23f);

                case SoundId.SmgFire:
                    return SynthesizeGunshot("SmgFire", 0.15f, 210f, 62f, 20f, 0.65f, 0.18f);

                case SoundId.AssaultRifleFire:
                    return SynthesizeGunshot("AssaultRifleFire", 0.28f, 145f, 42f, 13f, 0.85f, 0.35f);

                case SoundId.SniperRifleFire:
                    return SynthesizeGunshot("SniperRifleFire", 0.58f, 120f, 26f, 6.2f, 1.0f, 0.65f);

                case SoundId.Reload:
                    return MakeClip("Reload", 0.52f, (t, u) =>
                    {
                        float c1 = Pulse(t, 0.04f, 0.045f) * (Sin(t, 920f) * 0.4f + Noise(t * 6f) * 0.35f);
                        float c2 = Pulse(t, 0.22f, 0.055f) * (Sin(t, 1240f) * 0.45f + Noise(t * 7f) * 0.4f);
                        float c3 = Pulse(t, 0.38f, 0.07f) * (Sin(t, Mathf.Lerp(780f, 1480f, Mathf.Clamp01((t - 0.38f) / 0.07f))) * 0.45f + Noise(t * 5f) * 0.35f);
                        return (c1 + c2 + c3) * 0.75f;
                    });

                case SoundId.EmptyClick:
                    return MakeClip("EmptyClick", 0.045f, (t, u) =>
                        Env(u, 0.03f, 35f) * (Sin(t, 1350f) * 0.45f + Sin(t, 2450f) * 0.25f));

                case SoundId.BulletHit:
                    return MakeClip("BulletHit", 0.11f, (t, u) =>
                        Env(u, 0.04f, 18f) * (Sin(t, Mathf.Lerp(130f, 42f, u)) * 0.65f + Noise(t * 3f) * 0.3f));

                case SoundId.PlayerHurt:
                    return MakeClip("PlayerHurt", 0.24f, (t, u) =>
                        Env(u, 0.05f, 10f) * (Sin(t, Mathf.Lerp(155f, 52f, u)) * 0.7f + Noise(t * 1.8f) * 0.35f));

                case SoundId.Dodge:
                    return MakeClip("Dodge", 0.18f, (t, u) =>
                        Mathf.Sin(u * Mathf.PI) * Noise(t * Mathf.Lerp(1.5f, 3.8f, u)) * 0.38f);

                case SoundId.MeleeSwing:
                    return MakeClip("MeleeSwing", 0.19f, (t, u) =>
                        Env(u, 0.12f, 11f) * (Sin(t, Mathf.Lerp(210f, 68f, u)) * 0.5f + Noise(t * 2.2f) * 0.35f));

                case SoundId.AftershockWarning:
                    return MakeClip("AftershockWarning", 1.25f, (t, u) =>
                    {
                        float rumble = Mathf.Sin(u * Mathf.PI) * (Sin(t, 44f) * 0.4f + Sin(t, 61f) * 0.25f);
                        float beep = (Pulse(t, 0.08f, 0.28f) + Pulse(t, 0.52f, 0.28f)) * Sin(t, 440f) * 0.28f;
                        return (rumble + beep) * 0.75f;
                    });

                case SoundId.AftershockImpact:
                    return SynthesizeEarthquake(2.2f);

                case SoundId.ColumnBroken:
                case SoundId.CeilingCollapsed:
                    return MakeClip("Collapse", 0.85f, (t, u) =>
                        Env(u, 0.03f, 4.2f) * (Sin(t, Mathf.Lerp(95f, 28f, u)) * 0.65f + Noise(t * 1.4f) * 0.55f));

                case SoundId.ColumnRepaired:
                    return MakeClip("ColumnRepaired", 0.62f, (t, u) =>
                    {
                        float h1 = Pulse(t, 0.03f, 0.09f) * (Sin(t, 620f) * 0.45f + Sin(t, 1240f) * 0.25f + Noise(t * 5f) * 0.3f);
                        float h2 = Pulse(t, 0.22f, 0.09f) * (Sin(t, 680f) * 0.45f + Sin(t, 1360f) * 0.25f + Noise(t * 5f) * 0.3f);
                        float h3 = Pulse(t, 0.41f, 0.14f) * (Sin(t, 820f) * 0.5f + Sin(t, 1640f) * 0.3f + Noise(t * 5f) * 0.25f);
                        return (h1 + h2 + h3) * 0.75f;
                    });

                case SoundId.RubbleCleared:
                    return MakeClip("RubbleCleared", 0.42f, (t, u) =>
                        Env(u, 0.08f, 6.5f) * (Noise(t * 1.8f) * 0.45f + Sin(t, Mathf.Lerp(140f, 55f, u)) * 0.35f));

                case SoundId.PipeBurst:
                    return MakeClip("PipeBurst", 0.75f, (t, u) =>
                        Env(u, 0.02f, 4.5f) * (Sin(t, Mathf.Lerp(260f, 80f, u)) * 0.4f + Noise(t * 5.5f) * 0.5f));

                case SoundId.Electrocution:
                    return MakeClip("Electrocution", 0.5f, (t, u) =>
                    {
                        float buzz = Sin(t, 100f) * 0.35f + Sin(t, 200f) * 0.25f + Sin(t, 300f) * 0.2f;
                        float zap = Noise(t * 12f) * (0.5f + 0.5f * Sin(t, 28f));
                        return Env(u, 0.03f, 5.5f) * (buzz + zap * 0.45f) * 0.8f;
                    });

                case SoundId.FireStarted:
                    return MakeClip("FireStarted", 0.6f, (t, u) =>
                        Env(u, 0.08f, 4.5f) * (Sin(t, Mathf.Lerp(110f, 55f, u)) * 0.45f + Noise(t * 2.2f) * 0.45f));

                case SoundId.FireExtinguished:
                case SoundId.GasLeak:
                    return MakeClip("SteamHiss", 0.55f, (t, u) =>
                        Env(u, 0.05f, 5.2f) * Noise(t * 7.5f) * 0.42f);

                case SoundId.GasExplosion:
                    return SynthesizeGunshot("GasExplosion", 1.15f, 78f, 22f, 3.5f, 1.0f, 0.8f);

                case SoundId.BreakerToggle:
                    return MakeClip("BreakerToggle", 0.22f, (t, u) =>
                    {
                        float clunk = Pulse(t, 0.01f, 0.07f) * (Sin(t, 180f) * 0.55f + Noise(t * 4f) * 0.4f);
                        float latch = Pulse(t, 0.09f, 0.08f) * (Sin(t, 340f) * 0.45f + Sin(t, 680f) * 0.25f);
                        return (clunk + latch) * 0.85f;
                    });

                case SoundId.CraftSuccess:
                    return MakeClip("CraftSuccess", 0.52f, (t, u) =>
                    {
                        float n1 = Pulse(t, 0.01f, 0.18f) * (Sin(t, 523.25f) * 0.35f + Sin(t, 1046.5f) * 0.15f);
                        float n2 = Pulse(t, 0.15f, 0.34f) * (Sin(t, 659.25f) * 0.38f + Sin(t, 1318.5f) * 0.16f);
                        return (n1 + n2) * 0.75f;
                    });

                case SoundId.PlaceStructure:
                    return MakeClip("PlaceStructure", 0.20f, (t, u) =>
                        Env(u, 0.03f, 14f) * (Sin(t, Mathf.Lerp(175f, 68f, u)) * 0.6f + Noise(t * 2.5f) * 0.25f));

                case SoundId.DoorKnock:
                    return MakeClip("DoorKnock", 0.58f, (t, u) =>
                    {
                        float k1 = Pulse(t, 0.02f, 0.09f) * (Sin(t, Mathf.Lerp(190f, 85f, (t - 0.02f) / 0.09f)) * 0.65f + Noise(t * 2f) * 0.2f);
                        float k2 = Pulse(t, 0.20f, 0.09f) * (Sin(t, Mathf.Lerp(195f, 88f, (t - 0.20f) / 0.09f)) * 0.7f + Noise(t * 2f) * 0.2f);
                        float k3 = Pulse(t, 0.38f, 0.11f) * (Sin(t, Mathf.Lerp(185f, 82f, (t - 0.38f) / 0.11f)) * 0.75f + Noise(t * 2f) * 0.2f);
                        return (k1 + k2 + k3) * 0.85f;
                    });

                case SoundId.RadioSignal:
                    return MakeClip("RadioSignal", 0.42f, (t, u) =>
                    {
                        float morse = (Pulse(t, 0.02f, 0.08f) + Pulse(t, 0.14f, 0.08f) + Pulse(t, 0.26f, 0.12f)) * Sin(t, 1080f) * 0.32f;
                        float stat = Env(u, 0.05f, 5f) * Noise(t * 8f) * 0.12f;
                        return morse + stat;
                    });

                case SoundId.HelicopterInbound:
                    return MakeClip("HelicopterInbound", 1.8f, (t, u) =>
                    {
                        float chop = 0.35f + 0.65f * Mathf.Pow(0.5f + 0.5f * Sin(t, 11.5f), 3f);
                        float body = Sin(t, 58f) * 0.45f + Noise(t * 1.6f) * 0.45f;
                        return Mathf.Sin(u * Mathf.PI) * chop * body * 0.85f;
                    });

                case SoundId.DayPhaseChange:
                default:
                    return MakeClip("DayPhaseChange", 0.65f, (t, u) =>
                        Mathf.Sin(u * Mathf.PI) * (Sin(t, 293.66f) * 0.28f + Sin(t, 440f) * 0.22f));
            }
        }

        static AudioClip SynthesizeGunshot(string name, float duration, float startFreq, float endFreq, float decay,
                                           float punch, float tailStrength, float pumpClackAt = -1f)
        {
            int samples = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            float lpState = 0f;
            uint rng = (uint)name.GetHashCode() ^ 0x9E3779B9u;

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;
                float u = i / (float)samples;

                rng ^= rng << 13;
                rng ^= rng >> 17;
                rng ^= rng << 5;
                float white = (rng & 0xFFFF) / 32767.5f - 1f;

                // Zamanla koyulaşan alçak geçiren filtre (barut patlaması -> şehir yankısı)
                float cutoff = Mathf.Lerp(0.58f, 0.09f, Mathf.Pow(u, 0.45f));
                lpState += cutoff * (white - lpState);

                float thumpEnv = Mathf.Exp(-t * decay * 1.4f);
                float freq = Mathf.Lerp(startFreq, endFreq, Mathf.Clamp01(t * decay * 0.7f));
                float thump = Mathf.Sin(2f * Mathf.PI * freq * t) * thumpEnv * punch;

                float blastEnv = Mathf.Exp(-t * decay);
                float tailEnv = Mathf.Exp(-t * (decay * 0.35f)) * tailStrength;
                float clack = 0f;
                if (pumpClackAt > 0f && t >= pumpClackAt && t <= pumpClackAt + 0.08f)
                {
                    float cu = (t - pumpClackAt) / 0.08f;
                    clack = Mathf.Sin(cu * Mathf.PI) * (Mathf.Sin(2f * Mathf.PI * 680f * t) * 0.25f + lpState * 0.3f);
                }

                float sample = thump + lpState * (blastEnv * 0.85f + tailEnv) + clack;
                data[i] = (float)Math.Tanh(sample * 1.35f) * 0.82f;
            }

            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip SynthesizeEarthquake(float duration)
        {
            int samples = Mathf.Max(256, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            float lp = 0f;
            uint rng = 0xA341316Cu;

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;
                float u = i / (float)samples;

                rng ^= rng << 13;
                rng ^= rng >> 17;
                rng ^= rng << 5;
                float white = (rng & 0xFFFF) / 32767.5f - 1f;
                lp += 0.06f * (white - lp);

                float env = Mathf.Sin(Mathf.Pow(u, 0.6f) * Mathf.PI);
                float sub = Mathf.Sin(2f * Mathf.PI * 38f * t) * 0.45f +
                            Mathf.Sin(2f * Mathf.PI * 57f * t) * 0.3f;
                data[i] = Mathf.Clamp((sub + lp * 0.75f) * env * 0.9f, -0.95f, 0.95f);
            }

            var clip = AudioClip.Create("AftershockImpact", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip SynthesizeAmbientWind(float duration)
        {
            int samples = Mathf.Max(1024, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            float lp1 = 0f;
            float lp2 = 0f;
            uint rng = 0xC001D00Du;

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;
                float u = i / (float)samples;

                rng ^= rng << 13;
                rng ^= rng >> 17;
                rng ^= rng << 5;
                float white = (rng & 0xFFFF) / 32767.5f - 1f;

                float mod = 0.015f + 0.010f * Mathf.Sin(2f * Mathf.PI * u);
                lp1 += mod * (white - lp1);
                lp2 += 0.025f * (lp1 - lp2);

                // Başlangıç ve bitişte pürüzsüz döngü geçişi (cross-fade envelope)
                float edge = Mathf.Min(Mathf.Clamp01(u * 12f), Mathf.Clamp01((1f - u) * 12f));
                float hum = Mathf.Sin(2f * Mathf.PI * 65f * t) * 0.08f;
                data[i] = (lp2 * 1.8f + hum) * edge;
            }

            var clip = AudioClip.Create("AmbientWind", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeClip(string name, float duration, Func<float, float, float> generator)
        {
            int samples = Mathf.Max(128, Mathf.RoundToInt(duration * SampleRate));
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;
                float u = i / (float)samples;
                data[i] = Mathf.Clamp(generator(t, u), -0.95f, 0.95f);
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Sin(float t, float freq) => Mathf.Sin(2f * Mathf.PI * freq * t);

        static float Env(float u, float attack, float decay)
        {
            float a = attack > 0.0001f ? Mathf.Clamp01(u / attack) : 1f;
            return a * Mathf.Exp(-u * decay);
        }

        static float Pulse(float t, float start, float length)
        {
            if (t < start || t > start + length) return 0f;
            float u = (t - start) / length;
            return Mathf.Sin(u * Mathf.PI) * Mathf.Exp(-u * 2.5f);
        }

        static float Noise(float seedTime)
        {
            float x = Mathf.Sin(seedTime * 12789.3f) * 43758.5453f;
            return (x - Mathf.Floor(x)) * 2f - 1f;
        }
    }
}
