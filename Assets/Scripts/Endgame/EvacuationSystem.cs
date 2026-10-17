using System.Collections;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Endgame
{
    public readonly struct HelicopterInbound { }

    /// <summary>Sinyal tamamlanınca tahliye helikopteri çatıya iner ve oyun zaferle biter.</summary>
    public class EvacuationSystem : MonoBehaviour
    {
        [SerializeField] GameSession session;
        [SerializeField] Transform helicopter;
        [SerializeField] Transform rotor;
        [SerializeField] Transform landingPoint;
        [SerializeField] float approachHeight = 14f;
        [SerializeField] float approachDuration = 6f;

        public bool IsEvacuating { get; private set; }

        void Awake()
        {
            if (session == null) session = FindFirstObjectByType<GameSession>();
            if (helicopter != null) helicopter.gameObject.SetActive(false);
        }

        void OnEnable() => EventBus.Subscribe<SignalCompleted>(OnSignalCompleted);
        void OnDisable() => EventBus.Unsubscribe<SignalCompleted>(OnSignalCompleted);

        void OnSignalCompleted(SignalCompleted evt)
        {
            if (IsEvacuating) return;
            IsEvacuating = true;
            EventBus.Publish(new HelicopterInbound());
            StartCoroutine(Approach());
        }

        IEnumerator Approach()
        {
            if (helicopter != null && landingPoint != null)
            {
                helicopter.gameObject.SetActive(true);
                Vector3 target = landingPoint.position;
                Vector3 start = target + new Vector3(-12f, approachHeight, 0f);

                for (float t = 0f; t < approachDuration; t += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / approachDuration);
                    helicopter.position = Vector3.Lerp(start, target, k);
                    if (rotor != null) rotor.Rotate(0f, 1080f * Time.deltaTime, 0f, Space.Self);
                    yield return null;
                }

                helicopter.position = target;
            }

            int saved = Survivor.AliveCount;
            if (StructuralIntegrity.Instance != null && StructuralIntegrity.Instance.IsCollapsed) yield break;
            session.End(GameOutcome.Victory, $"Tahliye helikopteri çatıya indi. {saved} kazazede kurtarıldı!");
        }
    }
}
