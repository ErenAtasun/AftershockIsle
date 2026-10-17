using AftershockIsle.Crafting;

namespace AftershockIsle.Expedition
{
    public enum WeaponKind { Slingshot, Pistol, Smg, Shotgun, AssaultRifle, SniperRifle }

    /// <summary>Dış saha silahı. Atış anlıktır (ışın); duvar, araba ve kasa yığını atışı keser.</summary>
    public readonly struct WeaponInfo
    {
        public readonly WeaponKind Kind;
        public readonly string Name;
        /// <summary>Saçma başına hasar.</summary>
        public readonly float Damage;
        public readonly float Range;
        public readonly float Interval;
        /// <summary>Atış sesinin düşmanları uyandırdığı yarıçap (m). Sıfır: sessiz.</summary>
        public readonly float Noise;
        public readonly bool UsesAmmo;
        /// <summary>Rastgele sapma (derece, her yöne).</summary>
        public readonly float Spread;
        public readonly int Pellets;
        /// <summary>İsabet edilen düşmanın geri savrulma mesafesi (m).</summary>
        public readonly float Knockback;
        /// <summary>Atışın kamera / titreşim şiddeti (0–1).</summary>
        public readonly float Recoil;

        public WeaponInfo(WeaponKind kind, string name, float damage, float range, float interval, float noise,
                          bool usesAmmo, float spread, int pellets, float knockback, float recoil)
        {
            Kind = kind;
            Name = name;
            Damage = damage;
            Range = range;
            Interval = interval;
            Noise = noise;
            UsesAmmo = usesAmmo;
            Spread = spread;
            Pellets = pellets;
            Knockback = knockback;
            Recoil = recoil;
        }
    }

    public static class FieldWeapons
    {
        /// <summary>Başlangıç silahı: taş sınırsız, sessiz, zayıf.</summary>
        public static readonly WeaponInfo Slingshot =
            new WeaponInfo(WeaponKind.Slingshot, "Sapan", 12f, 12f, 0.6f, 0f, false, 3f, 1, 0f, 0.1f);

        /// <summary>Tezgahta üretilir (Kademe 2). Dengeli ama gürültülü, mermi harcar.</summary>
        public static readonly WeaponInfo Pistol =
            new WeaponInfo(WeaponKind.Pistol, "El Yapımı Tabanca", 26f, 18f, 0.35f, 16f, true, 1.5f, 1, 0.3f, 0.3f);

        /// <summary>Tezgahta üretilir (Kademe 2). Çok hızlı seri atış, yakın-orta menzil.</summary>
        public static readonly WeaponInfo Smg =
            new WeaponInfo(WeaponKind.Smg, "Hafif Makineli (SMG)", 16f, 15f, 0.16f, 18f, true, 4.5f, 1, 0.2f, 0.22f);

        /// <summary>Tezgahta üretilir (Kademe 2). Yakında yıkıcı, geri iter; yavaş, çok gürültülü, atış başına 1 mermi.</summary>
        public static readonly WeaponInfo Shotgun =
            new WeaponInfo(WeaponKind.Shotgun, "Hurda Pompalı", 10f, 8f, 0.95f, 22f, true, 14f, 6, 1.4f, 0.8f);

        /// <summary>Tezgahta üretilir (Kademe 3). Yüksek hasar, uzun menzil, seri atış.</summary>
        public static readonly WeaponInfo AssaultRifle =
            new WeaponInfo(WeaponKind.AssaultRifle, "Taarruz Tüfeği", 30f, 22f, 0.24f, 20f, true, 2.0f, 1, 0.45f, 0.38f);

        /// <summary>Tezgahta üretilir (Kademe 3). Nokta atışı yüksek tekli hasar ve geri savurma.</summary>
        public static readonly WeaponInfo SniperRifle =
            new WeaponInfo(WeaponKind.SniperRifle, "Keskin Nişancı Tüfeği", 68f, 28f, 1.15f, 25f, true, 0.35f, 1, 1.6f, 0.9f);

        static readonly WeaponKind[] order =
        {
            WeaponKind.Slingshot,
            WeaponKind.Pistol,
            WeaponKind.Smg,
            WeaponKind.Shotgun,
            WeaponKind.AssaultRifle,
            WeaponKind.SniperRifle
        };

        public static WeaponInfo Get(WeaponKind kind) => kind switch
        {
            WeaponKind.Pistol => Pistol,
            WeaponKind.Smg => Smg,
            WeaponKind.Shotgun => Shotgun,
            WeaponKind.AssaultRifle => AssaultRifle,
            WeaponKind.SniperRifle => SniperRifle,
            _ => Slingshot,
        };

        public static bool IsAvailable(WeaponKind kind) => kind switch
        {
            WeaponKind.Pistol => Workbench.Has(RecipeId.Pistol),
            WeaponKind.Smg => Workbench.Has(RecipeId.Smg),
            WeaponKind.Shotgun => Workbench.Has(RecipeId.Shotgun),
            WeaponKind.AssaultRifle => Workbench.Has(RecipeId.AssaultRifle),
            WeaponKind.SniperRifle => Workbench.Has(RecipeId.SniperRifle),
            _ => true,
        };

        /// <summary>Sıradaki kullanılabilir silah (yoksa aynısı).</summary>
        public static WeaponKind Next(WeaponKind current)
        {
            int index = System.Array.IndexOf(order, current);
            for (int step = 1; step <= order.Length; step++)
            {
                var candidate = order[(index + step) % order.Length];
                if (IsAvailable(candidate)) return candidate;
            }
            return current;
        }

        /// <summary>Sefer başında elde olan en güçlü silah.</summary>
        public static WeaponKind Best()
        {
            for (int i = order.Length - 1; i > 0; i--)
                if (IsAvailable(order[i])) return order[i];
            return WeaponKind.Slingshot;
        }
    }
}
