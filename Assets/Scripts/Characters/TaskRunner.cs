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

    /// <summary>Bir iş tamamlandı (ses ve geri bildirim sistemleri dinler).</summary>
    public readonly struct TaskFinished
    {
        public readonly Survivor Survivor;
        public readonly Interactable Target;

        public TaskFinished(Survivor survivor, Interactable target)
        {
            Survivor = survivor;
            Target = target;
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
            /// <summary>True ise karakter nesnenin yanında durduğu yerden çalışır (F ile verilen iş), çalışma noktasına yürümez.</summary>
            public bool InPlace;
        }

        // Yerinde çalışma için nesnenin kenarına en fazla bu kadar uzak olunabilir; uzaksa çalışma noktasına yürünür.
        const float InPlaceReach = 1.4f;

        const int MaxResolveSteps = 16;

        [SerializeField] float arriveTolerance = 0.15f;

        readonly List<QueuedTask> queue = new List<QueuedTask>();
        Survivor survivor;
        CharacterMover mover;
        float progress;
        float workX;
        bool workedInPlace;

        /// <summary>Yetenekler ve tükenmişlik bu çarpanı değiştirir.</summary>
        public float WorkSpeedMultiplier { get; set; } = 1f;
        public Interactable Current { get; private set; }
        public TaskState State { get; private set; }
        public int QueuedCount => queue.Count;
        /// <summary>
        /// Bir sonraki Assign'ı "durduğun yerden" yapar (doğrudan kontrolde F). CommandController HandleCommand'dan önce
        /// açar, sonra kapatır; tezgah gibi menü açan nesneler Assign çağırmazsa etkisi olmaz.
        /// </summary>
        public bool PreferInPlace { get; set; }
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
            queue.Add(new QueuedTask { Target = target, InPlace = PreferInPlace && !(target is Seat) });
        }

        public void CancelAll()
        {
            ReleaseCurrent();
            queue.Clear();
        }

        void Update()
        {
            if (Time.deltaTime <= 0f) return;

            if (!survivor.CanAct)
            {
                SuspendWhileDisabled();
                return;
            }

            // Üzerinde çalışılan nesne yok olmuş olabilir (ör. başka sebeple temizlenen moloz).
            if (State != TaskState.Idle && Current == null) State = TaskState.Idle;

            switch (State)
            {
                case TaskState.Idle: TryStartNext(); break;
                case TaskState.Moving: UpdateMoving(); break;
                case TaskState.Working: UpdateWorking(); break;
            }
        }

        // Enkaz altında kalan ya da paniğe kapılan karakter nesneyi tutmaya devam etmesin: iş sıranın başına döner,
        // başkası o nesneyle çalışabilir; karakter kendine gelince işe yeniden başlar (ilerleme sıfırlanır).
        void SuspendWhileDisabled()
        {
            if (State == TaskState.Idle || Current == null) return;

            var interrupted = Current;
            ReleaseCurrent();
            mover.Halt();
            if (!survivor.IsDead && !queue.Exists(q => q.Target == interrupted))
                queue.Insert(0, new QueuedTask { Target = interrupted });
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

                // F ile verilen iş: yanındaysa olduğu yerden başlar (nesnenin üstüne/içine yürümez).
                if (queue[0].InPlace && CanWorkInPlace(target))
                {
                    queue.RemoveAt(0);
                    mover.Halt();
                    workX = transform.position.x;
                    State = TaskState.Working;
                    progress = 0f;
                    workedInPlace = true;
                    return;
                }
                workedInPlace = false;

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

        bool CanWorkInPlace(Interactable target)
        {
            if (mover.IsClimbing || mover.CurrentFloor != target.Floor) return false;
            var here = transform.position;
            if (!target.TryGetFootprint(out var bounds))
                return Vector2.Distance(new Vector2(here.x, here.z), new Vector2(target.transform.position.x, target.transform.position.z)) <= InPlaceReach;
            float dx = Mathf.Max(0f, Mathf.Max(bounds.min.x - here.x, here.x - bounds.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(bounds.min.z - here.z, here.z - bounds.max.z));
            return dx * dx + dz * dz <= InPlaceReach * InPlaceReach;
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
            if (Current is Seat seat) mover.FaceDirection(seat.FaceDirection);
            else mover.FaceDirection(toTarget.sqrMagnitude > 0.09f ? toTarget : Vector3.forward);

            float traitSpeed = TraitCatalog.GetWorkSpeedMultiplier(survivor, Current.Category);
            progress += Time.deltaTime * WorkSpeedMultiplier * SkillTree.WorkSpeedBonus(Current.Category) * traitSpeed *
                        AftershockIsle.Crafting.ToolKit.WorkSpeed(Current.Category);
            if (progress < Current.WorkDuration) return;

            var done = Current;
            ReleaseCurrent();
            done.Complete(survivor);
            EventBus.Publish(new TaskFinished(survivor, done));
            survivor.GetComponent<SurvivorChatter>()?.OnTaskCompleted(done.DisplayName);

            // Tekrarlı işin ön koşulları (taşıma, kova) sıradaki çözümlemede yeniden araya girer.
            var chest = StorageChest.FindNearest(survivor);
            // Yerinde başlanan tekrarlı iş (kazı) yine yerinde sürer.
            if (done.RepeatAfterComplete)
                queue.Insert(0, new QueuedTask { Target = done, InPlace = workedInPlace });
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
