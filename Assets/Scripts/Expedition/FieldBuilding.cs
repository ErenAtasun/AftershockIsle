using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>Sahadaki yapının tipi; lokasyona göre seçilir.</summary>
    public enum FieldLayout { House, Construction, Clinic, Workshop, Ruin, Market, Station }

    /// <summary>
    /// Dış sahadaki girilebilir tek katlı yapı. Duvarlar gerçek collider taşır (yürüme, mermi, görüş hattı).
    /// Tipe göre: Ev (raf, masa), Klinik (iki oda: bekleme + koğuş, yataklar), Atölye (tezgahlar, raflar),
    /// Yıkık bina (çatısız, ön duvarı çökmüş, moloz yığınları), İnşaat (bel hizasında yarım duvarlar, çatısız).
    /// Çatılı yapıda kâşif içeri girince çatı ve kameraya bakan ön duvar gizlenir; dışarı çıkınca geri gelir.
    /// </summary>
    public class FieldBuilding
    {
        public delegate Transform PrimitiveFactory(PrimitiveType type, string name, Color color, bool solid);
        public delegate Renderer[] ArtDresser(Transform box, ArtKey key, float yaw);

        /// <summary>İçeride aranacak yer: zemin noktası, kutu boyutu, giydirilecek model ve yönü.</summary>
        public readonly struct SpotSlot
        {
            public readonly Vector3 Position;
            public readonly Vector3 Size;
            public readonly ArtKey Art;
            public readonly float Yaw;

            public SpotSlot(Vector3 position, Vector3 size, ArtKey art, float yaw)
            {
                Position = position;
                Size = size;
                Art = art;
                Yaw = yaw;
            }
        }

        const float WallThickness = 0.25f;
        const float DoorWidth = 1.6f;
        const float DoorHeight = 2.3f;
        const float WindowWidth = 1.6f;
        const float WindowBottom = 1.0f;
        const float WindowTop = 2.0f;

        // Modellerin ön yüzü dosyada +Z'de; bu açılar ön yüzü odanın içine çevirir.
        const float FaceCamera = 180f;
        const float FaceRight = 90f;
        const float FaceLeft = 270f;

        static readonly Color WallColor = new Color(0.5f, 0.47f, 0.42f);
        static readonly Color InnerWallColor = new Color(0.42f, 0.4f, 0.36f);
        static readonly Color RoofColor = new Color(0.3f, 0.28f, 0.27f);
        static readonly Color FloorColor = new Color(0.36f, 0.33f, 0.3f);
        static readonly Color FurnitureColor = new Color(0.4f, 0.3f, 0.2f);
        static readonly Color ConcreteColor = new Color(0.55f, 0.55f, 0.52f);
        static readonly Color RubbleColor = new Color(0.45f, 0.42f, 0.38f);
        static readonly Color MetalColor = new Color(0.55f, 0.6f, 0.66f);
        static readonly Color PumpColor = new Color(0.72f, 0.2f, 0.16f);

        readonly List<Renderer> cutaway = new List<Renderer>();
        readonly List<SpotSlot> spotSlots = new List<SpotSlot>();
        readonly PrimitiveFactory make;
        readonly ArtDresser dress;
        readonly float groundY;
        readonly float wallHeight;
        bool showingInterior;

        public FieldLayout Layout { get; }

        /// <summary>İç alan (X-Z): kâşif burada ise içeride sayılır.</summary>
        public Rect Interior { get; }

        /// <summary>Duvarlar dahil kapladığı alan (dışarıdaki engeller buna değmez).</summary>
        public Rect Footprint { get; }

        /// <summary>Ön kapının önündeki nokta (dışarıda boş bırakılır).</summary>
        public Vector2 FrontDoor { get; }

        /// <summary>İçeride aranacak yerler; sonuncusu kurtarma lokasyonunda mahsur kalanın yeridir.</summary>
        public IReadOnlyList<SpotSlot> SpotSlots => spotSlots;

        public bool ShowingInterior => showingInterior;

        public bool IsInside(Vector3 point) => Interior.Contains(new Vector2(point.x, point.z));

        public static float WidthFor(FieldLayout layout) => layout switch
        {
            FieldLayout.Clinic => 14f,
            FieldLayout.Market => 14f,
            FieldLayout.Station => 12f,
            FieldLayout.Workshop => 12f,
            FieldLayout.Construction => 12f,
            _ => 10f,
        };

        static bool HasRoof(FieldLayout layout) =>
            layout == FieldLayout.House || layout == FieldLayout.Clinic || layout == FieldLayout.Workshop || layout == FieldLayout.Market;

        // Bel hizası duvarlı açık yapılar (inşaat, camları kırık istasyon marketi): çömelince siper.
        static bool IsLowWalled(FieldLayout layout) => layout == FieldLayout.Construction || layout == FieldLayout.Station;

        public FieldBuilding(PrimitiveFactory make, ArtDresser dress, Vector2 min, Vector2 size, float groundY, FieldLayout layout)
        {
            this.make = make;
            this.dress = dress;
            this.groundY = groundY;
            Layout = layout;
            wallHeight = IsLowWalled(layout) ? 1.1f : layout == FieldLayout.Ruin ? 2.4f : 3.2f;
            Footprint = new Rect(min, size);
            Interior = new Rect(min + Vector2.one * WallThickness, size - Vector2.one * (WallThickness * 2f));

            float x0 = min.x, z0 = min.y, x1 = min.x + size.x, z1 = min.y + size.y;
            bool roof = HasRoof(layout);
            bool hideFront = roof || layout == FieldLayout.Ruin;

            // Yıkık binada ön duvarın büyük kısmı çökmüş: kapı yerine geniş bir yarık.
            float frontGap = layout == FieldLayout.Ruin ? 3.5f : DoorWidth;
            float frontDoorX = x0 + size.x * (layout == FieldLayout.Ruin ? 0.45f : 0.35f);
            float sideDoorZ = z0 + size.y * 0.5f;
            float lintel = layout == FieldLayout.Ruin || IsLowWalled(layout) ? wallHeight : DoorHeight;
            FrontDoor = new Vector2(frontDoorX, z0 - 1.2f);

            var floor = make(PrimitiveType.Cube, "Bina Zemini", FloorColor, false);
            floor.position = new Vector3((x0 + x1) * 0.5f, groundY + 0.01f, (z0 + z1) * 0.5f);
            floor.localScale = new Vector3(size.x, 0.02f, size.y);

            // Arka duvar: çatılı yapılarda ortasında pencere.
            float windowWidth = roof ? WindowWidth : 0f;
            WallAlongX("Arka Duvar", z1 - WallThickness * 0.5f, x0, x1, (x0 + x1) * 0.5f, windowWidth, WindowBottom, WindowTop, false, InnerWallColor);
            WallAlongZ("Sol Duvar", x0 + WallThickness * 0.5f, z0, z1, 0f, 0f, 0f, 0f, InnerWallColor);
            WallAlongZ("Sağ Duvar", x1 - WallThickness * 0.5f, z0, z1, sideDoorZ, DoorWidth, 0f, lintel, InnerWallColor);
            WallAlongX("Ön Duvar", z0 + WallThickness * 0.5f, x0, x1, frontDoorX, frontGap, 0f, lintel, hideFront, WallColor);

            if (roof)
            {
                var roofSlab = make(PrimitiveType.Cube, "Çatı", RoofColor, false);
                roofSlab.position = new Vector3((x0 + x1) * 0.5f, groundY + wallHeight + 0.12f, (z0 + z1) * 0.5f);
                roofSlab.localScale = new Vector3(size.x + 0.3f, 0.25f, size.y + 0.3f);
                cutaway.Add(roofSlab.GetComponent<Renderer>());
            }

            var inner = Interior;
            switch (layout)
            {
                case FieldLayout.Clinic: BuildClinic(inner); break;
                case FieldLayout.Workshop: BuildWorkshop(inner); break;
                case FieldLayout.Ruin: BuildRuin(inner); break;
                case FieldLayout.Construction: BuildConstruction(inner); break;
                case FieldLayout.Market: BuildMarket(inner); break;
                case FieldLayout.Station: BuildStation(inner); break;
                default: BuildHouse(inner); break;
            }
        }

        /// <summary>Kâşif içerideyse çatı ve ön duvar gizlenir.</summary>
        public void UpdateCutaway(Vector3 avatarPosition)
        {
            bool inside = IsInside(avatarPosition);
            if (inside == showingInterior) return;

            showingInterior = inside;
            foreach (var renderer in cutaway)
                if (renderer != null) renderer.enabled = !inside;
        }

        // ---------------------------------------------------------------- Tipe göre iç düzen

        void BuildHouse(Rect r)
        {
            Prop("Raf", ArtKey.Dresser, FaceRight, new Vector3(r.xMin + 0.3f, 0f, r.center.y + 0.6f), new Vector3(0.55f, 1.6f, 2f), true, FurnitureColor);
            Prop("Masa", ArtKey.Table, 0f, new Vector3(r.center.x + 0.6f, 0f, r.center.y - 0.2f), new Vector3(1.6f, 1.05f, 0.9f), true, FurnitureColor);
            Prop("Sandalye", ArtKey.Chair, FaceCamera, new Vector3(r.center.x + 0.6f, 0f, r.center.y + 0.65f), new Vector3(0.7f, 1.25f, 0.7f), false, FurnitureColor);

            Spot(new Vector3(r.xMax - 0.9f, 0f, r.yMax - 0.35f), new Vector3(1.3f, 1.3f, 0.65f), ArtKey.Dresser, FaceCamera);
            Spot(new Vector3(r.xMin + 1.6f, 0f, r.yMax - 0.5f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
            Spot(new Vector3(r.xMax - 1.1f, 0f, r.yMin + 0.8f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
        }

        // İki oda: solda bekleme (masa), sağda koğuş (yataklar). Aradaki duvarın kapısı köşe çatışması yaratır.
        void BuildClinic(Rect r)
        {
            float divider = r.center.x;
            WallAlongZ("Ara Duvar", divider, r.yMin, r.yMax, r.yMin + 1.3f, 1.4f, 0f, DoorHeight, InnerWallColor);

            Prop("Kayıt Masası", ArtKey.Table, 0f, new Vector3(r.xMin + 2.2f, 0f, r.center.y), new Vector3(1.8f, 1.1f, 0.9f), true, FurnitureColor);
            Prop("Yangın Tüpü", ArtKey.Extinguisher, FaceCamera, new Vector3(r.xMin + 0.3f, 0f, r.yMin + 0.4f), new Vector3(0.4f, 0.85f, 0.3f), false, Color.red);
            Prop("Yatak", ArtKey.Bed, 90f, new Vector3(divider + 2f, 0f, r.yMax - 0.6f), new Vector3(2.2f, 0.9f, 1.1f), true, FurnitureColor);
            Prop("Yatak", ArtKey.Bed, 90f, new Vector3(divider + 4.6f, 0f, r.yMax - 0.6f), new Vector3(2.2f, 0.9f, 1.1f), true, FurnitureColor);

            Spot(new Vector3(r.xMin + 1.2f, 0f, r.yMax - 0.35f), new Vector3(1.3f, 1.3f, 0.65f), ArtKey.Dresser, FaceCamera);
            Spot(new Vector3(r.xMax - 0.35f, 0f, r.center.y - 0.6f), new Vector3(0.65f, 1.3f, 1.3f), ArtKey.Dresser, FaceLeft);
            Spot(new Vector3(divider + 1.2f, 0f, r.yMin + 0.8f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
        }

        void BuildWorkshop(Rect r)
        {
            Prop("Tezgah", ArtKey.Table, 0f, new Vector3(r.center.x - 1.6f, 0f, r.center.y), new Vector3(2f, 1.1f, 0.9f), true, FurnitureColor);
            Prop("Tezgah", ArtKey.Table, 0f, new Vector3(r.center.x + 1.6f, 0f, r.center.y), new Vector3(2f, 1.1f, 0.9f), true, FurnitureColor);
            Prop("Raf", ArtKey.Dresser, FaceRight, new Vector3(r.xMin + 0.3f, 0f, r.center.y + 0.8f), new Vector3(0.55f, 1.6f, 2.2f), true, FurnitureColor);
            Prop("Kasa", ArtKey.Crate, 0f, new Vector3(r.xMax - 0.6f, 0f, r.yMax - 1.8f), new Vector3(0.9f, 0.85f, 0.9f), true, FurnitureColor);
            Prop("Kasa", ArtKey.Crate, 0f, new Vector3(r.xMax - 0.6f, 0.85f, r.yMax - 1.8f), new Vector3(0.8f, 0.8f, 0.8f), true, FurnitureColor);
            Prop("Kova", ArtKey.Bucket, 0f, new Vector3(r.center.x, 0f, r.yMin + 0.5f), new Vector3(0.5f, 0.6f, 0.5f), false, FurnitureColor);

            Spot(new Vector3(r.center.x, 0f, r.yMax - 0.35f), new Vector3(1.3f, 1.3f, 0.65f), ArtKey.Dresser, FaceCamera);
            Spot(new Vector3(r.xMin + 0.35f, 0f, r.yMin + 1.2f), new Vector3(0.65f, 1.3f, 1.3f), ArtKey.Dresser, FaceRight);
            Spot(new Vector3(r.xMax - 1f, 0f, r.yMin + 0.9f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
        }

        // Çatısı yok, ön duvarı çökmüş; içerisi moloz yığınlarıyla dolu (siper). Mahsur kalan son yerde.
        void BuildRuin(Rect r)
        {
            Prop("Moloz", ArtKey.Rubble, 0f, new Vector3(r.xMin + 2f, 0f, r.center.y + 0.5f), new Vector3(1.6f, 1.1f, 1.3f), true, RubbleColor);
            Prop("Moloz", ArtKey.Rubble, 0f, new Vector3(r.center.x + 1.5f, 0f, r.center.y - 0.6f), new Vector3(1.3f, 0.9f, 1.1f), true, RubbleColor);
            Prop("Moloz", ArtKey.Rubble, 0f, new Vector3(r.xMax - 1.4f, 0f, r.yMax - 1f), new Vector3(1.8f, 1.3f, 1.2f), true, RubbleColor);

            Spot(new Vector3(r.center.x - 0.5f, 0f, r.yMax - 0.7f), new Vector3(1.2f, 0.7f, 1f), ArtKey.Rubble, 0f);
            Spot(new Vector3(r.xMin + 0.8f, 0f, r.yMin + 0.9f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
            Spot(new Vector3(r.xMax - 1.2f, 0f, r.yMin + 1.3f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
        }

        // Bel hizasında duvarlar: çömelince siper, ayaktayken üstünden ateş edilir.
        void BuildConstruction(Rect r)
        {
            Prop("Beton Blok", ArtKey.Crate, 0f, new Vector3(r.center.x - 1.5f, 0f, r.center.y), new Vector3(2f, 1f, 0.7f), true, ConcreteColor, false);
            Prop("Kasa", ArtKey.Crate, 0f, new Vector3(r.xMin + 1f, 0f, r.yMax - 1f), new Vector3(0.9f, 0.85f, 0.9f), true, FurnitureColor);
            Prop("Kasa", ArtKey.Crate, 0f, new Vector3(r.xMin + 1f, 0.85f, r.yMax - 1f), new Vector3(0.8f, 0.8f, 0.8f), true, FurnitureColor);
            Prop("Moloz", ArtKey.Rubble, 0f, new Vector3(r.xMax - 1.5f, 0f, r.center.y + 0.6f), new Vector3(1.4f, 0.9f, 1.1f), true, RubbleColor);

            Spot(new Vector3(r.center.x + 1.5f, 0f, r.yMax - 0.6f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
            Spot(new Vector3(r.xMin + 2.5f, 0f, r.yMin + 0.9f), new Vector3(1.2f, 0.7f, 1f), ArtKey.Rubble, 0f);
            Spot(new Vector3(r.xMax - 0.8f, 0f, r.yMin + 0.9f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
        }

        // Market: iki uzun raf sırası koridor oluşturur (raf arkası siper, koridor boyu açık atış hattı); önde kasa.
        void BuildMarket(Rect r)
        {
            Prop("Raf Sırası", ArtKey.Dresser, FaceCamera, new Vector3(r.center.x - 2.2f, 0f, r.center.y + 0.9f), new Vector3(3.2f, 1.5f, 0.6f), true, FurnitureColor);
            Prop("Raf Sırası", ArtKey.Dresser, FaceCamera, new Vector3(r.center.x + 2.2f, 0f, r.center.y + 0.9f), new Vector3(3.2f, 1.5f, 0.6f), true, FurnitureColor);
            Prop("Raf Sırası", ArtKey.Dresser, FaceCamera, new Vector3(r.center.x, 0f, r.center.y - 0.6f), new Vector3(3.6f, 1.5f, 0.6f), true, FurnitureColor);
            Prop("Kasa Bankosu", ArtKey.Table, 0f, new Vector3(r.xMin + 1.4f, 0f, r.yMin + 0.8f), new Vector3(1.8f, 1.05f, 0.8f), true, FurnitureColor);
            Prop("Devrik Araba", ArtKey.Crate, 0f, new Vector3(r.xMax - 2f, 0f, r.yMin + 0.7f), new Vector3(0.8f, 0.7f, 0.6f), false, MetalColor, false);

            Spot(new Vector3(r.xMin + 0.9f, 0f, r.yMax - 0.35f), new Vector3(1.3f, 1.3f, 0.65f), ArtKey.Dresser, FaceCamera);
            Spot(new Vector3(r.xMax - 0.9f, 0f, r.yMax - 0.35f), new Vector3(1.3f, 1.3f, 0.65f), ArtKey.Dresser, FaceCamera);
            Spot(new Vector3(r.center.x, 0f, r.yMax - 0.5f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
            Spot(new Vector3(r.xMax - 0.8f, 0f, r.center.y - 0.4f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
        }

        // Benzin istasyonu marketi: camları kırık bel hizası duvarlar, ortada pompa adası (siper), arkada depo kasaları.
        void BuildStation(Rect r)
        {
            Prop("Akaryakıt Pompası", ArtKey.Crate, 0f, new Vector3(r.center.x - 1.2f, 0f, r.center.y), new Vector3(0.6f, 1.6f, 0.6f), true, PumpColor, false);
            Prop("Akaryakıt Pompası", ArtKey.Crate, 0f, new Vector3(r.center.x + 1.2f, 0f, r.center.y), new Vector3(0.6f, 1.6f, 0.6f), true, PumpColor, false);
            Prop("Pompa Adası", ArtKey.Crate, 0f, new Vector3(r.center.x, 0f, r.center.y), new Vector3(3.4f, 0.25f, 1f), false, ConcreteColor, false);
            Prop("Bidon", ArtKey.Bucket, 0f, new Vector3(r.xMin + 0.6f, 0f, r.center.y - 0.8f), new Vector3(0.5f, 0.7f, 0.5f), true, PumpColor);
            Prop("Kasa", ArtKey.Crate, 0f, new Vector3(r.xMax - 0.7f, 0f, r.yMax - 1.2f), new Vector3(0.9f, 0.85f, 0.9f), true, FurnitureColor);

            Spot(new Vector3(r.xMin + 1f, 0f, r.yMax - 0.5f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
            Spot(new Vector3(r.center.x, 0f, r.yMax - 0.35f), new Vector3(1.3f, 1.3f, 0.65f), ArtKey.Dresser, FaceCamera);
            Spot(new Vector3(r.xMax - 0.9f, 0f, r.yMin + 0.9f), new Vector3(0.9f, 0.8f, 0.9f), ArtKey.Crate, 0f);
        }

        /// <summary>Eşya: kutu (collider) + üstüne model. floorPoint.y: yığında alttaki kutunun üstü. useArt false: düz kutu.</summary>
        void Prop(string name, ArtKey art, float yaw, Vector3 floorPoint, Vector3 size, bool solid, Color color, bool useArt = true)
        {
            var box = make(PrimitiveType.Cube, name, color, solid);
            box.position = new Vector3(floorPoint.x, groundY + floorPoint.y + size.y * 0.5f, floorPoint.z);
            box.localScale = size;
            if (useArt) dress?.Invoke(box, art, yaw);
        }

        void Spot(Vector3 floorPoint, Vector3 size, ArtKey art, float yaw) =>
            spotSlots.Add(new SpotSlot(new Vector3(floorPoint.x, groundY, floorPoint.z), size, art, yaw));

        // ---------------------------------------------------------------- Duvarlar

        // X boyunca uzanan duvar; [gapCenter ± gapWidth/2] aralığında yerden gapBottom'a ve gapTop'tan yukarıya kadar dolu.
        void WallAlongX(string name, float z, float x0, float x1, float gapCenter, float gapWidth, float gapBottom, float gapTop,
                        bool hideInside, Color color)
        {
            if (gapWidth <= 0f)
            {
                Segment(name, new Vector3((x0 + x1) * 0.5f, 0f, z), new Vector3(x1 - x0, wallHeight, WallThickness), 0f, hideInside, color);
                return;
            }

            float gapMin = gapCenter - gapWidth * 0.5f;
            float gapMax = gapCenter + gapWidth * 0.5f;
            Segment(name, new Vector3((x0 + gapMin) * 0.5f, 0f, z), new Vector3(gapMin - x0, wallHeight, WallThickness), 0f, hideInside, color);
            Segment(name, new Vector3((gapMax + x1) * 0.5f, 0f, z), new Vector3(x1 - gapMax, wallHeight, WallThickness), 0f, hideInside, color);
            if (gapBottom > 0f)
                Segment(name + " Alt", new Vector3(gapCenter, 0f, z), new Vector3(gapWidth, gapBottom, WallThickness), 0f, hideInside, color);
            Segment(name + " Üst", new Vector3(gapCenter, 0f, z), new Vector3(gapWidth, wallHeight - gapTop, WallThickness), gapTop, hideInside, color);
        }

        // Z boyunca uzanan duvar; gapWidth sıfırsa boşluksuz.
        void WallAlongZ(string name, float x, float z0, float z1, float gapCenter, float gapWidth, float gapBottom, float gapTop, Color color)
        {
            if (gapWidth <= 0f)
            {
                Segment(name, new Vector3(x, 0f, (z0 + z1) * 0.5f), new Vector3(WallThickness, wallHeight, z1 - z0), 0f, false, color);
                return;
            }

            float gapMin = gapCenter - gapWidth * 0.5f;
            float gapMax = gapCenter + gapWidth * 0.5f;
            Segment(name, new Vector3(x, 0f, (z0 + gapMin) * 0.5f), new Vector3(WallThickness, wallHeight, gapMin - z0), 0f, false, color);
            Segment(name, new Vector3(x, 0f, (gapMax + z1) * 0.5f), new Vector3(WallThickness, wallHeight, z1 - gapMax), 0f, false, color);
            if (gapBottom > 0f)
                Segment(name + " Alt", new Vector3(x, 0f, gapCenter), new Vector3(WallThickness, gapBottom, gapWidth), 0f, false, color);
            Segment(name + " Üst", new Vector3(x, 0f, gapCenter), new Vector3(WallThickness, wallHeight - gapTop, gapWidth), gapTop, false, color);
        }

        void Segment(string name, Vector3 center, Vector3 size, float bottom, bool hideInside, Color color)
        {
            if (size.x <= 0.01f || size.y <= 0.01f || size.z <= 0.01f) return;

            var wall = make(PrimitiveType.Cube, name, color, true);
            wall.position = new Vector3(center.x, groundY + bottom + size.y * 0.5f, center.z);
            wall.localScale = size;
            if (!hideInside) return;
            cutaway.Add(wall.GetComponent<Renderer>());

            // Gizlenen duvarın yerini belli etmek için hep görünen alçak taban izi.
            if (bottom > 0f) return;
            var stub = make(PrimitiveType.Cube, name + " Taban", color * 0.8f, false);
            stub.position = new Vector3(center.x, groundY + 0.15f, center.z);
            stub.localScale = new Vector3(size.x, 0.3f, size.z);
        }
    }
}
