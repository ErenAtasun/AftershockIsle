using System.Linq;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.DebugTools
{
    /// <summary>
    /// Test eylemleri; F1 test panelindeki butonlarla tetiklenir. Yalnızca editörde ve development build'de çalışır.
    /// Gaz kaçağı, yangın, boru patlat, artçı sarsıntı, bütünlük -10, rastgele sağlam kolonu çatlat,
    /// her kaynaktan +5 ve +1 yetenek puanı, eksik dalın uzmanı bir test kazazedesi ekle.
    /// </summary>
    public class DebugCheats : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void Update()
        {
            if (GameInput.DebugFirePressed)
                DisasterDirector.Fire(DisasterType.Fire);

            if (GameInput.DebugFloodPressed)
                DisasterDirector.Fire(DisasterType.PipeBurst);

            if (GameInput.DebugGasPressed)
                DisasterDirector.Fire(DisasterType.GasLeak);

            if (GameInput.DebugQuakePressed)
                DisasterDirector.Fire(DisasterType.Aftershock);

            if (GameInput.DebugDamagePressed)
                StructuralIntegrity.Instance.ApplyDamage(10f, DamageSource.Debug);

            if (GameInput.DebugCrackPressed)
            {
                // Kırık olmayan rastgele bir kolonu bir seviye düşürür.
                var standing = SupportColumn.All.Where(c => c.State != ColumnState.Broken).ToList();
                if (standing.Count > 0)
                {
                    var column = standing[Random.Range(0, standing.Count)];
                    column.SetState(column.State == ColumnState.Reinforced ? ColumnState.Cracked : ColumnState.Broken);
                }
            }

            if (GameInput.DebugResourcesPressed)
            {
                foreach (var type in ResourceTypes.All)
                    ResourceStorage.Instance.Add(new ResourceAmount(type, 5));
                SkillTree.Instance.AddPoints(1);
            }

            if (GameInput.DebugSpawnSurvivorPressed) SpawnTestSurvivor();
        }

        static void SpawnTestSurvivor()
        {
            var roster = SurvivorRoster.Instance;
            var branch = SurvivorRoster.FirstMissingBranch();
            if (roster == null || roster.IsFull || branch == null) return;

            roster.Spawn($"Kazazede ({branch.Value.ToDisplayName()})", branch.Value, 2, Random.Range(-1.5f, 1.5f));
        }
#endif
    }
}
