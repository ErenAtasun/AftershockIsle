using System;
using UnityEngine;

namespace AftershockIsle.Economy
{
    public enum ResourceType { Scrap, Metal, Electronics, Supplies }

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
        public static readonly ResourceType[] All =
            { ResourceType.Scrap, ResourceType.Metal, ResourceType.Electronics, ResourceType.Supplies };

        public static int Count => All.Length;

        public static string ToDisplayName(this ResourceType type) => type switch
        {
            ResourceType.Scrap => "Kalas & Hurda",
            ResourceType.Metal => "Boru & Metal",
            ResourceType.Electronics => "Elektronik & Akü",
            _ => "Yaşam Kiti",
        };

        /// <summary>Ağır parçalar taşınırken karakter yavaşlar.</summary>
        public static bool IsHeavy(this ResourceType type) =>
            type == ResourceType.Scrap || type == ResourceType.Metal;

        public static Color ToColor(this ResourceType type) => type switch
        {
            ResourceType.Scrap => new Color(0.6f, 0.4f, 0.2f),
            ResourceType.Metal => new Color(0.55f, 0.6f, 0.68f),
            ResourceType.Electronics => new Color(0.95f, 0.8f, 0.15f),
            _ => new Color(0.85f, 0.2f, 0.2f),
        };
    }
}
