using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.Interaction
{
    /// <summary>
    /// Sandalye: karakter oturup dinlenir (stres azalır, biraz can dolar). Oturan karakter sandalyenin üstüne alçalır ve
    /// kameraya doğru bakar. Gerginleşen, boştaki karakterler (seçili olmayanlar) kendiliğinden en yakın boş sandalyeye
    /// oturur (SeatAutoRest). Sandalye yürüme yolunu kapatmaz.
    /// </summary>
    public class Seat : Interactable
    {
        public const float StressRelief = 18f;
        public const float HealAmount = 5f;

        static readonly List<Seat> all = new List<Seat>();
        public static IReadOnlyList<Seat> All => all;

        /// <summary>Oturan karakterin bakacağı yön (varsayılan: kameraya).</summary>
        public Vector3 FaceDirection { get; set; } = Vector3.back;

        public override string ActionLabel => "Otur ve dinlen";
        public override WorkCategory Category => WorkCategory.Operate;
        public override bool BlocksMovement => false;
        public override string StatusText => $"Oturup dinlen: -{StressRelief:0} stres, +{HealAmount:0} can.";

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        // Karakter sandalyenin tam üstünde durur.
        public override float GetWorkX(int fromFloor, float fromX) => transform.position.x;
        public override float GetWorkZ() => Building != null ? Building.ClampZ(transform.position.z) : transform.position.z;

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (worker.Condition.Stress <= 1f && worker.Health >= worker.MaxHealth - 1f)
            {
                reason = $"{worker.DisplayName} zaten dinç ve sakin.";
                return false;
            }
            reason = null;
            return true;
        }

        public override void Complete(Survivor worker)
        {
            float relief = StressRelief * (worker.HasTrait(TraitId.Optimist) ? 1.2f : 1f);
            worker.Condition.RelieveStress(relief);
            worker.Heal(HealAmount);
        }

        // ---------------------------------------------------------------- Sandalye ekleme

        /// <summary>Verilen nesneyi oturulabilir yapar (collider'ı yoksa modelin sınırlarından bir kutu ekler).</summary>
        public static Seat Make(GameObject target)
        {
            var seat = target.GetComponent<Seat>();
            if (seat != null) return seat;

            if (target.GetComponentInChildren<Collider>() == null)
            {
                var renderers = target.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                    var box = target.AddComponent<BoxCollider>();
                    box.center = target.transform.InverseTransformPoint(bounds.center);
                    box.size = bounds.size;
                }
            }

            seat = target.AddComponent<Seat>();
            seat.ConfigureRuntime("Sandalye", 7f);
            return seat;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // Sahne yeniden kurulmadan da çalışsın: eski sahnedeki süs sandalyeleri ("Sandalye", "Sandalye (Tezgah)") oturulabilir olur.
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindFirstObjectByType<BuildingLayout>() == null) return;

            foreach (var transform in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!transform.name.StartsWith("Sandalye") || transform.GetComponent<Interactable>() != null) continue;
                if (transform.GetComponentInParent<Interactable>() != null) continue;
                if (transform.GetComponentInChildren<Renderer>() == null) continue;
                Make(transform.gameObject);
            }

            if (FindFirstObjectByType<SeatAutoRest>() == null) new GameObject("[Dinlenme]").AddComponent<SeatAutoRest>();
        }
    }

    /// <summary>Boşta kalan, gerginleşen karakterleri en yakın boş sandalyeye oturtur (oyuncunun seçtiği karakter hariç).</summary>
    public class SeatAutoRest : MonoBehaviour
    {
        const float CheckInterval = 1.5f;
        const float IdleSeconds = 4f;
        const float StressThreshold = 30f;

        readonly Dictionary<Survivor, float> idleSince = new Dictionary<Survivor, float>();
        CommandController commands;
        float nextCheck;

        void Update()
        {
            if (Time.deltaTime <= 0f || Time.time < nextCheck) return;
            nextCheck = Time.time + CheckInterval;

            if (Seat.All.Count == 0) return;
            if (commands == null) commands = FindFirstObjectByType<CommandController>();
            var selected = commands != null ? commands.Selected : null;
            var guard = NightDefenseSystem.Guard;

            foreach (var survivor in Survivor.All)
            {
                if (survivor == null || survivor.IsDead || !survivor.CanAct || survivor == selected || survivor == guard) continue;

                bool idle = survivor.Tasks.State == TaskState.Idle && survivor.Tasks.QueuedCount == 0 &&
                            !survivor.Mover.IsMoving && !survivor.Mover.IsSteering;
                if (!idle)
                {
                    idleSince.Remove(survivor);
                    continue;
                }

                if (!idleSince.TryGetValue(survivor, out float since))
                {
                    idleSince[survivor] = Time.time;
                    continue;
                }

                bool needsRest = survivor.Condition.Stress >= StressThreshold || survivor.Health < survivor.MaxHealth * 0.6f;
                if (Time.time - since < IdleSeconds || !needsRest) continue;

                var seat = NearestFreeSeat(survivor);
                if (seat == null) continue;

                survivor.Tasks.Assign(seat, false);
                idleSince.Remove(survivor);
            }
        }

        static Seat NearestFreeSeat(Survivor survivor)
        {
            Seat best = null;
            float bestScore = float.MaxValue;
            int floor = survivor.Mover.CurrentFloor;
            foreach (var seat in Seat.All)
            {
                if (seat == null || !seat.isActiveAndEnabled || seat.ClaimedBy != null) continue;
                if (!seat.CanStart(survivor, out _)) continue;

                float score = Mathf.Abs(seat.Floor - floor) * 100f + Mathf.Abs(seat.transform.position.x - survivor.transform.position.x);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = seat;
                }
            }
            return best;
        }
    }
}
