using AftershockIsle.Economy;

namespace AftershockIsle.Crafting
{
    public enum RecipeId
    {
        UpgradeTier2 = 0,
        ElectricPump = 1,
        BypassValve = 2,
        UpgradeTier3 = 3,
        RadioModule = 4,
        SteelBeam = 5,
        LightningRod = 6,
        AmmoPack = 7,
        Pistol = 8,
        Shotgun = 9,
        ComfortCorner = 10,
        ShelterHeater = 11,
        StorageRack = 12,
        GasMask = 13,
        WaterPurifier = 14,
        MedStation = 15,
        FireSuppressor = 16,
        ArmorVest = 17,
        BackupGenerator = 18,
        Smg = 19,
        AssaultRifle = 20,
        SniperRifle = 21,
        ReinforcedDoor = 22
    }

    public readonly struct RecipeInfo
    {
        public const int FloorBasement = 1 << 0;
        public const int FloorGround = 1 << 1;
        public const int FloorLiving = 1 << 2;
        public const int FloorRoof = 1 << 3;
        public const int FloorIndoors = FloorBasement | FloorGround | FloorLiving;
        public const int FloorAll = FloorIndoors | FloorRoof;

        public readonly RecipeId Id;
        /// <summary>Tarifin görüneceği/üretilebileceği en düşük tezgah kademesi.</summary>
        public readonly int RequiredTier;
        public readonly string Name;
        public readonly string Description;
        public readonly ResourceAmount[] Costs;
        public readonly float Duration;
        public readonly bool Repeatable;
        public readonly bool IsPlaceable;
        public readonly int GridWidth;
        public readonly int GridDepth;
        public readonly int AllowedFloorMask;

        public RecipeInfo(RecipeId id, int requiredTier, string name, string description,
                          ResourceAmount[] costs, float duration, bool repeatable = false,
                          bool isPlaceable = false, int gridWidth = 1, int gridDepth = 1, int allowedFloorMask = FloorAll)
        {
            Id = id;
            RequiredTier = requiredTier;
            Name = name;
            Description = description;
            Costs = costs;
            Duration = duration;
            Repeatable = repeatable;
            IsPlaceable = isPlaceable;
            GridWidth = gridWidth;
            GridDepth = gridDepth;
            AllowedFloorMask = allowedFloorMask;
        }

        public bool IsUpgrade => Id == RecipeId.UpgradeTier2 || Id == RecipeId.UpgradeTier3;

        public bool AllowsFloor(int floor) => (AllowedFloorMask & (1 << floor)) != 0;

        public string AllowedFloorsText()
        {
            if (AllowedFloorMask == FloorAll) return "Tüm Katlar";
            if (AllowedFloorMask == FloorIndoors) return "İç Mekan (Kat 0-2)";
            if (AllowedFloorMask == FloorBasement) return "Sadece Bodrum";
            if (AllowedFloorMask == FloorRoof) return "Sadece Çatı";
            return "Belirli Katlar";
        }
    }

    /// <summary>GDD 7: tezgah kademeleri, yerleşik ev eşyaları (Grid) ve afet/ekipman çözümleri.</summary>
    public static class RecipeCatalog
    {
        static ResourceAmount R(ResourceType type, int amount) => new ResourceAmount(type, amount);

