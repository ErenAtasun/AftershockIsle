using AftershockIsle.Crafting;

namespace AftershockIsle.Expedition
{
    public enum WeaponKind { Slingshot, Pistol }

    /// <summary>Dış saha silahı. Atış anlıktır (ışın); duvar, araba ve kasa yığını atışı keser.</summary>
    public readonly struct WeaponInfo
    {
        public readonly WeaponKind Kind;
        public readonly string Name;
        public readonly float Damage;
        public readonly float Range;
        public readonly float Interval;
        /// <summary>Atış sesinin düşmanları uyandırdığı yarıçap (m). Sıfır: sessiz.</summary>
        public readonly float Noise;
        public readonly bool UsesAmmo;
        /// <summary>Rastgele sapma (derece).</summary>
        public readonly float Spread;

        public WeaponInfo(WeaponKind kind, string name, float damage, float range, float interval, float noise, bool usesAmmo, float spread)
        {
            Kind = kind;
            Name = name;
            Damage = damage;
            Range = range;
            Interval = interval;
            Noise = noise;
            UsesAmmo = usesAmmo;
            Spread = spread;
        }
    }

    public static class FieldWeapons
    {
        /// <summary>Başlangıç silahı: taş sınırsız, sessiz, zayıf.</summary>
        public static readonly WeaponInfo Slingshot = new WeaponInfo(WeaponKind.Slingshot, "Sapan", 12f, 12f, 0.6f, 0f, false, 3f);

        /// <summary>Tezgahta üretilir (Kademe 2). Güçlü ama gürültülü, mermi harcar.</summary>
        public static readonly WeaponInfo Pistol = new WeaponInfo(WeaponKind.Pistol, "El Yapımı Tabanca", 26f, 18f, 0.35f, 16f, true, 1.5f);

        public static WeaponInfo Get(WeaponKind kind) => kind == WeaponKind.Pistol ? Pistol : Slingshot;

        public static bool IsAvailable(WeaponKind kind) => kind == WeaponKind.Slingshot || Workbench.Has(RecipeId.Pistol);
    }
}
