using System;
using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum OutdoorCategory { Ground, Curb, GroundBack, Car, Van, Pickup, Barrier, Sandbags, Dumpster, Building, Skyline, Prop, Debris }

    /// <summary>
    /// Dış mekan model kiti (Blender'da tasarlanıp Assets/Models/Outdoor'a aktarılan parçalar). Editör
    /// "Aftershock → Dış Mekan Kitini Kur" ile doldurulur; saha kurulurken FieldExpedition buradan çeker.
    /// Boş ya da yoksa saha eski kutu görünümüyle kurulur.
    /// </summary>
    [CreateAssetMenu(menuName = "Aftershock/Outdoor Kit", fileName = "OutdoorKit")]
    public class OutdoorKit : ScriptableObject
    {
        public const string ResourcePath = "OutdoorKit";

        [Serializable]
        public struct Entry
        {
            public string name;
            public OutdoorCategory category;
            public GameObject prefab;
            /// <summary>Prefab'ın dünya boyutu (metre): x uzunluk, y yükseklik, z derinlik.</summary>
            public Vector3 size;
            /// <summary>Binalarda: orijinden ön cephe düzlemine mesafe (cephe -Z yönünde, kameraya bakar).</summary>
            public float facade;
        }

        [Serializable]
        public struct MaterialEntry
        {
            public string name;
            public Material material;
        }

        public Entry[] entries = Array.Empty<Entry>();
        public MaterialEntry[] materials = Array.Empty<MaterialEntry>();

        /// <summary>Editör: kitin hangi kaynak dosya durumundan kurulduğu (değişince otomatik yeniden kurulur).</summary>
        [HideInInspector] public string sourceHash;

        static OutdoorKit cached;
        static bool searched;

        public static OutdoorKit Instance
        {
            get
            {
                if (cached == null && !searched)
                {
                    searched = true;
                    cached = Resources.Load<OutdoorKit>(ResourcePath);
                }
                return cached;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            cached = null;
            searched = false;
        }

        public bool HasContent => entries != null && entries.Length > 0;

        public bool Has(OutdoorCategory category)
        {
            if (entries == null) return false;
            foreach (var entry in entries)
                if (entry.category == category && entry.prefab != null) return true;
            return false;
        }

        readonly List<Entry> scratch = new List<Entry>();

        /// <summary>Kategorideki rastgele bir parça (isteğe bağlı ad önekiyle). Yoksa false.</summary>
        public bool TryPick(OutdoorCategory category, out Entry result, string namePrefix = null)
        {
            scratch.Clear();
            foreach (var entry in entries)
            {
                if (entry.category != category || entry.prefab == null) continue;
                if (!string.IsNullOrEmpty(namePrefix) && !entry.name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                scratch.Add(entry);
            }

            if (scratch.Count == 0)
            {
                result = default;
                return false;
            }

            result = scratch[UnityEngine.Random.Range(0, scratch.Count)];
            return true;
        }

        public bool TryGet(string name, out Entry result)
        {
            foreach (var entry in entries)
            {
                if (entry.prefab == null || !string.Equals(entry.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                result = entry;
                return true;
            }
            result = default;
            return false;
        }

        public Material GetMaterial(string name)
        {
            if (materials == null) return null;
            foreach (var entry in materials)
                if (entry.material != null && string.Equals(entry.name, name, StringComparison.OrdinalIgnoreCase)) return entry.material;
            return null;
        }
    }
}
