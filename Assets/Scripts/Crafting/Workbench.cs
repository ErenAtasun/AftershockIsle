using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Crafting
{
    public readonly struct RecipeCrafted
    {
        public readonly RecipeId Id;
        public RecipeCrafted(RecipeId id) => Id = id;
    }

    public readonly struct PlaceableItemReady
    {
        public readonly RecipeId Id;
        public PlaceableItemReady(RecipeId id) => Id = id;
    }

    public readonly struct WorkbenchMenuRequested
    {
        public readonly Survivor Worker;
        public WorkbenchMenuRequested(Survivor worker) => Worker = worker;
    }

    /// <summary>
    /// Çalışma Tezgahı. Sağ tık üretim menüsünü açar; seçilen tarif bekleyen sipariş olur ve
    /// karakter tezgahta çalışınca malzeme depodan düşülüp sonuç uygulanır.
    /// Yerleştirilebilir (Grid) eşyalar üretilince kurulum envanterine eklenir.
    /// </summary>
    public class Workbench : Interactable
    {
        readonly HashSet<RecipeId> crafted = new HashSet<RecipeId>();
        readonly Dictionary<RecipeId, int> unplaced = new Dictionary<RecipeId, int>();

        RecipeInfo? pending;

        public static Workbench Instance { get; private set; }

        public int Tier { get; private set; } = 1;
        public int SteelBeams { get; private set; }
        /// <summary>Dış sefer tabanca mermisi (Mermi Paketi ile üretilir).</summary>
        public int Ammo { get; private set; }

        public const int AmmoPerPack = 8;
        public RecipeInfo? PendingOrder => pending;

        /// <summary>
        /// Tarifin aktif olarak sığınakta bulunup bulunmadığını döndürür.
        /// Grid'e yerleştirilen yapılarda sahneye yerleştirilmiş olmasını da kontrol eder.
        /// </summary>
        public static bool Has(RecipeId id)
        {
            if (Instance == null) return false;
            var info = RecipeCatalog.Get(id);
            if (info.IsPlaceable)
                return PlaceableStructure.HasAny(id);
            return Instance.crafted.Contains(id);
        }

        public static bool IsCrafted(RecipeId id) => Instance != null && Instance.crafted.Contains(id);

        public int GetUnplacedCount(RecipeId id) => unplaced.TryGetValue(id, out int c) ? c : 0;

        public int TotalUnplacedCount
        {
            get
            {
                int total = 0;
                foreach (var kv in unplaced) total += kv.Value;
                return total;
            }
        }

        public void AddUnplaced(RecipeId id, int amount = 1)
        {
            if (amount <= 0) return;
            unplaced[id] = GetUnplacedCount(id) + amount;
        }

        public bool TryConsumeUnplaced(RecipeId id)
        {
            int count = GetUnplacedCount(id);
            if (count <= 0) return false;
            if (count == 1) unplaced.Remove(id);
            else unplaced[id] = count - 1;
            return true;
        }

        public IReadOnlyDictionary<RecipeId, int> UnplacedInventory => unplaced;

        public override string ActionLabel => pending.HasValue ? $"Üret: {pending.Value.Name}" : "Üretim Menüsü";
        public override WorkCategory Category => WorkCategory.Repair;
        public override float WorkDuration => pending.HasValue ? pending.Value.Duration : base.WorkDuration;
        public override bool RequiresEmptyHands => true;
        public override string StatusText =>
            $"Kademe {Tier}" + (SteelBeams > 0 ? $"   Stokta çelik kiriş: {SteelBeams}" : "") +
            (Ammo > 0 ? $"   Mermi: {Ammo}" : "") +
            (TotalUnplacedCount > 0 ? $"   Kuruluma hazır eşya: {TotalUnplacedCount} ([B] ile yerleştir)" : "") +
            (pending.HasValue ? $"\nSipariş: {pending.Value.Name}" : "");

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
        }

        // Sahneye konan tezgah prefabı eski greybox tezgahın yerini alınca (o kapatılır) Instance aktif olanı göstersin.
        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance != this) return;
            Instance = null;
            foreach (var other in FindObjectsByType<Workbench>(FindObjectsSortMode.None))
                if (other != this) { Instance = other; break; }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void HandleCommand(Survivor worker, bool append) =>
            EventBus.Publish(new WorkbenchMenuRequested(worker));

        public bool IsUnlocked(RecipeInfo recipe) => Tier >= recipe.RequiredTier;

        public bool CanOrder(RecipeInfo recipe, out string reason)
        {
            if (!IsUnlocked(recipe)) reason = $"Tezgah Kademe {recipe.RequiredTier} gerekli.";
            else if (!recipe.Repeatable && crafted.Contains(recipe.Id)) reason = "Zaten üretildi.";
            else if (recipe.Id == RecipeId.UpgradeTier2 && Tier >= 2) reason = "Zaten Kademe 2.";
            else if (recipe.Id == RecipeId.UpgradeTier3 && Tier >= 3) reason = "Zaten Kademe 3.";
            else if (!ResourceStorage.Instance.HasAll(recipe.Costs)) reason = "Depoda yeterli malzeme yok.";
            else reason = null;
            return reason == null;
        }

        /// <summary>Siparişi verir ve işçiyi tezgaha gönderir.</summary>
        public bool Order(RecipeInfo recipe, Survivor worker, bool append)
        {
            if (!CanOrder(recipe, out _) || worker == null) return false;

            pending = recipe;
            worker.Tasks.Assign(this, append);
            return true;
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (!pending.HasValue)
            {
                reason = "Önce üretim menüsünden bir tarif seç.";
                return false;
            }
            return CanOrder(pending.Value, out reason);
        }

        public override bool CanContinue(Survivor worker) => pending.HasValue;

        public override void Complete(Survivor worker)
        {
            if (!pending.HasValue) return;

            var recipe = pending.Value;
            pending = null;
            if (!ResourceStorage.Instance.TrySpendAll(recipe.Costs)) return;

            switch (recipe.Id)
            {
                case RecipeId.UpgradeTier2: Tier = Mathf.Max(Tier, 2); break;
                case RecipeId.UpgradeTier3: Tier = Mathf.Max(Tier, 3); break;
                case RecipeId.SteelBeam: SteelBeams++; break;
                case RecipeId.AmmoPack: Ammo += AmmoPerPack; break;
            }

            crafted.Add(recipe.Id);
            if (recipe.IsPlaceable)
            {
                AddUnplaced(recipe.Id, 1);
                EventBus.Publish(new PlaceableItemReady(recipe.Id));
            }

            EventBus.Publish(new RecipeCrafted(recipe.Id));
        }

        public bool TryUseAmmo()
        {
            if (Ammo <= 0) return false;
            Ammo--;
            return true;
        }

        /// <summary>Sahada düşmandan toplanan mermi.</summary>
        public void AddAmmo(int amount) => Ammo += Mathf.Max(0, amount);

        public bool TryUseSteelBeam()
        {
            if (SteelBeams <= 0) return false;
            SteelBeams--;
            return true;
        }

        public IEnumerable<RecipeId> GetCraftedRecipes() => crafted;

        public void RestoreState(int tier, int steelBeams, int ammo, IEnumerable<RecipeId> craftedRecipes,
                                 Dictionary<RecipeId, int> savedUnplaced = null)
        {
            Tier = tier;
            SteelBeams = steelBeams;
            Ammo = ammo;
            crafted.Clear();
            if (craftedRecipes != null)
            {
                foreach (var r in craftedRecipes) crafted.Add(r);
            }

            unplaced.Clear();
            if (savedUnplaced != null)
            {
                foreach (var kv in savedUnplaced)
                    if (kv.Value > 0) unplaced[kv.Key] = kv.Value;
            }
        }
    }
}
