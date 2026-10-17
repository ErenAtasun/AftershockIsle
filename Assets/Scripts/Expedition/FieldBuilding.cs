using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>
    /// Dış sahadaki girilebilir tek katlı yapı: dört duvar (önde ve sağda kapı, arkada pencere), çatı, içeride siperlik eşya.
    /// Duvarlar gerçek collider taşır (yürüme, ileride mermi ve görüş hattı). Kâşif içeri girince çatı ve kameraya bakan
    /// ön duvar gizlenir, içerisi sığınaktaki gibi kesit olur; dışarı çıkınca geri gelir.
    /// </summary>
    public class FieldBuilding
    {
        public delegate Transform PrimitiveFactory(PrimitiveType type, string name, Color color, bool solid);

        const float WallThickness = 0.25f;
        const float WallHeight = 3.2f;
        const float DoorWidth = 1.6f;
        const float DoorHeight = 2.3f;
        const float WindowWidth = 1.6f;
        const float WindowBottom = 1.0f;
        const float WindowTop = 2.0f;

        static readonly Color WallColor = new Color(0.5f, 0.47f, 0.42f);
        static readonly Color InnerWallColor = new Color(0.42f, 0.4f, 0.36f);
        static readonly Color RoofColor = new Color(0.3f, 0.28f, 0.27f);
        static readonly Color FloorColor = new Color(0.36f, 0.33f, 0.3f);
        static readonly Color FurnitureColor = new Color(0.4f, 0.3f, 0.2f);

        readonly List<Renderer> cutaway = new List<Renderer>();
        readonly List<Vector3> spotPositions = new List<Vector3>();
        readonly PrimitiveFactory make;
        readonly float groundY;
        bool showingInterior;

        /// <summary>İç alan (X-Z): kâşif burada ise içeride sayılır.</summary>
        public Rect Interior { get; }

        /// <summary>Duvarlar dahil kapladığı alan (dışarıdaki engeller buna değmez).</summary>
        public Rect Footprint { get; }

        /// <summary>Ön kapının önündeki nokta (dışarıda boş bırakılır).</summary>
        public Vector2 FrontDoor { get; }

        /// <summary>İçeride aranacak yerler için uygun noktalar.</summary>
        public IReadOnlyList<Vector3> SpotPositions => spotPositions;

        public bool ShowingInterior => showingInterior;

        public FieldBuilding(PrimitiveFactory make, Vector2 min, Vector2 size, float groundY)
        {
            this.make = make;
            this.groundY = groundY;
            Footprint = new Rect(min, size);
            Interior = new Rect(min + Vector2.one * WallThickness, size - Vector2.one * (WallThickness * 2f));

            float x0 = min.x, z0 = min.y, x1 = min.x + size.x, z1 = min.y + size.y;
            float frontDoorX = x0 + size.x * 0.35f;
            float sideDoorZ = z0 + size.y * 0.5f;
            FrontDoor = new Vector2(frontDoorX, z0 - 1.2f);

            var floor = make(PrimitiveType.Cube, "Bina Zemini", FloorColor, false);
            floor.position = new Vector3((x0 + x1) * 0.5f, groundY + 0.01f, (z0 + z1) * 0.5f);
            floor.localScale = new Vector3(size.x, 0.02f, size.y);

            // Arka duvar: ortasında pencere.
            float windowX = (x0 + x1) * 0.5f;
            WallAlongX("Arka Duvar", z1 - WallThickness * 0.5f, x0, x1, windowX, WindowWidth, WindowBottom, WindowTop, false, InnerWallColor);

            // Sol duvar kapısız, sağ duvarda yan kapı.
            WallAlongZ("Sol Duvar", x0 + WallThickness * 0.5f, z0, z1, 0f, 0f, 0f, 0f, InnerWallColor);
            WallAlongZ("Sağ Duvar", x1 - WallThickness * 0.5f, z0, z1, sideDoorZ, DoorWidth, 0f, DoorHeight, InnerWallColor);

            // Ön duvar (kameraya bakan) ve çatı içerideyken gizlenir.
            WallAlongX("Ön Duvar", z0 + WallThickness * 0.5f, x0, x1, frontDoorX, DoorWidth, 0f, DoorHeight, true, WallColor);

            var roof = make(PrimitiveType.Cube, "Çatı", RoofColor, false);
            roof.position = new Vector3((x0 + x1) * 0.5f, groundY + WallHeight + 0.12f, (z0 + z1) * 0.5f);
            roof.localScale = new Vector3(size.x + 0.3f, 0.25f, size.y + 0.3f);
            cutaway.Add(roof.GetComponent<Renderer>());

            BuildFurniture(x0, z0, x1, z1);
        }

        /// <summary>Kâşif içerideyse çatı ve ön duvar gizlenir.</summary>
        public void UpdateCutaway(Vector3 avatarPosition)
        {
            bool inside = Interior.Contains(new Vector2(avatarPosition.x, avatarPosition.z));
            if (inside == showingInterior) return;

            showingInterior = inside;
            foreach (var renderer in cutaway)
                if (renderer != null) renderer.enabled = !inside;
        }

        public bool Overlaps(Rect rect, float margin)
        {
            var expanded = new Rect(Footprint.x - margin, Footprint.y - margin, Footprint.width + margin * 2f, Footprint.height + margin * 2f);
            return expanded.Overlaps(rect);
        }

        // İç eşya: sol duvarda raf, ortada devrik masa (siper). Aranacak yerler sağ arka köşe ve rafın önü.
        void BuildFurniture(float x0, float z0, float x1, float z1)
        {
            var shelf = make(PrimitiveType.Cube, "Raf", FurnitureColor, true);
            shelf.position = new Vector3(x0 + WallThickness + 0.3f, groundY + 0.9f, (z0 + z1) * 0.5f + 0.6f);
            shelf.localScale = new Vector3(0.5f, 1.8f, 2.2f);

            var table = make(PrimitiveType.Cube, "Devrik Masa", FurnitureColor * 0.9f, true);
            table.position = new Vector3((x0 + x1) * 0.5f + 0.6f, groundY + 0.45f, (z0 + z1) * 0.5f - 0.2f);
            table.localScale = new Vector3(1.8f, 0.9f, 0.5f);

            spotPositions.Add(new Vector3(x1 - WallThickness - 0.9f, groundY, z1 - WallThickness - 0.7f));
            spotPositions.Add(new Vector3(x0 + WallThickness + 1.5f, groundY, z1 - WallThickness - 0.7f));
            spotPositions.Add(new Vector3(x1 - WallThickness - 1.2f, groundY, z0 + WallThickness + 1.0f));
        }

        // X boyunca uzanan duvar; [gapCenter ± gapWidth/2] aralığında yerden gapBottom'a ve gapTop'tan yukarıya kadar dolu.
        void WallAlongX(string name, float z, float x0, float x1, float gapCenter, float gapWidth, float gapBottom, float gapTop,
                        bool hideInside, Color color)
        {
            float gapMin = gapCenter - gapWidth * 0.5f;
            float gapMax = gapCenter + gapWidth * 0.5f;

            Segment(name, new Vector3((x0 + gapMin) * 0.5f, 0f, z), new Vector3(gapMin - x0, WallHeight, WallThickness), 0f, hideInside, color);
            Segment(name, new Vector3((gapMax + x1) * 0.5f, 0f, z), new Vector3(x1 - gapMax, WallHeight, WallThickness), 0f, hideInside, color);
            if (gapBottom > 0f)
                Segment(name + " Alt", new Vector3(gapCenter, 0f, z), new Vector3(gapWidth, gapBottom, WallThickness), 0f, hideInside, color);
            Segment(name + " Üst", new Vector3(gapCenter, 0f, z), new Vector3(gapWidth, WallHeight - gapTop, WallThickness), gapTop, hideInside, color);
        }

        // Z boyunca uzanan duvar; gapWidth sıfırsa boşluksuz.
        void WallAlongZ(string name, float x, float z0, float z1, float gapCenter, float gapWidth, float gapBottom, float gapTop, Color color)
        {
            if (gapWidth <= 0f)
            {
                Segment(name, new Vector3(x, 0f, (z0 + z1) * 0.5f), new Vector3(WallThickness, WallHeight, z1 - z0), 0f, false, color);
                return;
            }

            float gapMin = gapCenter - gapWidth * 0.5f;
            float gapMax = gapCenter + gapWidth * 0.5f;
            Segment(name, new Vector3(x, 0f, (z0 + gapMin) * 0.5f), new Vector3(WallThickness, WallHeight, gapMin - z0), 0f, false, color);
            Segment(name, new Vector3(x, 0f, (gapMax + z1) * 0.5f), new Vector3(WallThickness, WallHeight, z1 - gapMax), 0f, false, color);
            if (gapBottom > 0f)
                Segment(name + " Alt", new Vector3(x, 0f, gapCenter), new Vector3(WallThickness, gapBottom, gapWidth), 0f, false, color);
            Segment(name + " Üst", new Vector3(x, 0f, gapCenter), new Vector3(WallThickness, WallHeight - gapTop, gapWidth), gapTop, false, color);
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
