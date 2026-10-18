using AftershockIsle.Interaction;

namespace AftershockIsle.Crafting
{
    /// <summary>
    /// Sığınağın alet dolabı: tezgahta üretilen aletleri herkes kullanır (tek sefer üretilir, kayıtta "üretildi" olarak saklanır).
    /// Kürek/Kazma kazıyı, Levye arama ve parçalamayı, Alet Takımı tamiri, Yangın Battaniyesi söndürmeyi hızlandırır;
    /// Kafa Lambası karanlık stresini, Büyük Sırt Çantası sefer çantasını etkiler.
    /// </summary>
    public static class ToolKit
    {
        public const float ShovelSpeed = 1.5f;
        public const float PickaxeSpeed = 2f;
        public const float CrowbarSpeed = 1.6f;
        public const float ToolboxSpeed = 1.4f;
        public const float BlanketSpeed = 1.5f;
        public const float PickaxeMetalChance = 0.25f;
        public const float HeadLampDarkStress = 0.4f;

        public static bool HasShovel => Workbench.Has(RecipeId.Shovel);
        public static bool HasPickaxe => Workbench.Has(RecipeId.Pickaxe);
        public static bool HasCrowbar => Workbench.Has(RecipeId.Crowbar);
        public static bool HasToolbox => Workbench.Has(RecipeId.Toolbox);
        public static bool HasHeadLamp => Workbench.Has(RecipeId.HeadLamp);
        public static bool HasBlanket => Workbench.Has(RecipeId.FireBlanket);
        public static bool HasBackpack => Workbench.Has(RecipeId.Backpack);

        /// <summary>Aletlerin iş hızına katkısı (en iyi alet geçerli).</summary>
        public static float WorkSpeed(WorkCategory category) => category switch
        {
            WorkCategory.Dig => HasPickaxe ? PickaxeSpeed : HasShovel ? ShovelSpeed : 1f,
            WorkCategory.Search or WorkCategory.Dismantle => HasCrowbar ? CrowbarSpeed : 1f,
            WorkCategory.Repair => HasToolbox ? ToolboxSpeed : 1f,
            WorkCategory.Firefight => HasBlanket ? BlanketSpeed : 1f,
            _ => 1f,
        };

        /// <summary>Bu iş için kullanılan aletin adı (yoksa null).</summary>
        public static string ToolFor(WorkCategory category) => category switch
        {
            WorkCategory.Dig => HasPickaxe ? "Kazma" : HasShovel ? "Kürek" : null,
            WorkCategory.Search or WorkCategory.Dismantle => HasCrowbar ? "Levye" : null,
            WorkCategory.Repair => HasToolbox ? "Alet Takımı" : null,
            WorkCategory.Firefight => HasBlanket ? "Yangın Battaniyesi" : null,
            _ => null,
        };

        /// <summary>
        /// Nesne bilgisinde gösterilen satır: alet varsa "Kürek ile 1.5x hızlı", yoksa hangi aletin işe yarayacağı.
        /// Bu iş için alet yoksa boş.
        /// </summary>
        public static string Hint(WorkCategory category)
        {
            string tool = ToolFor(category);
            if (tool != null) return $"{tool} ile {WorkSpeed(category):0.#}x hızlı";
            return category switch
            {
                WorkCategory.Dig => "Kürek ya da Kazma üretirsen daha hızlı kazılır",
                WorkCategory.Search or WorkCategory.Dismantle => "Levye üretirsen daha hızlı aranır/parçalanır",
                WorkCategory.Repair => "Alet Takımı üretirsen daha hızlı tamir edilir",
                WorkCategory.Firefight => "Yangın Battaniyesi üretirsen daha hızlı söndürülür",
                _ => "",
            };
        }
    }
}
