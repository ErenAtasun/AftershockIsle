using System.Linq;
using AftershockIsle.Building;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.DebugTools
{
    /// <summary>
    /// Afet sistemleri gelene kadar test kısayolları. Yalnızca editörde ve development build'de çalışır.
    /// F9: bütünlük -10   F10: rastgele sağlam kolonu çatlat   F11: her kaynaktan +5
    /// </summary>
    public class DebugCheats : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void Update()
        {
            if (GameInput.DebugDamagePressed)
                StructuralIntegrity.Instance.ApplyDamage(10f, DamageSource.Debug);

            if (GameInput.DebugCrackPressed)
            {
                var intact = SupportColumn.All.Where(c => c.State == ColumnState.Intact).ToList();
                if (intact.Count > 0) intact[Random.Range(0, intact.Count)].SetState(ColumnState.Cracked);
            }

            if (GameInput.DebugResourcesPressed)
                foreach (var type in ResourceTypes.All)
                    ResourceStorage.Instance.Add(new ResourceAmount(type, 5));
        }
#endif
    }
}
