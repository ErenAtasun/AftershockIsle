using AftershockIsle.Economy;

namespace AftershockIsle.Crafting
{
    public enum RecipeId { UpgradeTier2, ElectricPump, BypassValve, UpgradeTier3, RadioModule, SteelBeam, LightningRod, AmmoPack, Pistol, Shotgun }

    public readonly struct RecipeInfo
    {
        public readonly RecipeId Id;
        /// <summary>Tarifin görüneceği/üretilebileceği en düşük tezgah kademesi.</summary>
        public readonly int RequiredTier;
        public readonly string Name;
        public readonly string Description;
        public readonly ResourceAmount[] Costs;
        public readonly float Duration;
        public readonly bool Repeatable;

        public RecipeInfo(RecipeId id, int requiredTier, string name, string description,
                          ResourceAmount[] costs, float duration, bool repeatable = false)
        {
            Id = id;
            RequiredTier = requiredTier;
            Name = name;
            Description = description;
            Costs = costs;
            Duration = duration;
            Repeatable = repeatable;
        }

        public bool IsUpgrade => Id == RecipeId.UpgradeTier2 || Id == RecipeId.UpgradeTier3;
    }

    /// <summary>GDD 7: tezgah kademeleri ve açtıkları afet çözümleri.</summary>
    public static class RecipeCatalog
    {
        static ResourceAmount R(ResourceType type, int amount) => new ResourceAmount(type, amount);

        public static readonly RecipeInfo[] All =
        {
            new RecipeInfo(RecipeId.UpgradeTier2, 1, "Kademe 2'ye Yükselt", "Mekanik & altyapı tarifleri açılır.",
                new[] { R(ResourceType.Scrap, 4), R(ResourceType.Metal, 2) }, 10f),

            new RecipeInfo(RecipeId.ElectricPump, 2, "Elektrikli Pompa", "Elektrik açıkken bodrumu kendiliğinden boşaltır. Çalıştıkça yıpranır.",
                new[] { R(ResourceType.Metal, 2), R(ResourceType.Scrap, 1) }, 8f),
            new RecipeInfo(RecipeId.BypassValve, 2, "Bypass Vanası", "Patlayan borunun suyunu bina dışına yönlendirir; bodrum artık dolmaz.",
                new[] { R(ResourceType.Metal, 1), R(ResourceType.Scrap, 1) }, 6f),

            new RecipeInfo(RecipeId.UpgradeTier3, 2, "Kademe 3'e Yükselt", "İletişim & kurtarma tarifleri açılır.",
                new[] { R(ResourceType.Electronics, 1), R(ResourceType.Metal, 2) }, 12f),

            new RecipeInfo(RecipeId.RadioModule, 3, "Telsiz Modülü", "Çatıdaki telsiz istasyonunun tamiri için gerekli.",
                new[] { R(ResourceType.Electronics, 2), R(ResourceType.Metal, 1) }, 10f),
            new RecipeInfo(RecipeId.SteelBeam, 3, "Çelik Kiriş", "Bir kolonu çelikle güçlendirir: sarsıntılardan etkilenmez. Çatı kolonları tahliye için şart.",
                new[] { R(ResourceType.Metal, 1), R(ResourceType.Scrap, 2) }, 6f, repeatable: true),
            new RecipeInfo(RecipeId.LightningRod, 3, "Paratoner", "Çatıya kurulur; sarsıntılar artık kıvılcımla yangın çıkarmaz.",
                new[] { R(ResourceType.Metal, 1), R(ResourceType.Electronics, 1) }, 6f),

            new RecipeInfo(RecipeId.AmmoPack, 1, "Mermi Paketi", "Dış sefer için +8 tabanca mermisi.",
                new[] { R(ResourceType.Scrap, 1), R(ResourceType.Metal, 1) }, 5f, repeatable: true),
            new RecipeInfo(RecipeId.Pistol, 2, "El Yapımı Tabanca", "Dış seferde sapandan güçlü ve uzun menzilli; ama gürültülü, yakındaki düşmanları uyandırır. Mermi harcar.",
                new[] { R(ResourceType.Metal, 2), R(ResourceType.Electronics, 1) }, 10f),
            new RecipeInfo(RecipeId.Shotgun, 2, "Hurda Pompalı", "Dış seferde yakın mesafede yıkıcı, düşmanı geri iter; İri Yağmacı'ya karşı en iyisi. Çok gürültülü, yavaş, atış başına 1 mermi.",
                new[] { R(ResourceType.Metal, 3), R(ResourceType.Scrap, 3) }, 12f),
        };

        public static RecipeInfo Get(RecipeId id) => All[(int)id];
    }
}
