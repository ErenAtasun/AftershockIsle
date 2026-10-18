using System.Collections;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.Endgame
{
    public readonly struct HelicopterInbound { }

    /// <summary>
    /// Sinyal tamamlanınca tahliye helikopteri çatıya iner ve oyun zaferle biter. Sonsuz modda tahliye yoktur: helikopter
    /// çatıya erzak bırakıp gider, telsiz birkaç gün sonra yeniden yardım çağırabilir.
    /// </summary>
    public class EvacuationSystem : MonoBehaviour
    {
        public const int SupplyDropCooldownDays = 3;

        [SerializeField] GameSession session;
        [SerializeField] Transform helicopter;
        [SerializeField] Transform rotor;
        [SerializeField] Transform landingPoint;
        [SerializeField] float approachHeight = 14f;
        [SerializeField] float approachDuration = 6f;

        public bool IsEvacuating { get; private set; }

        /// <summary>Sonsuz modda telsizin yeniden yardım çağırabileceği gün (kayıtta saklanır).</summary>
        public static int NextDropDay { get; set; }

        void Awake()
        {
            if (session == null) session = FindFirstObjectByType<GameSession>();
            if (helicopter != null) helicopter.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            EventBus.Subscribe<SignalCompleted>(OnSignalCompleted);
            EventBus.Subscribe<DayStarted>(OnDayStarted);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<SignalCompleted>(OnSignalCompleted);
            EventBus.Unsubscribe<DayStarted>(OnDayStarted);
        }

        // Sonsuz mod: bekleme bitince telsiz yeniden sinyal gönderebilir.
        void OnDayStarted(DayStarted evt)
        {
            var radio = RadioStation.Instance;
            if (!GameMode.IsEndless || radio == null || !radio.SignalComplete || evt.Day < NextDropDay || IsEvacuating) return;
            radio.RestoreState(true, 0f, false);
            EventBus.Publish(new NarrativeNotice("Telsiz yeniden hazır: koşullar sağlanırsa yeni bir erzak yardımı çağırabilirsin."));
        }

        void OnSignalCompleted(SignalCompleted evt)
        {
            if (IsEvacuating) return;
            IsEvacuating = true;
            EventBus.Publish(new HelicopterInbound());
            StartCoroutine(GameMode.IsEndless ? SupplyDrop() : Approach());
        }

        IEnumerator Approach()
        {
            if (helicopter != null && landingPoint != null)
            {
                Vector3 target = landingPoint.position;
                helicopter.gameObject.SetActive(true);
                yield return Fly(target + new Vector3(-12f, approachHeight, 0f), target, approachDuration);
            }

            int saved = Survivor.AliveCount;
            if (StructuralIntegrity.Instance != null && StructuralIntegrity.Instance.IsCollapsed) yield break;
            if (session != null) session.End(GameOutcome.Victory, $"Tahliye helikopteri çatıya indi. {saved} kazazede kurtarıldı!");
        }

        IEnumerator SupplyDrop()
        {
            var cycle = FindFirstObjectByType<DayCycle>();
            NextDropDay = (cycle != null ? cycle.Day : 1) + SupplyDropCooldownDays;

            Vector3 target = landingPoint != null ? landingPoint.position : Vector3.zero;
            Vector3 hover = target + Vector3.up * 2.5f;
            if (helicopter != null && landingPoint != null)
            {
                helicopter.gameObject.SetActive(true);
                yield return Fly(target + new Vector3(-12f, approachHeight, 0f), hover, approachDuration);
                for (float t = 0f; t < 1.5f; t += Time.deltaTime)
                {
                    if (rotor != null) rotor.Rotate(0f, 1080f * Time.deltaTime, 0f, Space.Self);
                    yield return null;
                }
            }

            var storage = ResourceStorage.Instance;
            if (storage != null)
            {
                storage.Add(new ResourceAmount(ResourceType.Supplies, 4));
                storage.Add(new ResourceAmount(ResourceType.Metal, 2));
                storage.Add(new ResourceAmount(ResourceType.Electronics, 1));
            }
            EventBus.Publish(new NarrativeNotice(
                $"Yardım helikopteri çatıya erzak bıraktı: +4 Yaşam Kiti, +2 Metal, +1 Elektronik. Telsiz Gün {NextDropDay}'de yeniden çağırabilir."));

            if (helicopter != null && landingPoint != null)
            {
                yield return Fly(hover, target + new Vector3(14f, approachHeight + 4f, 0f), approachDuration * 0.8f);
                helicopter.gameObject.SetActive(false);
            }
            IsEvacuating = false;
        }

        IEnumerator Fly(Vector3 from, Vector3 to, float duration)
        {
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                helicopter.position = Vector3.Lerp(from, to, k);
                if (rotor != null) rotor.Rotate(0f, 1080f * Time.deltaTime, 0f, Space.Self);
                yield return null;
            }
            helicopter.position = to;
        }
    }
}
