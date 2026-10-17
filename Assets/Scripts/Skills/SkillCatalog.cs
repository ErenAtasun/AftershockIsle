namespace AftershockIsle.Skills
{
    public enum SkillBranch { Construction, Exploration, Engineering }

    public enum SkillId
    {
        QuickHammer, SteelBracing, LifeTriangleArchitect,
        NimbleStep, WideBag, LifeSaver,
        Recycling, AutomationMaster, FrequencyLock,
    }

    public readonly struct SkillInfo
    {
        public readonly SkillId Id;
        public readonly SkillBranch Branch;
        public readonly int Tier;
        public readonly string Name;
        public readonly string Description;

        public SkillInfo(SkillId id, SkillBranch branch, int tier, string name, string description)
        {
            Id = id;
            Branch = branch;
            Tier = tier;
            Name = name;
            Description = description;
        }
    }

    /// <summary>GDD 5.2'deki 3 dallı yetenek ağacı. Etkiler SkillTree yardımcıları üzerinden okunur.</summary>
    public static class SkillCatalog
    {
        public static readonly SkillInfo[] All =
        {
            new SkillInfo(SkillId.QuickHammer, SkillBranch.Construction, 1, "Hızlı Çekiç", "Tamir ve kazma hızı +%30."),
            new SkillInfo(SkillId.SteelBracing, SkillBranch.Construction, 2, "Çelik Tahkimat", "Yeniden dikilen çatlak kolonlar 2 sarsıntıya dayanır."),
            new SkillInfo(SkillId.LifeTriangleArchitect, SkillBranch.Construction, 3, "Yaşam Üçgeni Mimarı", "Kazazedeler sarsıntılarda hiç hasar almaz."),

            new SkillInfo(SkillId.NimbleStep, SkillBranch.Exploration, 1, "Çevik Adım", "Yürüme ve tırmanma hızı +%25."),
            new SkillInfo(SkillId.WideBag, SkillBranch.Exploration, 2, "Geniş Çanta", "Dış seferde +1 ganimet slotu. (Aşama 5)"),
            new SkillInfo(SkillId.LifeSaver, SkillBranch.Exploration, 3, "Hayat Kurtaran", "Kazazede çıkarma süresi yarıya iner. (Aşama 5)"),

            new SkillInfo(SkillId.Recycling, SkillBranch.Engineering, 1, "Geri Dönüşüm", "Mobilya parçalamadan +%50 hurda."),
            new SkillInfo(SkillId.AutomationMaster, SkillBranch.Engineering, 2, "Otomasyon Ustası", "Pompalar %50 daha geç bozulur. (Aşama 6)"),
            new SkillInfo(SkillId.FrequencyLock, SkillBranch.Engineering, 3, "Frekans Kilidi", "Telsiz sinyal hızı +%100. (Aşama 6)"),
        };

        public static SkillInfo Get(SkillId id) => All[(int)id];

        public static string ToDisplayName(this SkillBranch branch) => branch switch
        {
            SkillBranch.Construction => "İnşaat",
            SkillBranch.Exploration => "Keşif",
            _ => "Mühendislik",
        };
    }
}
