using System.Collections.Generic;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Characters
{
    public enum TaskState { Idle, Moving, Working }

    public readonly struct TaskNotice
    {
        public readonly Survivor Survivor;
        public readonly string Message;

        public TaskNotice(Survivor survivor, string message)
        {
            Survivor = survivor;
            Message = message;
        }
    }

    /// <summary>
    /// Karakterin iş kuyruğu: sıradaki işe yürür, çalışır, tamamlar.
    /// Bir işin ön koşulu varsa (elde yük varken boş el isteyen iş → sandığa taşı,
    /// yangın → kova doldur) ön koşul araya girer; ön koşul başarısız olursa bağlı iş de düşer.
    /// Tekrarlı işler (moloz, tulumba, yangın) kendiliğinden devam eder.
    /// </summary>
    [RequireComponent(typeof(Survivor))]
    public class TaskRunner : MonoBehaviour
    {
        struct QueuedTask
        {
            public Interactable Target;
            /// <summary>True ise ardındaki iş için araya girmiştir; başarısız olursa o iş de düşer.</summary>
            public bool IsPrerequisite;
        }

        const int MaxResolveSteps = 16;

        [SerializeField] float arriveTolerance = 0.15f;

        readonly List<QueuedTask> queue = new List<QueuedTask>();
        Survivor survivor;
        CharacterMover mover;
        float progress;
        float workX;

        /// <summary>Yetenekler ve tükenmişlik bu çarpanı değiştirir.</summary>
        public float WorkSpeedMultiplier { get; set; } = 1f;
        public Interactable Current { get; private set; }
        public TaskState State { get; private set; }
        public int QueuedCount => queue.Count;
        public float Progress01 =>
            State == TaskState.Working && Current != null ? Mathf.Clamp01(progress / Current.WorkDuration) : 0f;

        void Awake()
        {
            survivor = GetComponent<Survivor>();
            mover = GetComponent<CharacterMover>();
        }

        public void Assign(Interactable target, bool append)
        {
            if (!append) CancelAll();
            if (target == Current || queue.Exists(q => q.Target == target)) return;
            queue.Add(new QueuedTask { Target = target });
        }

        public void CancelAll()
        {
            ReleaseCurrent();
            queue.Clear();
        }

        void Update()
        {
            if (Time.deltaTime <= 0f || !survivor.CanAct) return;

            // Üzerinde çalışılan nesne yok olmuş olabilir (ör. başka sebeple temizlenen moloz).
            if (State != TaskState.Idle && Current == null) State = TaskState.Idle;

            switch (State)
            {
                case TaskState.Idle: TryStartNext(); break;
                case TaskState.Moving: UpdateMoving(); break;
                case TaskState.Working: UpdateWorking(); break;
            }
        }

        void TryStartNext()
        {
            for (int step = 0; step < MaxResolveSteps && queue.Count > 0; step++)
            {
                var target = queue[0].Target;
                if (target == null)
                {
                    FailFront(null);
                    continue;
                }

                var prerequisite = ResolvePrerequisite(target);
                if (prerequisite != null)
                {
                    queue.Insert(0, new QueuedTask { Target = prerequisite, IsPrerequisite = true });
                    continue;
                }

                if (!target.AllowedInSmoke && FireSpot.IsSmokeBlocked(target.Floor))
                {
                    FailFront($"Duman yüzünden {mover.Building.GetFloorName(target.Floor)} katına girilemiyor.");
                    continue;
                }

                if (!target.CanStart(survivor, out string reason))
                {
                    FailFront(reason);
                    continue;
                }

                if (!target.TryClaim(survivor))
                {
                    FailFront($"{target.DisplayName} üzerinde başkası çalışıyor.");
                    continue;
                }

                Current = target;
                workX = mover.Building.ClampX(target.GetWorkX(mover.CurrentFloor, transform.position.x));
                if (!mover.MoveTo(target.Floor, workX, target.GetWorkZ()))
                {
                    ReleaseCurrent();
                    FailFront($"{target.DisplayName} noktasına ulaşılamıyor.");
                    continue;
                }

                queue.RemoveAt(0);
                State = TaskState.Moving;
                return;
            }
        }

        Interactable ResolvePrerequisite(Interactable target)
        {
            if (target.RequiresEmptyHands && survivor.IsCarrying)
            {
                // Depolanabilir yük en yakın sandığa gider; kova gibi depolanamayan yük yere boşaltılır.
                var chest = StorageChest.FindNearest(survivor);
                if (survivor.Carried.type.IsStorable() && chest != null && target != chest) return chest;
                if (!survivor.Carried.type.IsStorable() && survivor.TryTakeCarried(out var dumped))
                    Notify($"{dumped.type.ToDisplayName()} boşaltıldı.");
            }

            return target.RequiredBefore(survivor);
        }

        /// <summary>Öndeki işi düşürür; o iş bir ön koşulsa ona bağlı işleri de düşürür.</summary>
        void FailFront(string reason)
        {
            Notify(reason);

            var failed = queue[0];
            queue.RemoveAt(0);
            while (failed.IsPrerequisite && queue.Count > 0)
            {
                failed = queue[0];
                queue.RemoveAt(0);
            }
        }

        void UpdateMoving()
        {
            if (mover.IsMoving) return;

            bool arrived = mover.CurrentFloor == Current.Floor &&
                           Mathf.Abs(transform.position.x - workX) <= arriveTolerance;
            if (!arrived)
            {
                Notify($"{Current.DisplayName} noktasına ulaşılamadı.");
                ReleaseCurrent();
                return;
            }

            State = TaskState.Working;
            progress = 0f;
        }

        void UpdateWorking()
        {
            if (!Current.CanContinue(survivor))
            {
                ReleaseCurrent();
                return;
            }

            Vector3 toTarget = Current.transform.position - transform.position;
            toTarget.y = 0f;
            mover.FaceDirection(toTarget.sqrMagnitude > 0.09f ? toTarget : Vector3.forward);

            float traitSpeed = TraitCatalog.GetWorkSpeedMultiplier(survivor, Current.Category);
            progress += Time.deltaTime * WorkSpeedMultiplier * SkillTree.WorkSpeedBonus(Current.Category) * traitSpeed;
            if (progress < Current.WorkDuration) return;

            var done = Current;
            ReleaseCurrent();
            done.Complete(survivor);
            survivor.GetComponent<SurvivorChatter>()?.OnTaskCompleted(done.DisplayName);

            // Tekrarlı işin ön koşulları (taşıma, kova) sıradaki çözümlemede yeniden araya girer.
            var chest = StorageChest.FindNearest(survivor);
            if (done.RepeatAfterComplete)
                queue.Insert(0, new QueuedTask { Target = done });
            else if (survivor.IsCarrying && survivor.Carried.type.IsStorable() && chest != null && done != chest)
                queue.Insert(0, new QueuedTask { Target = chest });
        }

        void ReleaseCurrent()
        {
            if (Current != null) Current.Release(survivor);
            Current = null;
            State = TaskState.Idle;
            progress = 0f;
        }

        void Notify(string message)
        {
            if (!string.IsNullOrEmpty(message))
                EventBus.Publish(new TaskNotice(survivor, message));
        }
    }
}
