using System;
using UnityEngine;

namespace AftershockIsle.Economy
{
    /// <summary>
    /// Elde taşınabilen her şey. İlk dördü depolanabilir kaynaklardır;
    /// Water (dolu kova) sandığa konmaz, sadece yangın söndürmek için taşınır.
    /// </summary>
    public enum ResourceType { Scrap, Metal, Electronics, Supplies, Water }

    [Serializable]
    public struct ResourceAmount
    {
        public ResourceType type;
        [Min(1)] public int amount;

        public ResourceAmount(ResourceType type, int amount)
        {
            this.type = type;
            this.amount = amount;
        }

        public override string ToString() => $"{amount} {type.ToDisplayName()}";
    }

    public static class ResourceTypes
    {
        /// <summary>Depoda tutulan kaynaklar (sayaç ve arayüz sırası).</summary>
        public static readonly ResourceType[] All =
            { ResourceType.Scrap, ResourceType.Metal, ResourceType.Electronics, ResourceType.Supplies };

        public static int Count => All.Length;

        public static bool IsStorable(this ResourceType type) => type != ResourceType.Water;

        public static string ToDisplayName(this ResourceType type) => type switch
        {
            ResourceType.Scrap => "Kalas & Hurda",
            ResourceType.Metal => "Boru & Metal",
            ResourceType.Electronics => "Elektronik & Akü",
            ResourceType.Supplies => "Yaşam Kiti",
            _ => "Su Kovası",
        };

        /// <summary>Ağır parçalar taşınırken karakter yavaşlar.</summary>
        public static bool IsHeavy(this ResourceType type) =>
            type == ResourceType.Scrap || type == ResourceType.Metal || type == ResourceType.Water;

        public static Color ToColor(this ResourceType type) => type switch
        {
            ResourceType.Scrap => new Color(0.6f, 0.4f, 0.2f),
            ResourceType.Metal => new Color(0.55f, 0.6f, 0.68f),
            ResourceType.Electronics => new Color(0.95f, 0.8f, 0.15f),
            ResourceType.Supplies => new Color(0.85f, 0.2f, 0.2f),
            _ => new Color(0.25f, 0.5f, 0.95f),
        };
    }
}
