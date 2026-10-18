using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Expedition;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Açlığın gün içinde hissedilmesi (açlık barı gece yemekte değişir): öğle başında akşam için kit yetmeyecekse
    /// hatırlatma, aç kazazedelerin ara sıra yakınması ve açlık son kademedeyse "bu gece de yemezse can kaybeder" uyarısı.
    /// </summary>
    public class HungerWatch : MonoBehaviour
    {
        const float ChatterInterval = 35f;

        static readonly string[] hungryLines =
        {
            "Midem kazınıyor...", "Bir lokma bir şey olsa...", "Dizlerimin bağı çözülüyor.", "Açlıktan elim titriyor.",
            "Bu gece bir şey yiyebilecek miyiz?",
        };

        static readonly string[] starvingLines =
        {
            "Bu gece de yemezsem... dayanamam.", "Gözüm kararıyor, bir şey yemeliyim.", "Ayakta duracak halim kalmadı.",
        };

        float nextChatter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindFirstObjectByType<BuildingLayout>() == null || FindFirstObjectByType<HungerWatch>() != null) return;
            new GameObject("[Açlık Takibi]").AddComponent<HungerWatch>();
        }

        void OnEnable() => EventBus.Subscribe<DayPhaseChanged>(OnPhaseChanged);
        void OnDisable() => EventBus.Unsubscribe<DayPhaseChanged>(OnPhaseChanged);

        void OnPhaseChanged(DayPhaseChanged evt)
        {
            if (evt.Phase != DayPhase.Noon || AftershockIsle.Save.SaveManager.IsApplying) return;

            var storage = ResourceStorage.Instance;
            if (storage == null) return;
            int need = Survivor.AliveCount + ExpeditionSystem.AwayCount;
            int kits = storage.Get(ResourceType.Supplies);
            if (kits >= need) return;

            EventBus.Publish(new NarrativeNotice(
                $"Akşama {need - kits} Yaşam Kiti eksik ({kits}/{need}). Öğle seferi, dolap arama ya da enkaz kazısıyla kit bulmaya çalış."));
        }

        void Update()
        {
            if (Time.deltaTime <= 0f || Time.time < nextChatter) return;
            nextChatter = Time.time + ChatterInterval * Random.Range(0.7f, 1.3f);

            Survivor pick = null;
            int count = 0;
            foreach (var survivor in Survivor.All)
            {
                if (survivor == null || survivor.IsDead || survivor.Condition.Hunger <= 0) continue;
                // Rastgele bir aç kazazede seç (rezervuar örneklemesi).
                if (Random.Range(0, ++count) == 0) pick = survivor;
            }
            if (pick == null) return;

            var chatter = pick.GetComponent<SurvivorChatter>();
            if (chatter == null) return;

            bool starving = pick.Condition.Hunger >= SurvivorCondition.MaxHunger;
            var lines = starving ? starvingLines : hungryLines;
            chatter.SayIfQuiet(lines[Random.Range(0, lines.Length)], 3.5f, starving ? new Color(1f, 0.55f, 0.45f) : new Color(1f, 0.8f, 0.55f));
        }
    }
}
