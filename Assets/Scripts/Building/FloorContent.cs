using System.Collections.Generic;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Building
{
    /// <summary>
    /// Boş kalan katların içeriği (3. kat harap daireler, çatıda yağmur toplayıcı ve anten, bodrumda kazan ve raf).
    /// Tek liste hem sahne kurucuda (modellerle) hem ModelManager'ın ilk kurulumunda kullanılır: sahnede eksik olanlar
    /// editörde bir kez düz kutularla eklenir. İsimler benzersizdir (kayıt nesneleri ismiyle eşleştirir).
    /// </summary>
    public static class FloorContent
    {
        public enum Kind { Furniture, Rubble, Water, Seat }

        public class Def
        {
            public string Name;
            public string Label;
            public Kind Kind;
            public int Floor;
            public float X;
            /// <summary>Arka duvardan uzaklık (pozitif) ya da öne doğru konum (negatif: bina merkezine göre Z).</summary>
            public float BackOffset;
            public bool Front;
            public Vector3 Size;
            public Color Color;
            public float Duration = 5f;
            public ResourceAmount Salvage;
            public bool HasSearch;
            public ResourceAmount Search;
            public int Digs = 3;
            public bool Blocks;
            public int Charges = 1;
            /// <summary>Sahne kurucunun giydireceği model (ModelKey adı; boşsa kutu kalır).</summary>
            public string Model;
            public float ModelYaw;
            /// <summary>Sahne kurucunun kullanacağı greybox malzemesi (Furniture, Metal, Rubble, Water).</summary>
            public string Palette = "Furniture";
        }

        static readonly Color Wood = new Color(0.5f, 0.35f, 0.3f);
        static readonly Color Steel = new Color(0.55f, 0.6f, 0.68f);
        static readonly Color Stone = new Color(0.45f, 0.42f, 0.38f);
        static readonly Color Blue = new Color(0.2f, 0.45f, 0.8f);

        public static readonly Def[] All =
        {
            // ---- Kat 0: Bodrum
            new Def { Name = "Eski Kazan", Label = "Eski Kalorifer Kazanı", Kind = Kind.Furniture, Floor = 0, X = -3.5f, BackOffset = 0.55f,
                Size = new Vector3(1.2f, 1.5f, 0.9f), Color = Steel, Palette = "Metal", Duration = 7f, Salvage = new ResourceAmount(ResourceType.Metal, 3) },
            new Def { Name = "Kömürlük Rafı", Label = "Kömürlük Rafı", Kind = Kind.Furniture, Floor = 0, X = 7.5f, BackOffset = 0.4f,
                Size = new Vector3(1f, 1.6f, 0.6f), Color = Wood, Duration = 5f, Salvage = new ResourceAmount(ResourceType.Scrap, 2),
                HasSearch = true, Search = new ResourceAmount(ResourceType.Supplies, 1), Model = "Dresser", ModelYaw = 180f },

            // ---- Kat 3: Harap daireler (yeni gelir kaynakları, göçüğün arkasında bir kasa)
            new Def { Name = "Komşu Dolabı", Label = "Komşunun Dolabı", Kind = Kind.Furniture, Floor = 3, X = -3.8f, BackOffset = 0.45f,
                Size = new Vector3(1.2f, 1.6f, 0.7f), Color = Wood, Duration = 6f, Salvage = new ResourceAmount(ResourceType.Scrap, 2),
                HasSearch = true, Search = new ResourceAmount(ResourceType.Supplies, 2), Model = "Dresser", ModelYaw = 180f },
            new Def { Name = "Kitaplık", Label = "Kitaplık", Kind = Kind.Furniture, Floor = 3, X = 0.6f, BackOffset = 0.4f,
                Size = new Vector3(1.4f, 1.8f, 0.5f), Color = Wood, Duration = 6f, Salvage = new ResourceAmount(ResourceType.Scrap, 3),
                HasSearch = true, Search = new ResourceAmount(ResourceType.Electronics, 1) },
            new Def { Name = "Eski Buzdolabı", Label = "Eski Buzdolabı", Kind = Kind.Furniture, Floor = 3, X = 2.8f, BackOffset = 0.45f,
                Size = new Vector3(0.8f, 1.7f, 0.7f), Color = new Color(0.85f, 0.85f, 0.82f), Palette = "Metal", Duration = 6f, Salvage = new ResourceAmount(ResourceType.Metal, 2),
                HasSearch = true, Search = new ResourceAmount(ResourceType.Supplies, 1) },
            new Def { Name = "Moloz (3. Kat)", Label = "Moloz", Kind = Kind.Rubble, Floor = 3, X = -1.4f, Front = true, BackOffset = -1.2f,
                Size = new Vector3(1.1f, 0.6f, 1f), Color = Stone, Palette = "Rubble", Duration = 5f, Digs = 3, Model = "Rubble" },
            new Def { Name = "Göçük (3. Kat)", Label = "Göçük", Kind = Kind.Rubble, Floor = 3, X = 6.9f, Front = true, BackOffset = 0f,
                Size = new Vector3(1.4f, 1.2f, 1.8f), Color = Stone, Palette = "Rubble", Duration = 5f, Digs = 4, Blocks = true, Model = "Rubble" },
            new Def { Name = "Çelik Kasa", Label = "Çelik Kasa", Kind = Kind.Furniture, Floor = 3, X = 8f, BackOffset = 0.45f,
                Size = new Vector3(0.7f, 1f, 0.6f), Color = new Color(0.3f, 0.32f, 0.35f), Palette = "Metal", Duration = 8f, Salvage = new ResourceAmount(ResourceType.Metal, 2),
                HasSearch = true, Search = new ResourceAmount(ResourceType.Electronics, 2) },
            new Def { Name = "Sandalye (3. Kat)", Label = "Sandalye", Kind = Kind.Seat, Floor = 3, X = -2.2f, Front = true, BackOffset = -0.3f,
                Size = new Vector3(0.7f, 1.25f, 0.7f), Color = Wood, Duration = 7f, Model = "Chair", ModelYaw = 90f },

            // ---- Kat 4: Çatı
            new Def { Name = "Yağmur Toplayıcı", Label = "Yağmur Suyu Toplayıcı", Kind = Kind.Water, Floor = 4, X = -6f, BackOffset = 0.5f,
                Size = new Vector3(0.8f, 1f, 0.8f), Color = Blue, Palette = "Water", Duration = 2f, Charges = 1, Model = "Bucket" },
            new Def { Name = "Eski Anten", Label = "Eski Çanak Anten", Kind = Kind.Furniture, Floor = 4, X = -5f, BackOffset = 0.35f,
                Size = new Vector3(0.6f, 1.4f, 0.4f), Color = Steel, Palette = "Metal", Duration = 5f, Salvage = new ResourceAmount(ResourceType.Electronics, 1) },
        };

        /// <summary>Tanımın dünya konumu (kutu merkezi).</summary>
        public static Vector3 Center(BuildingLayout layout, Def def)
        {
            float z = def.Front ? layout.WalkPlaneZ + def.BackOffset : layout.WalkPlaneZ + layout.Depth * 0.5f - def.BackOffset;
            return new Vector3(layout.transform.position.x + def.X, layout.FloorY(def.Floor) + def.Size.y * 0.5f, z);
        }

        /// <summary>Kutuyu oluşturup bileşenini ayarlar (Awake, ayarlardan sonra çalışsın diye pasif kurulur).</summary>
        public static GameObject Create(BuildingLayout layout, Transform parent, Def def, Material material = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.SetActive(false);
            go.name = def.Name;
            go.transform.SetParent(parent, true);
            go.transform.position = Center(layout, def);
            go.transform.localScale = def.Size;

            var renderer = go.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var runtime = new Material(shader) { color = def.Color };
                if (runtime.HasProperty("_BaseColor")) runtime.SetColor("_BaseColor", def.Color);
                renderer.sharedMaterial = runtime;
            }

            switch (def.Kind)
            {
                case Kind.Furniture:
                    var furniture = go.AddComponent<Furniture>();
                    furniture.SetupRuntime(def.Salvage, false, def.HasSearch, def.Search);
                    furniture.ConfigureRuntime(def.Label, def.Duration);
                    break;
                case Kind.Rubble:
                    var rubble = go.AddComponent<RubblePile>();
                    rubble.SetupRuntime(def.Digs, def.Blocks);
                    rubble.ConfigureRuntime(def.Label, def.Duration);
                    break;
                case Kind.Water:
                    var water = go.AddComponent<WaterSource>();
                    water.SetupRuntime(def.Charges);
                    water.ConfigureRuntime(def.Label, def.Duration);
                    break;
                case Kind.Seat:
                    var seat = go.AddComponent<Seat>();
                    seat.ConfigureRuntime(def.Label, def.Duration);
                    break;
            }

            go.SetActive(true);
            return go;
        }

        // ---------------------------------------------------------------- Çalışma anı

        /// <summary>
        /// Sahnede olmayan içeriği düz kutularla ekler. Oyun açılışında çalışmaz (oyun yalnızca sahnedekini kullanır);
        /// ModelManager sahneyi ilk kurarken editörde bir kez çağırır, eklenenler sahneyle kaydedilir.
        /// </summary>
        public static void AddMissing(BuildingLayout layout)
        {
            if (layout == null || layout.FloorCount < 5) return;

            var existing = new HashSet<string>();
            foreach (var t in layout.GetComponentsInChildren<Transform>(true)) existing.Add(t.name);

            Transform parent = null;
            foreach (var def in All)
            {
                if (existing.Contains(def.Name)) continue;
                if (parent == null)
                {
                    parent = new GameObject("Kat İçeriği").transform;
                    parent.SetParent(layout.transform, false);
                }
                Create(layout, parent, def);
            }
        }
    }
}
