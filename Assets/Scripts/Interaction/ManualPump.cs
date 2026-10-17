using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using UnityEngine;

namespace AftershockIsle.Interaction
{
    /// <summary>
    /// Manuel su tulumbası. Karakter her darbede biraz su boşaltır ve su bitene kadar kendiliğinden devam eder.
    /// Biri pompalarken oyuncu Q/E'ye (gamepad: LT/RT) dönüşümlü basarsa her doğru vuruş ekstra su çeker.
    /// </summary>
    public class ManualPump : Interactable
    {
        [Tooltip("Her darbede boşaltılan su (yüzde puanı)")]
        [SerializeField] float drainPerStroke = 2.5f;
        [Tooltip("Ritim mini-oyununda her doğru dönüşümlü basışın ekstra boşalttığı su")]
        [SerializeField] float rhythmBonus = 0.8f;

        bool lastWasLeft;
        bool hasLastKey;

        public override string ActionLabel => "Pompala";
        public override bool RequiresEmptyHands => true;
        public override bool RepeatAfterComplete => FloodSystem.Instance != null && FloodSystem.Instance.Level > 0.01f;
        public override string StatusText => IsBeingOperated
            ? $"{GameInput.Prompt(InputPrompt.Pump)} tuşlarına dönüşümlü bas: daha hızlı pompala!"
            : "Bodrumdaki suyu elle boşaltır.";

        /// <summary>Şu an biri tulumbada çalışıyor mu (mini-oyun açık mı).</summary>
        public bool IsBeingOperated
        {
            get
            {
                var worker = ClaimedBy;
                return worker != null && worker.Tasks.Current == this && worker.Tasks.State == TaskState.Working;
            }
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            bool hasWater = FloodSystem.Instance != null && FloodSystem.Instance.Level > 0.01f;
            reason = hasWater ? null : "Pompalanacak su yok.";
            return hasWater;
        }

        public override void Complete(Survivor worker) => FloodSystem.Instance.Drain(drainPerStroke);

        void Update()
        {
            if (Time.timeScale <= 0f || !IsBeingOperated) return;

            if (GameInput.PumpLeftPressed) Stroke(true);
            if (GameInput.PumpRightPressed) Stroke(false);
        }

        void Stroke(bool left)
        {
            // Aynı tuşa üst üste basmak ritmi bozar ve sayılmaz.
            if (hasLastKey && lastWasLeft == left) return;

            hasLastKey = true;
            lastWasLeft = left;
            FloodSystem.Instance.Drain(rhythmBonus);
        }
    }
}
