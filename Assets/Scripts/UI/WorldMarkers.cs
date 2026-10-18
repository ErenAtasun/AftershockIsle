using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Interaction;
using AftershockIsle.Player;
using UnityEngine;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Dünya işaretleri (uGUI): nesnelerin üstünde ne yapılabileceğini söyleyen küçük etiketler ve kat adları.
    /// Kırmızı yanıp sönen = acil (yangın, gaz, patlak boru, kırık kolon, su baskını); amber = önerilen (yolu kapatan moloz,
    /// güçlendirilecek kolon); gri = isteğe bağlı iş (yalnızca seçili kazazedenin katında); soluk lacivert = istasyon.
    /// Kat adlarının yanında o kattaki tehlike (yangın, duman, gaz, su) yazar. İşaretler tıklamayı engellemez.
    /// </summary>
    public class WorldMarkers : MonoBehaviour
    {
        const float RebuildSeconds = 0.4f;
        const float ListSeconds = 1f;
        const int PoolSize = 64;

        struct Entry
        {
            public Vector3 World;
            public string Text;
            public MarkerKind Kind;
            public bool IsFloor;
            public Color FloorColor;
        }

        class Item
        {
            public RectTransform Rect;
            public Image Plate;
            public Text Label;
            public Outline Outline;
            public float Width;
        }

        CommandController commands;
        BuildingLayout building;
        Camera cam;
        RectTransform canvasRect;
        readonly List<Item> pool = new List<Item>();
        readonly List<Entry> entries = new List<Entry>();
        readonly List<Interactable> things = new List<Interactable>();
        readonly Vector2[] screen = new Vector2[PoolSize];
        float rebuildAt;
        float listAt;
        int used;
        bool showAll;

        void Start()
        {
            commands = FindFirstObjectByType<CommandController>();
            building = FindFirstObjectByType<BuildingLayout>();
            var canvas = UiFactory.Canvas("[İşaretler]", 4, transform);
            canvasRect = (RectTransform)canvas.transform;
        }

        void LateUpdate()
        {
            if (building == null || canvasRect == null) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            var session = FindFirstObjectByType<GameSession>();
            bool hidden = SideViewCamera.ViewingField || ModalMenu.IsOpen || PauseMenu.IsOpen ||
                          (session != null && session.Outcome != GameOutcome.None);
            if (hidden)
            {
                HideAll();
                return;
            }

            if (Time.unscaledTime >= rebuildAt)
            {
                rebuildAt = Time.unscaledTime + RebuildSeconds;
                Rebuild();
            }

            Position();
        }

        void HideAll()
        {
            foreach (var item in pool)
                if (item.Rect.gameObject.activeSelf) item.Rect.gameObject.SetActive(false);
            used = 0;
        }

        // ---------------------------------------------------------------- Liste

        void Rebuild()
        {
            if (Time.unscaledTime >= listAt)
            {
                listAt = Time.unscaledTime + ListSeconds;
                things.Clear();
                things.AddRange(FindObjectsByType<Interactable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            }

            entries.Clear();
            var selected = commands != null ? commands.Selected : null;
            int selectedFloor = selected != null ? selected.Mover.CurrentFloor : -1;

            // Ekran sade kalsın: acil ve önerilen işaretler hep görünür; isteğe bağlı işler, istasyonlar ve kat adları
            // yalnızca zaman durunca ya da Alt basılıyken (kat adları ayrıca o katta tehlike varsa) gösterilir.
            showAll = Time.timeScale <= 0f || GameInput.ShowAllMarkersHeld;
            var hovered = commands != null ? commands.Hovered : null;
            var nearby = commands != null ? commands.Nearby : null;

            foreach (var thing in things)
            {
                if (thing == null || !thing.isActiveAndEnabled || !thing.IsInteractive) continue;
                // Üstüne gelinen ya da yanındaki nesneyi bilgi kartı ve "[F]" istemi zaten anlatıyor.
                if (thing == hovered || thing == nearby) continue;

                var guide = InteractableGuide.Of(thing);
                if (guide.Kind == MarkerKind.None || string.IsNullOrEmpty(guide.Marker)) continue;
                if ((guide.Kind == MarkerKind.Optional || guide.Kind == MarkerKind.Station) && !showAll) continue;
                if (guide.Kind == MarkerKind.Optional && thing.Floor != selectedFloor) continue;
                // Elinde iş olan (başka biri çalışan) nesneye de etiket kalır; çalışan zaten ilerleme çubuğuyla görünür.

                Vector3 world = thing.TryGetFootprint(out var bounds)
                    ? new Vector3(bounds.center.x, bounds.max.y + 0.25f, bounds.center.z)
                    : thing.transform.position + Vector3.up * 1.2f;
                entries.Add(new Entry { World = world, Text = guide.Marker, Kind = guide.Kind });
            }

            AddFloorLabels();
            Apply();
        }

        void AddFloorLabels()
        {
            var flood = FloodSystem.Instance;
            for (int floor = 0; floor < building.FloorCount; floor++)
            {
                string name = building.GetFloorName(floor).ToUpperInvariant();
                var hazards = new List<string>();

                if (floor == 0 && flood != null)
                {
                    if (flood.Level > 1f) hazards.Add($"SU %{flood.Level:0}");
                    if (flood.IsPipeBurst) hazards.Add("BORU PATLAK");
                }

                var fire = FireSpot.ForFloor(floor);
                if (fire != null && fire.IsBurning) hazards.Add($"YANGIN %{fire.Intensity:0}");
                else if (FireSpot.IsSmokeBlocked(floor)) hazards.Add("DUMAN / GAZ");

                foreach (var leak in GasLeak.All)
                    if (leak != null && leak.Floor == floor && leak.IsLeaking) { hazards.Add("GAZ KAÇAĞI"); break; }

                // Tehlike yoksa kat adı yalnızca "hepsini göster" modunda yazılır.
                if (hazards.Count == 0 && !showAll) continue;

                int rubble = RubblePile.CountOnFloor(floor);
                string tail = hazards.Count > 0 ? "  ·  " + string.Join("  ·  ", hazards) : "";
                if (rubble > 0 && hazards.Count == 0) tail = $"  ·  {rubble} moloz";

                var world = new Vector3(building.transform.position.x - building.Width * 0.5f - 0.5f, building.FloorY(floor) + 2.7f, building.WalkPlaneZ);
                entries.Add(new Entry
                {
                    World = world,
                    Text = name + tail,
                    Kind = MarkerKind.None,
                    IsFloor = true,
                    FloorColor = hazards.Count > 0 ? new Color(1f, 0.55f, 0.45f) : new Color(0.78f, 0.8f, 0.82f, 0.85f),
                });
            }
        }

        // ---------------------------------------------------------------- Görünüm

        void Apply()
        {
            used = Mathf.Min(entries.Count, PoolSize);
            while (pool.Count < used) pool.Add(CreateItem());

            for (int i = 0; i < pool.Count; i++)
            {
                var item = pool[i];
                if (i >= used)
                {
                    if (item.Rect.gameObject.activeSelf) item.Rect.gameObject.SetActive(false);
                    continue;
                }

                var entry = entries[i];
                item.Rect.gameObject.SetActive(true);
                item.Label.text = entry.Text;

                switch (entry.Kind)
                {
                    case MarkerKind.Urgent:
                        Style(item, 17, new Color(0.78f, 0.14f, 0.1f, 0.94f), Color.white, true);
                        break;
                    case MarkerKind.Important:
                        Style(item, 16, new Color(0.86f, 0.62f, 0.1f, 0.94f), new Color(0.1f, 0.08f, 0.02f), false);
                        break;
                    case MarkerKind.Station:
                        Style(item, 13, new Color(0.09f, 0.11f, 0.16f, 0.72f), new Color(0.82f, 0.88f, 0.95f), false);
                        break;
                    case MarkerKind.Optional:
                        Style(item, 14, new Color(0.2f, 0.22f, 0.26f, 0.82f), new Color(0.92f, 0.92f, 0.88f), false);
                        break;
                    default:
                        // Kat adı: zemin yok, kenarlıklı yazı.
                        Style(item, 15, new Color(0f, 0f, 0f, 0f), entry.FloorColor, true);
                        item.Outline.enabled = true;
                        break;
                }

                item.Width = Mathf.Max(48f, item.Label.preferredWidth + (entry.IsFloor ? 12f : 20f));
                item.Rect.sizeDelta = new Vector2(item.Width, entry.IsFloor ? 24f : 25f);
            }
        }

        static void Style(Item item, int size, Color plate, Color text, bool outline)
        {
            item.Label.fontSize = size;
            item.Label.color = text;
            item.Plate.color = plate;
            item.Outline.enabled = outline;
        }

        Item CreateItem()
        {
            var plate = UiFactory.Image(canvasRect, "İşaret", null, new Color(0.2f, 0.22f, 0.26f, 0.82f));
            plate.raycastTarget = false;
            var label = UiFactory.Label(plate.transform, "", 14, UiFactory.Text, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UiFactory.Stretch(label.rectTransform, 2f);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rect = plate.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            return new Item { Rect = rect, Plate = plate, Label = label, Outline = label.GetComponent<Outline>() };
        }

        void Position()
        {
            if (used == 0) return;
            var half = canvasRect.rect.size * 0.5f;

            for (int i = 0; i < used; i++)
            {
                var entry = entries[i];
                var point = cam.WorldToScreenPoint(entry.World);
                var item = pool[i];
                if (point.z < 0f)
                {
                    item.Rect.gameObject.SetActive(false);
                    screen[i] = new Vector2(-9999f, -9999f);
                    continue;
                }
                if (!item.Rect.gameObject.activeSelf) item.Rect.gameObject.SetActive(true);

                // Bina kenarı ekran dışındaysa kat adı da gizlenir (kenara yapışıp başka arayüzün üstüne binmesin).
                if (entry.IsFloor && (point.x - item.Width * 0.5f < 4f || point.x + item.Width * 0.5f > Screen.width - 4f))
                {
                    item.Rect.gameObject.SetActive(false);
                    screen[i] = new Vector2(-9999f, -9999f);
                    continue;
                }

                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, point, null, out var local);
                screen[i] = local;
            }

            // Üst üste binen işaretleri dikey olarak aç.
            for (int i = 0; i < used; i++)
            {
                if (entries[i].IsFloor || screen[i].x < -9000f) continue;
                for (int pass = 0; pass < 4; pass++)
                {
                    bool moved = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (entries[j].IsFloor || screen[j].x < -9000f) continue;
                        float dx = Mathf.Abs(screen[i].x - screen[j].x);
                        float dy = Mathf.Abs(screen[i].y - screen[j].y);
                        float needX = (pool[i].Width + pool[j].Width) * 0.5f + 4f;
                        if (dx < needX && dy < 27f)
                        {
                            screen[i].y = screen[j].y + 28f;
                            moved = true;
                        }
                    }
                    if (!moved) break;
                }
            }

            float pulse = 0.78f + 0.22f * Mathf.Sin(Time.unscaledTime * 6f);
            for (int i = 0; i < used; i++)
            {
                var item = pool[i];
                if (!item.Rect.gameObject.activeSelf) continue;

                var local = screen[i];
                local.x = Mathf.Clamp(local.x, -half.x + item.Width * 0.5f, half.x - item.Width * 0.5f);
                local.y = Mathf.Clamp(local.y, -half.y + 4f, half.y - 30f);
                item.Rect.anchoredPosition = local;

                if (entries[i].Kind == MarkerKind.Urgent)
                {
                    var color = item.Plate.color;
                    color.a = 0.94f * pulse;
                    item.Plate.color = color;
                }
            }
        }
    }
}
