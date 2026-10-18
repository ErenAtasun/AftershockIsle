using System.Collections.Generic;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>
    /// Sefer hazırlığı: kâşifin yanına aldığı silah, mermi, Yaşam Kiti ve aletler. Her şeyin ağırlığı (kg) vardır;
    /// çanta kapasitesinden ekipmanın ağırlığı düşülür, kalan yer ganimete kalır. Böylece "silahla mı, kazmayla mı"
    /// kararı verilir. Mermi ve kit sığınaktan alınır; dönüşte kalanı geri konur (kâşif ölürse kaybolur).
    /// Kazma gömülü sandıkları, Levye kilitli kasaları açar (sahada bu noktalar alet olmadan açılamaz).
    /// </summary>
    public class ExpeditionLoadout
    {
        public const int BaseCapacity = 10;
        public const int CompanionCapacity = 3;
        public const int AmmoPerKg = 10;
        public const int AmmoStep = 5;
        public const int MaxKits = 3;
        public const int PickaxeWeight = 2;
        public const int CrowbarWeight = 1;

        public WeaponKind Weapon = WeaponKind.Slingshot;
        public int Ammo;
        public int Kits;
        public bool Pickaxe;
        public bool Crowbar;

        // ---------------------------------------------------------------- Ağırlıklar

        public static int ResourceWeight(ResourceType type) => type switch
        {
            ResourceType.Metal => 2,
            ResourceType.Water => 2,
            _ => 1,
        };

        public static int Weight(ResourceAmount item) => ResourceWeight(item.type) * Mathf.Max(0, item.amount);

        public static int Weight(IEnumerable<ResourceAmount> items)
        {
            int total = 0;
            if (items != null) foreach (var item in items) total += Weight(item);
            return total;
        }

        public static int WeaponWeight(WeaponKind kind) => kind switch
        {
            WeaponKind.Pistol => 1,
            WeaponKind.Smg => 2,
            WeaponKind.Shotgun => 2,
            WeaponKind.AssaultRifle => 3,
            WeaponKind.SniperRifle => 3,
            _ => 0,
        };

        public static int AmmoWeight(int ammo) => ammo <= 0 ? 0 : Mathf.CeilToInt(ammo / (float)AmmoPerKg);

        /// <summary>Ekipmanın toplam ağırlığı (ganimet hariç).</summary>
        public int GearWeight(int ammo, int kits) =>
            WeaponWeight(Weapon) + AmmoWeight(ammo) + Mathf.Max(0, kits) + (Pickaxe ? PickaxeWeight : 0) + (Crowbar ? CrowbarWeight : 0);

        public int GearWeight() => GearWeight(Ammo, Kits);

        /// <summary>Çanta kapasitesi: taban + yetenek + Büyük Sırt Çantası + yoldaş.</summary>
        public static int Capacity(bool withCompanion) =>
            BaseCapacity + (SkillTree.Has(SkillId.WideBag) ? 2 : 0) + (ToolKit.HasBackpack ? 3 : 0) + (withCompanion ? CompanionCapacity : 0);

        // ---------------------------------------------------------------- Sığınaktaki stok

        public static int AmmoInShelter => Workbench.Instance != null ? Workbench.Instance.Ammo : 0;
        public static int KitsInShelter => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Supplies) : 0;

        public static List<WeaponKind> AvailableWeapons()
        {
            var list = new List<WeaponKind>();
            foreach (WeaponKind kind in System.Enum.GetValues(typeof(WeaponKind)))
                if (FieldWeapons.IsAvailable(kind)) list.Add(kind);
            return list;
        }

        /// <summary>Makul varsayılan: en iyi silah, 20 mermi, 1 kit; aletler alınmaz (yer kaplar, oyuncu seçer).</summary>
        public static ExpeditionLoadout Default()
        {
            var loadout = new ExpeditionLoadout { Weapon = FieldWeapons.Best() };
            loadout.Ammo = FieldWeapons.Get(loadout.Weapon).UsesAmmo ? Mathf.Min(20, AmmoInShelter) : 0;
            loadout.Kits = Mathf.Min(1, KitsInShelter);
            return loadout;
        }

        /// <summary>Stok değişmişse (ör. kit harcandı) seçimi geçerli sınırlara çeker.</summary>
        public void Clamp()
        {
            if (!FieldWeapons.IsAvailable(Weapon)) Weapon = WeaponKind.Slingshot;
            Ammo = FieldWeapons.Get(Weapon).UsesAmmo ? Mathf.Clamp(Ammo, 0, AmmoInShelter) : 0;
            Kits = Mathf.Clamp(Kits, 0, Mathf.Min(MaxKits, KitsInShelter));
            if (!ToolKit.HasPickaxe) Pickaxe = false;
            if (!ToolKit.HasCrowbar) Crowbar = false;
        }

        /// <summary>Yola çıkarken mermi ve kit sığınaktan düşülür.</summary>
        public void TakeFromShelter()
        {
            Clamp();
            Ammo = Workbench.Instance != null ? Workbench.Instance.TakeAmmo(Ammo) : 0;
            if (Kits > 0 && ResourceStorage.Instance != null) ResourceStorage.Instance.TrySpend(new ResourceAmount(ResourceType.Supplies, Kits));
        }

        /// <summary>Dönüşte kalan mermi ve kit sığınağa geri konur.</summary>
        public static void ReturnToShelter(int ammoLeft, int kitsLeft)
        {
            if (ammoLeft > 0 && Workbench.Instance != null) Workbench.Instance.AddAmmo(ammoLeft);
            if (kitsLeft > 0 && ResourceStorage.Instance != null) ResourceStorage.Instance.Add(new ResourceAmount(ResourceType.Supplies, kitsLeft));
        }

        public static string WeightLegend => "Ağırlıklar: Hurda 1 · Metal 2 · Elektronik 1 · Yaşam Kiti 1 kg · 10 mermi 1 kg";
    }
}