        public static readonly RecipeInfo[] All =
        {
            // 0: UpgradeTier2
            new RecipeInfo(RecipeId.UpgradeTier2, 1, "Kademe 2'ye Yükselt", "Mekanik, revir, su arıtma ve altyapı tarifleri açılır.",
                new[] { R(ResourceType.Scrap, 4), R(ResourceType.Metal, 2) }, 10f),

            // 1: ElectricPump (Grid Placeable - Bodrum)
            new RecipeInfo(RecipeId.ElectricPump, 2, "Elektrikli Pompa", "Bodruma (Grid ile) yerleştirilir. Elektrik açıkken bodrumu kendiliğinden boşaltır.",
                new[] { R(ResourceType.Metal, 2), R(ResourceType.Scrap, 1) }, 8f,
                repeatable: false, isPlaceable: true, gridWidth: 1, gridDepth: 1, allowedFloorMask: RecipeInfo.FloorBasement),

            // 2: BypassValve
            new RecipeInfo(RecipeId.BypassValve, 2, "Bypass Vanası", "Patlayan borunun suyunu bina dışına yönlendirir; bodrum artık dolmaz.",
                new[] { R(ResourceType.Metal, 1), R(ResourceType.Scrap, 1) }, 6f),

            // 3: UpgradeTier3
            new RecipeInfo(RecipeId.UpgradeTier3, 2, "Kademe 3'e Yükselt", "İletişim, yedek jeneratör & kurtarma tarifleri açılır.",
                new[] { R(ResourceType.Electronics, 1), R(ResourceType.Metal, 2) }, 12f),

            // 4: RadioModule
            new RecipeInfo(RecipeId.RadioModule, 3, "Telsiz Modülü", "Çatıdaki telsiz istasyonunun tamiri için gerekli.",
                new[] { R(ResourceType.Electronics, 2), R(ResourceType.Metal, 1) }, 10f),

            // 5: SteelBeam
            new RecipeInfo(RecipeId.SteelBeam, 3, "Çelik Kiriş", "Bir kolonu çelikle güçlendirir: sarsıntılardan etkilenmez. Çatı kolonları tahliye için şart.",
                new[] { R(ResourceType.Metal, 1), R(ResourceType.Scrap, 2) }, 6f, repeatable: true),

            // 6: LightningRod (Grid Placeable - Çatı)
            new RecipeInfo(RecipeId.LightningRod, 3, "Paratoner Kulesi", "Çatıya (Grid ile) yerleştirilir; sarsıntılar artık kıvılcımla yangın çıkarmaz.",
                new[] { R(ResourceType.Metal, 1), R(ResourceType.Electronics, 1) }, 6f,
                repeatable: false, isPlaceable: true, gridWidth: 1, gridDepth: 1, allowedFloorMask: RecipeInfo.FloorRoof),

            // 7: AmmoPack
            new RecipeInfo(RecipeId.AmmoPack, 1, "Mermi Paketi", "Dış sefer için +8 tabanca mermisi.",
                new[] { R(ResourceType.Scrap, 1), R(ResourceType.Metal, 1) }, 5f, repeatable: true),

            // 8: Pistol
            new RecipeInfo(RecipeId.Pistol, 2, "El Yapımı Tabanca", "Dış seferde sapandan güçlü ve uzun menzilli; mermi harcar.",
                new[] { R(ResourceType.Metal, 2), R(ResourceType.Electronics, 1) }, 10f),

            // 9: Shotgun
            new RecipeInfo(RecipeId.Shotgun, 2, "Hurda Pompalı", "Dış seferde yakın mesafede yıkıcı, düşmanı geri iter; İri Yağmacı'ya karşı en iyisi.",
                new[] { R(ResourceType.Metal, 3), R(ResourceType.Scrap, 3) }, 12f),

            // 10: ComfortCorner (Grid Placeable 2x1 - İç Mekan)
            new RecipeInfo(RecipeId.ComfortCorner, 1, "Dinlenme Köşesi", "Eve yerleştirilir (2x1). Kattaki boşta kazazedelerin stresini 2.5x hızlı düşürür; sağ tıkla dinlenerek -25 stres düşürür.",
                new[] { R(ResourceType.Scrap, 2) }, 6f,
                repeatable: true, isPlaceable: true, gridWidth: 2, gridDepth: 1, allowedFloorMask: RecipeInfo.FloorIndoors),

            // 11: ShelterHeater (Grid Placeable 1x1 - İç Mekan)
            new RecipeInfo(RecipeId.ShelterHeater, 1, "Sığınak Sobası", "Eve yerleştirilir (1x1). Bulunduğu katı aydınlatır ve ısıtır; elektrik kesikken karanlıkta çalışma stresini önler.",
                new[] { R(ResourceType.Scrap, 2), R(ResourceType.Metal, 1) }, 6f,
                repeatable: true, isPlaceable: true, gridWidth: 1, gridDepth: 1, allowedFloorMask: RecipeInfo.FloorIndoors),

            // 12: StorageRack (Grid Placeable 2x1 - Tüm Katlar)
            new RecipeInfo(RecipeId.StorageRack, 1, "Ek Depo Rafı", "Eve yerleştirilir (2x1). Bulunduğu katta ortak sandık görevi görür; kazazedeler malzemeyi üst kata taşımaz.",
                new[] { R(ResourceType.Scrap, 3) }, 5f,
                repeatable: true, isPlaceable: true, gridWidth: 2, gridDepth: 1, allowedFloorMask: RecipeInfo.FloorAll),

            // 13: GasMask (Ekipman)
            new RecipeInfo(RecipeId.GasMask, 1, "Gaz & Duman Maskesi", "Ekipman: Kazazedelerin duman ve gazdan aldığı hasarı %75 azaltır, dumanlı katta çalışabilmelerini sağlar.",
                new[] { R(ResourceType.Metal, 1), R(ResourceType.Scrap, 1) }, 6f),

            // 14: WaterPurifier (Grid Placeable 1x1 - Tüm Katlar)
            new RecipeInfo(RecipeId.WaterPurifier, 2, "Su Arıtma İstasyonu", "Eve yerleştirilir (1x1). Her sabah 4 kova su doldurur ve her 2 günde bir depoya +1 Yaşam Kiti üretir.",
                new[] { R(ResourceType.Metal, 2), R(ResourceType.Scrap, 2) }, 8f,
                repeatable: true, isPlaceable: true, gridWidth: 1, gridDepth: 1, allowedFloorMask: RecipeInfo.FloorAll),

            // 15: MedStation (Grid Placeable 2x1 - İç Mekan)
            new RecipeInfo(RecipeId.MedStation, 2, "Tıbbi Revir Masası", "Eve yerleştirilir (2x1). Sağ tıkla 1 Yaşam Kiti harcayarak +55 HP iyileştirir ve -25 stres düşürür.",
                new[] { R(ResourceType.Scrap, 2), R(ResourceType.Metal, 1), R(ResourceType.Supplies, 1) }, 8f,
                repeatable: false, isPlaceable: true, gridWidth: 2, gridDepth: 1, allowedFloorMask: RecipeInfo.FloorIndoors),

            // 16: FireSuppressor (Grid Placeable 1x1 - İç Mekan)
            new RecipeInfo(RecipeId.FireSuppressor, 2, "Yangın Söndürme Tüpü", "Eve yerleştirilir (1x1). Kattaki yangın büyümesini %50 yavaşlatır; kovasız doğrudan yangın söndürme sağlar.",
                new[] { R(ResourceType.Metal, 2), R(ResourceType.Scrap, 1) }, 6f,
                repeatable: true, isPlaceable: true, gridWidth: 1, gridDepth: 1, allowedFloorMask: RecipeInfo.FloorIndoors),

            // 17: ArmorVest (Ekipman)
            new RecipeInfo(RecipeId.ArmorVest, 2, "Hurda Zırh Yeleği", "Ekipman: Dış sefere çıkan kâşif ve yoldaşın aldığı tüm hasarı %35 azaltır.",
                new[] { R(ResourceType.Metal, 2), R(ResourceType.Scrap, 2) }, 8f),

            // 18: BackupGenerator (Grid Placeable 2x1 - İç Mekan)
            new RecipeInfo(RecipeId.BackupGenerator, 3, "Yedek Jeneratör", "Eve yerleştirilir (2x1). Depremde şalterin atma ihtimalini %65 düşürür; sağ tıkla bulunduğu kattan elektriği açar.",
                new[] { R(ResourceType.Metal, 2), R(ResourceType.Electronics, 2) }, 10f,
                repeatable: false, isPlaceable: true, gridWidth: 2, gridDepth: 1, allowedFloorMask: RecipeInfo.FloorIndoors),

            // 19: Smg (Ekipman / Silah)
            new RecipeInfo(RecipeId.Smg, 2, "Hafif Makineli (SMG)", "Dış seferde çok hızlı seri atış sağlar; köpek sürülerine ve yakın çatışmalara karşı etkilidir.",
                new[] { R(ResourceType.Metal, 2), R(ResourceType.Scrap, 2), R(ResourceType.Electronics, 1) }, 10f),

            // 20: AssaultRifle (Ekipman / Silah)
            new RecipeInfo(RecipeId.AssaultRifle, 3, "Taarruz Tüfeği", "Dış seferde yüksek hasar, uzun menzil ve dengeli seri atış sağlar.",
                new[] { R(ResourceType.Metal, 3), R(ResourceType.Scrap, 2), R(ResourceType.Electronics, 1) }, 12f),

            // 21: SniperRifle (Ekipman / Silah)
            new RecipeInfo(RecipeId.SniperRifle, 3, "Keskin Nişancı Tüfeği", "Dış seferde çok uzun menzil, nokta atışı isabet ve yıkıcı tek atış hasarı sağlar.",
                new[] { R(ResourceType.Metal, 3), R(ResourceType.Electronics, 2) }, 14f),

            // 22: ReinforcedDoor (Bina güçlendirmesi)
            new RecipeInfo(RecipeId.ReinforcedDoor, 2, "Çelik Kapı Barikatı", "Giriş kapısını çelik levhalarla güçlendirir: gece yağmacı baskınlarında savunmaya +40 ekler; nöbetçi olmasa bile kapı çoğu zorlamaya dayanır.",
                new[] { R(ResourceType.Metal, 3), R(ResourceType.Scrap, 2) }, 10f),
        };

        public static RecipeInfo Get(RecipeId id) => All[(int)id];
    }
}
