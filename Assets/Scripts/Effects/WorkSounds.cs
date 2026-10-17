using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Crafting;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Interaction;
using AftershockIsle.Player;
using UnityEngine;

namespace AftershockIsle.Effects
{
    /// <summary>
    /// Sığınaktaki iş sesleri: çalışan her kazazedenin işine göre ritmik ses (kazı, çekiç, arama, üretim, su, tulumba),
    /// iş bitince toplama sesi, seçili karakterin adım sesleri ve merdiven sesi. Ses, işin süresine yayılan darbeler olarak çalar;
    /// oyun hızına uyar (2x hızda sıklaşır) ve zaman durunca susar. Aynı anda çok kişi çalışınca ses birikmesin diye
    /// toplam darbe sayısı sınırlıdır.
    /// </summary>
    public class WorkSounds : MonoBehaviour
    {
        const float MinSpacing = 0.09f;
        const float WorkVolume = 0.55f;
        const float StepVolume = 0.32f;

        readonly Dictionary<Survivor, float> nextWork = new Dictionary<Survivor, float>();
        readonly Dictionary<Survivor, float> nextStep = new Dictionary<Survivor, float>();
        readonly List<Survivor> stale = new List<Survivor>();
        CommandController commands;
        float lastPlayed;

        void OnEnable() => EventBus.Subscribe<TaskFinished>(OnTaskFinished);
        void OnDisable() => EventBus.Unsubscribe<TaskFinished>(OnTaskFinished);

        void OnTaskFinished(TaskFinished evt)
        {
            if (evt.Target is RubblePile || evt.Target is Furniture || evt.Target is WaterSource)
                SoundManager.Play(SoundId.Pickup, 0.7f, 0.04f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (commands == null) commands = FindFirstObjectByType<CommandController>();
            var selected = commands != null ? commands.Selected : null;

            Prune(nextWork);
            Prune(nextStep);

            foreach (var survivor in Survivor.All)
            {
                if (survivor == null || survivor.IsDead || !survivor.isActiveAndEnabled) continue;

                var tasks = survivor.Tasks;
                if (tasks != null && tasks.State == TaskState.Working && tasks.Current != null)
                    TickWork(survivor, tasks.Current);

                if (survivor == selected) TickMovement(survivor);
            }
        }

        void TickWork(Survivor survivor, Interactable target)
        {
            if (!WorkSoundFor(target, out var id, out float interval)) return;

            nextWork.TryGetValue(survivor, out float next);
            if (Time.time < next) return;

            // İlk darbe biraz gecikmeli gelsin (karakter aleti kaldırır), sonra düzenli sürsün.
            nextWork[survivor] = Time.time + interval * Random.Range(0.92f, 1.08f);
            if (next <= 0f) return;

            Strike(id, WorkVolume);
        }

        void TickMovement(Survivor survivor)
        {
            var mover = survivor.Mover;
            if (mover == null || mover.Frozen) return;

            bool climbing = mover.IsClimbing;
            bool walking = mover.IsMoving || mover.IsSteering;
            if (!walking) return;

            nextStep.TryGetValue(survivor, out float next);
            if (Time.time < next) return;

            float speed = Mathf.Max(0.4f, mover.SpeedMultiplier);
            float interval = (climbing ? 0.5f : 0.42f) / speed;
            nextStep[survivor] = Time.time + interval;
            if (next <= 0f) return;

            Strike(climbing ? SoundId.Ladder : SoundId.Footstep, climbing ? 0.45f : StepVolume);
        }

        void Strike(SoundId id, float volume)
        {
            if (Time.unscaledTime - lastPlayed < MinSpacing) return;
            lastPlayed = Time.unscaledTime;
            SoundManager.Play(id, volume, 0.08f);
        }

        /// <summary>İşe uygun ses ve darbe aralığı (saniye). Sessiz işler false döner.</summary>
        static bool WorkSoundFor(Interactable target, out SoundId id, out float interval)
        {
            id = SoundId.Hammer;
            interval = 0.55f;

            switch (target)
            {
                case RubblePile _:
                    id = SoundId.Dig;
                    interval = 0.72f;
                    return true;
                case Workbench _:
                    id = SoundId.CraftTap;
                    interval = 0.6f;
                    return true;
                case ManualPump _:
                    id = SoundId.PumpStroke;
                    interval = 0.6f;
                    return true;
                case WaterSource _:
                    id = SoundId.WaterFill;
                    interval = 0.65f;
                    return true;
                case FireSpot _:
                    id = SoundId.WaterFill;
                    interval = 0.5f;
                    return true;
                case StorageChest _:
                    return false;
            }

            switch (target.Category)
            {
                case WorkCategory.Repair:
                case WorkCategory.Dismantle:
                    id = SoundId.Hammer;
                    interval = 0.55f;
                    return true;
                case WorkCategory.Dig:
                    id = SoundId.Dig;
                    interval = 0.72f;
                    return true;
                case WorkCategory.Search:
                    id = SoundId.Search;
                    interval = 0.8f;
                    return true;
                default:
                    return false;
            }
        }

        // Yok olan kazazedeler sözlükte birikmesin.
        void Prune(Dictionary<Survivor, float> map)
        {
            if (map.Count == 0) return;
            stale.Clear();
            foreach (var key in map.Keys)
                if (key == null) stale.Add(key);
            foreach (var key in stale) map.Remove(key);
        }
    }
}
