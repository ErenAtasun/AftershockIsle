using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Expedition;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.Core
{
    public enum GameOutcome { None, Victory, Defeat }

    public readonly struct GameEnded
    {
        public readonly GameOutcome Outcome;
        public readonly string Reason;

        public GameEnded(GameOutcome outcome, string reason)
        {
            Outcome = outcome;
            Reason = reason;
        }
    }

    /// <summary>Zafer/yenilgi koşullarını dinler, oyunu dondurur ve R ile yeniden başlatır.</summary>
    public class GameSession : MonoBehaviour
    {
        [SerializeField] TimeManager timeManager;

        public GameOutcome Outcome { get; private set; }
        public string EndReason { get; private set; }

        void Awake()
        {
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
        }

        void OnEnable()
        {
            EventBus.Subscribe<BuildingCollapsed>(OnBuildingCollapsed);
            EventBus.Subscribe<SurvivorDied>(OnSurvivorDied);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<BuildingCollapsed>(OnBuildingCollapsed);
            EventBus.Unsubscribe<SurvivorDied>(OnSurvivorDied);
        }

        void OnSurvivorDied(SurvivorDied evt)
        {
            if (Survivor.AliveCount + ExpeditionSystem.AwayCount == 0)
                End(GameOutcome.Defeat, "Sığınaktaki tüm kazazedeler öldü.");
        }

        void Update()
        {
            if (Outcome != GameOutcome.None && GameInput.RestartPressed)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void OnBuildingCollapsed(BuildingCollapsed evt) => End(GameOutcome.Defeat, evt.Source switch
        {
            DamageSource.Flood => "Bina su baskınına dayanamadı ve temelden kaydı.",
            DamageSource.Fire => "Yangın taşıyıcı kirişleri yaktı, bina çöktü.",
            DamageSource.Aftershock => "Artçı sarsıntı binayı yıktı.",
            _ => "Yapısal bütünlük tükendi, bina çöktü.",
        });

        public void End(GameOutcome outcome, string reason)
        {
            if (Outcome != GameOutcome.None) return;

            Outcome = outcome;
            EndReason = reason;
            timeManager.enabled = false;
            Time.timeScale = 0f;
            EventBus.Publish(new GameEnded(outcome, reason));
        }
    }
}
