using System.Collections.Generic;
using AftershockIsle.Core;
using AftershockIsle.Interaction;
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
    /// Elde yük varken boş el isteyen işten önce sandığa taşıma işi araya girer;
    /// tekrarlı işler (moloz) taşıma bitince kendiliğinden devam eder.
    /// </summary>
    [RequireComponent(typeof(Survivor))]
    public class TaskRunner : MonoBehaviour
    {
        [SerializeField] float arriveTolerance = 0.15f;

        readonly List<Interactable> queue = new List<Interactable>();
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
            if (target == Current || queue.Contains(target)) return;
            queue.Add(target);
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
            while (queue.Count > 0)
            {
                var next = queue[0];
                queue.RemoveAt(0);
                if (next == null) continue;

                var chest = StorageChest.Main;
                bool haulFirst = next.RequiresEmptyHands && survivor.IsCarrying && chest != null;
                if (haulFirst)
                {
                    queue.Insert(0, next);
                    next = chest;
                }

                if (!next.CanStart(survivor, out string reason))
                {
                    Fail(reason, haulFirst);
                    continue;
                }

                if (!next.TryClaim(survivor))
                {
                    Fail($"{next.DisplayName} üzerinde başkası çalışıyor.", haulFirst);
                    continue;
                }

                Current = next;
                workX = mover.Building.ClampX(next.GetWorkX(mover.CurrentFloor, transform.position.x));
                if (!mover.MoveTo(next.Floor, workX))
                {
                    ReleaseCurrent();
                    Fail($"{next.DisplayName} noktasına ulaşılamıyor.", haulFirst);
                    continue;
                }

                State = TaskState.Moving;
                return;
            }
        }

        /// <summary>Taşıma başarısızsa ardındaki boş el isteyen iş de yapılamaz; onu da düşür.</summary>
        void Fail(string reason, bool droppedHaul)
        {
            Notify(reason);
            if (droppedHaul && queue.Count > 0) queue.RemoveAt(0);
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
            mover.FaceDirection(Mathf.Abs(toTarget.x) > 0.3f ? toTarget : Vector3.forward);

            progress += Time.deltaTime * WorkSpeedMultiplier;
            if (progress < Current.WorkDuration) return;

            var done = Current;
            ReleaseCurrent();
            done.Complete(survivor);

            bool repeat = done != null && done.RepeatAfterComplete;
            var chest = StorageChest.Main;
            if (survivor.IsCarrying && chest != null && done != chest)
            {
                queue.Insert(0, chest);
                if (repeat) queue.Insert(1, done);
            }
            else if (repeat)
            {
                queue.Insert(0, done);
            }
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
