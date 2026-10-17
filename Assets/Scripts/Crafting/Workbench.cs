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

    public readonly struct WorkbenchMenuRequested
    {
        public readonly Survivor Worker;
        public WorkbenchMenuRequested(Survivor worker) => Worker = worker;
    }

    /// <summary>
    /// Çalışma Tezgahı. Sağ tık üretim menüsünü açar; seçilen tarif bekleyen sipariş olur ve
    /// karakter tezgahta çalışınca malzeme depodan düşülüp sonuç uygulanır.
    /// </summary>
    public class Workbench : Interactable
    {
        readonly HashSet<RecipeId> crafted = new HashSet<RecipeId>();

        RecipeInfo? pending;

        public static Workbench Instance { get; private set; }

        public int Tier { get; private set; } = 1;
        public int SteelBeams { get; private set; }
        public RecipeInfo? PendingOrder => pending;

        public static bool Has(RecipeId id) => Instance != null && Instance.crafted.Contains(id);

        public override string ActionLabel => pending.HasValue ? $"Üret: {pending.Value.Name}" : "Üretim Menüsü";
        public override WorkCategory Category => WorkCategory.Repair;
        public override float WorkDuration => pending.HasValue ? pending.Value.Duration : base.WorkDuration;
        public override bool RequiresEmptyHands => true;
        public override string StatusText =>
            $"Kademe {Tier}" + (SteelBeams > 0 ? $"   Stokta çelik kiriş: {SteelBeams}" : "") +
            (pending.HasValue ? $"\nSipariş: {pending.Value.Name}" : "");

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
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
            }

            crafted.Add(recipe.Id);
            EventBus.Publish(new RecipeCrafted(recipe.Id));
        }

        public bool TryUseSteelBeam()
        {
            if (SteelBeams <= 0) return false;
            SteelBeams--;
            return true;
        }
    }
}
