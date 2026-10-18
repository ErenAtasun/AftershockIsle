using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using AftershockIsle.UI;
using UnityEngine;

namespace AftershockIsle.Player
{
    /// <summary>
    /// Sığınak kontrolü. İki yol aynı anda çalışır:
    /// Doğrudan (klavye / gamepad, sahayla aynı): seçili karakter WASD / sol çubukla kat içinde X-Z'de serbest yürür
    /// (odanın içine / dışına), merdivenin ucunda merdivenin yönüne yürüyünce çıkar/iner, F / A en yakın nesneye iş verir, X / B durdurur,
    /// Tab / LB-RB karakter değiştirir (seferdeki kâşif de sıradadır; ona gelince saha görünümü açılır).
    /// Fare: sol tık seç, sağ tık nesneye iş ver (Shift ile sıraya ekle) ya da boşluğa yürü.
    /// Duraklatmada da emir verilebilir; karakter zaman akınca harekete geçer.
    /// </summary>
    public class CommandController : MonoBehaviour
    {
        const float InteractReach = 1.4f;
        const float StickDeadZone = 0.2f;
        const float InteractableRefresh = 0.5f;
        const float NoticeCooldown = 1.5f;

        [SerializeField] Camera cam;
        [SerializeField] BuildingLayout building;
        [SerializeField] float maxRayDistance = 200f;

        readonly RaycastHit[] hits = new RaycastHit[32];
        readonly List<Interactable> interactables = new List<Interactable>();
        readonly List<Survivor> roster = new List<Survivor>();
        int hitCount;
        float interactablesRefreshAt;
        bool steering;
        LadderTarget nearbyLadder;
        float nextNoticeAt;
        GUIStyle promptStyle;
        GUIStyle statusStyle;
        GUIStyle hintStyle;

        public Survivor Selected { get; private set; }
        public Interactable Hovered { get; private set; }

        /// <summary>F / A'nın şu an etkileşeceği nesne (seçili karakterin katında, en yakın).</summary>
        public Interactable Nearby { get; private set; }

        struct LadderTarget
        {
            public bool Valid;
            public int TargetFloor;
            public float X;
            // Merdivenin bu kattaki ucu (basamaklı merdivende alt uç önde, üst uç arkada).
            public float Z;
            // Merdivene girmek için yürünecek derinlik yönü (+1 odanın içine / arkaya, -1 öne).
            public float EntryDirection;
            public float Distance;
            public LadderDef Def;
        }

        // Merdiven ucuna bu kadar yakınken merdivenin yönüne yürümek çıkışı/inişi başlatır.
        const float StairEntryReach = 0.75f;
        // Girdinin ne kadarı merdivenin yönünde olmalı (yan yürürken yanlışlıkla binilmesin).
        const float StairEntryAlignment = 0.55f;
        // Merdiven ucu bu mesafeye kadar izlenir (istem ve binme için).
        const float StairReleaseDistance = 1.1f;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
        }

        // Oyun ilk karakter seçili başlar: klavye/gamepad ile hemen yürütülebilir.
        void Start() => SelectFirst();

        void Update()
        {
            hitCount = Physics.RaycastNonAlloc(PointerRay(), hits, maxRayDistance);
            Hovered = Closest<Interactable>();

            // Her zaman bir karakter seçili kalır: ölen ya da sefere çıkan (sahnede pasif) kazazedenin yerine sıradaki seçilir.
            if (Selected != null && (Selected.IsDead || !Selected.isActiveAndEnabled)) Select(null);
            if (Selected == null) SelectFirst();

            bool menuOpen = ModalMenu.IsOpen;
            // Yerleşim modunda LB/RB (ve Tab) kat / yapı seçer; karakter değiştirmez.
            if (!menuOpen && !PlacementController.IsPlacementActive) HandleCharacterSwitch();

            // Dış sahadayken ya da Grid Yerleştirme modundayken sığınaktaki seçim/emir askıda kalır.
            if (SideViewCamera.ViewingField || menuOpen || PlacementController.IsPlacementActive)
            {
                Hovered = null;
                Nearby = null;
                nearbyLadder = default;
                StopSteering();
                return;
            }

            UpdateDirectControl();

            if (UIBlocker.IsPointerOver(GameInput.PointerPosition))
            {
                Hovered = null;
                return;
            }

            if (GameInput.SelectPressed)
            {
                // Boşluğa tıklamak seçimi kaldırmaz; başka karaktere tıklamak (ya da Tab) ona geçer.
                var clicked = Closest<Survivor>();
                if (clicked != null && !clicked.IsDead) Select(clicked);
            }

            if (GameInput.CommandPressed && Selected != null) IssueMouseOrder();
        }

        public void Select(Survivor survivor)
        {
            if (survivor == Selected) return;

            StopSteering();
            if (Selected != null) Selected.SetSelected(false);
            Selected = survivor;
            if (Selected != null) Selected.SetSelected(true);

            EventBus.Publish(new SurvivorSelected(Selected));
        }

        // ---------------------------------------------------------------- Karakter değiştirme

        void HandleCharacterSwitch()
        {
            int step = GameInput.NextCharacterPressed ? 1 : GameInput.PrevCharacterPressed ? -1 : 0;
            if (step == 0) return;

            if (SideViewCamera.ViewingField)
            {
                SideViewCamera.ShowField(false);
                if (Selected == null) SelectFirst();
                return;
            }

            roster.Clear();
            foreach (var survivor in Survivor.All)
                if (!survivor.IsDead && survivor.isActiveAndEnabled) roster.Add(survivor);

            int index = Selected != null ? roster.IndexOf(Selected) : (step > 0 ? -1 : roster.Count);
            int next = index + step;

            // Listenin sonundan sonra seferdeki kâşif gelir: saha görünümü.
            if ((next >= roster.Count || next < 0) && SideViewCamera.HasField)
            {
                SideViewCamera.ShowField(true);
                return;
            }

            if (roster.Count == 0) return;
            Select(roster[(next % roster.Count + roster.Count) % roster.Count]);
        }

        void SelectFirst()
        {
            foreach (var survivor in Survivor.All)
            {
                if (survivor.IsDead || !survivor.isActiveAndEnabled) continue;
                Select(survivor);
                return;
            }
        }

        // ---------------------------------------------------------------- Doğrudan kontrol

        void UpdateDirectControl()
        {
            var survivor = Selected;
            FindNearby(survivor);
            if (survivor == null) return;

            if (GameInput.CancelOrderPressed)
            {
                survivor.Tasks.CancelAll();
                if (!survivor.Mover.IsClimbing) survivor.Mover.Halt();
            }

            if (GameInput.InteractPressed && CanCommand(survivor) && Nearby != null)
            {
                // F ile verilen iş durulan yerden yapılır (nesnenin çalışma noktasına, üstüne yürünmez).
                survivor.Tasks.PreferInPlace = true;
                Nearby.HandleCommand(survivor, GameInput.QueueModifierHeld);
                survivor.Tasks.PreferInPlace = false;
            }

            Vector2 move = GameInput.CharacterMove;

            // Merdivende oyuncu kendisi çıkar/iner: W yukarı, S aşağı; bırakınca karakter merdivende durur.
            if (survivor.Mover.IsManualClimbing)
            {
                steering = false;
                survivor.Mover.ClimbInput(Mathf.Abs(move.y) > StickDeadZone ? move.y : 0f);
                return;
            }

            if (TryWalkOntoStair(survivor, move)) return;

            if (move.sqrMagnitude > StickDeadZone * StickDeadZone)
            {
                if (!steering && !CanCommand(survivor)) return;
                if (!steering)
                {
                    survivor.Tasks.CancelAll();
                    steering = true;
                }
                survivor.Mover.Steer(move);
            }
            else
            {
                StopSteering();
            }
        }

        /// <summary>
        /// Merdivenin ucunda W (alt uçta) ya da S (üst uçta) basınca karakter merdivene biner; sonrasını oyuncu W/S ile
        /// kendisi sürer (CharacterMover.ClimbInput). F gerekmez.
        /// </summary>
        bool TryWalkOntoStair(Survivor survivor, Vector2 move)
        {
            if (!nearbyLadder.Valid || nearbyLadder.Distance > StairEntryReach) return false;
            if (move.sqrMagnitude <= StickDeadZone * StickDeadZone) return false;
            if (move.normalized.y * nearbyLadder.EntryDirection < StairEntryAlignment) return false;
            if (!CanCommand(survivor)) return false;
            if (FireSpot.IsSmokeBlocked(nearbyLadder.TargetFloor))
            {
                Notice(survivor, $"Duman yüzünden {building.GetFloorName(nearbyLadder.TargetFloor)} katına girilemiyor.");
                return false;
            }

            survivor.Tasks.CancelAll();
            StopSteering();
            survivor.Mover.BeginManualClimb(nearbyLadder.Def);
            survivor.Mover.ClimbInput(move.y);
            return true;
        }

        void StopSteering()
        {
            if (!steering) return;
            steering = false;
            if (Selected != null) Selected.Mover.Steer(Vector2.zero);
        }

        bool Climb(Survivor survivor, LadderTarget ladder)
        {
            if (FireSpot.IsSmokeBlocked(ladder.TargetFloor))
            {
                Notice(survivor, $"Duman yüzünden {building.GetFloorName(ladder.TargetFloor)} katına girilemiyor.");
                return false;
            }

            survivor.Tasks.CancelAll();
            survivor.Mover.MoveTo(ladder.TargetFloor, ladder.X);
            return true;
        }

        bool CanCommand(Survivor survivor)
        {
            if (survivor.Condition.IsPanicking)
            {
                Notice(survivor, "Panik halinde, emir dinlemiyor.");
                return false;
            }
            if (ExpeditionSystem.Instance != null && ExpeditionSystem.Instance.IsInTransit(survivor))
            {
                Notice(survivor, "Kapıdan geçiyor, emir veremezsin.");
                return false;
            }
            return true;
        }

        void Notice(Survivor survivor, string message)
        {
            if (Time.unscaledTime < nextNoticeAt) return;
            nextNoticeAt = Time.unscaledTime + NoticeCooldown;
            EventBus.Publish(new TaskNotice(survivor, message));
        }

        // F / A'nın hedefi: karakterin katında, ona en yakın (X-Z mesafesi) nesne ya da merdiven.
        void FindNearby(Survivor survivor)
        {
            Nearby = null;
            nearbyLadder = default;
            if (survivor == null) return;

            if (Time.unscaledTime >= interactablesRefreshAt)
            {
                interactablesRefreshAt = Time.unscaledTime + InteractableRefresh;
                interactables.Clear();
                interactables.AddRange(FindObjectsByType<Interactable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            }

            int floor = survivor.Mover.CurrentFloor;
            var here = new Vector2(survivor.transform.position.x, survivor.transform.position.z);
            float bestDistance = InteractReach;

            foreach (var candidate in interactables)
            {
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.Floor != floor || !candidate.IsInteractive) continue;

                float distance = FootprintDistance(candidate, here);
                if (distance >= bestDistance) continue;
                Nearby = candidate;
                bestDistance = distance;
            }

            // Merdivenler F ile değil yürüyerek kullanılır: nesnelerle yarışmaz, en yakın ucu ayrıca tutulur.
            float bestLadder = StairReleaseDistance + 0.5f;
            foreach (var raw in building.Ladders)
            {
                var ladder = building.WithProfile(raw);
                int target;
                if (ladder.lowerFloor == floor) target = floor + 1;
                else if (ladder.lowerFloor == floor - 1) target = floor - 1;
                else continue;

                float x = building.transform.position.x + ladder.x;
                float z = building.LadderStandZ(ladder, floor);
                float distance = Vector2.Distance(here, new Vector2(x, z));
                if (distance >= bestLadder) continue;

                // W yukarı, S aşağı: alt uçta W ile binilir, üst uçta S ile.
                float entry = target > floor ? 1f : -1f;
                nearbyLadder = new LadderTarget { Valid = true, TargetFloor = target, X = x, Z = z, EntryDirection = entry, Distance = distance, Def = ladder };
                bestLadder = distance;
            }
        }

        // Nesnenin taban dikdörtgenine (yoksa merkezine) X-Z mesafesi: büyük mobilyaya her yüzünden yaklaşılabilir.
        static float FootprintDistance(Interactable candidate, Vector2 point)
        {
            if (!candidate.TryGetFootprint(out var bounds))
            {
                var position = candidate.transform.position;
                return Vector2.Distance(point, new Vector2(position.x, position.z));
            }

            var closest = new Vector2(
                Mathf.Clamp(point.x, bounds.min.x, bounds.max.x),
                Mathf.Clamp(point.y, bounds.min.z, bounds.max.z));
            return Vector2.Distance(point, closest);
        }

        // ---------------------------------------------------------------- Fare

        void IssueMouseOrder()
        {
            if (!CanCommand(Selected)) return;

            if (Hovered != null)
            {
                Hovered.HandleCommand(Selected, GameInput.QueueModifierHeld);
                return;
            }

            var ray = PointerRay();
            if (Mathf.Abs(ray.direction.z) < 0.0001f) return;

            // Kat, yürüme düzlemiyle kesişimden; nokta, o katın zemininden (odanın derinliğinde tıklanan yer).
            float t = (building.WalkPlaneZ - ray.origin.z) / ray.direction.z;
            if (t < 0f) return;

            Vector3 point = ray.GetPoint(t);
            int floor = building.FloorAtHeight(point.y);
            float? z = null;
            if (ray.direction.y < -0.0001f)
            {
                float floorT = (building.FloorY(floor) - ray.origin.y) / ray.direction.y;
                var onFloor = ray.GetPoint(floorT);
                if (floorT > 0f && onFloor.z >= building.MinZ - 1f && onFloor.z <= building.MaxZ + 1f)
                {
                    point.x = onFloor.x;
                    z = onFloor.z;
                }
            }

            if (FireSpot.IsSmokeBlocked(floor) && floor != Selected.Mover.CurrentFloor)
            {
                EventBus.Publish(new TaskNotice(Selected, $"Duman yüzünden {building.GetFloorName(floor)} katına girilemiyor."));
                return;
            }

            Selected.Tasks.CancelAll();
            Selected.Mover.MoveTo(floor, point.x, z);
        }

        Ray PointerRay() => cam.ScreenPointToRay(GameInput.PointerPosition);

        // Bina geometrisi de collider taşıdığı için ilk isabet yerine aranan türün en yakınını seç.
        T Closest<T>() where T : Component
        {
            T closest = null;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                var candidate = hits[i].collider.GetComponentInParent<T>();
                if (candidate == null || hits[i].distance >= closestDistance) continue;
                // Şu an etkileşilecek bir şey olmayan nesneler (yanmayan yangın odağı gibi) hayalet gibi seçilmesin.
                if (candidate is Interactable thing && !thing.IsInteractive) continue;

                closest = candidate;
                closestDistance = hits[i].distance;
            }

            return closest;
        }

        // ---------------------------------------------------------------- Ekran ipuçları

        void OnGUI()
        {
            UiTheme.Apply();
            if (SideViewCamera.ViewingField || ModalMenu.IsOpen || cam == null) return;

            promptStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            statusStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperCenter };
            hintStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

            if (Selected != null && Nearby != null) DrawNearbyPrompt();
            else if (Selected != null && nearbyLadder.Valid && nearbyLadder.Distance <= StairEntryReach && !Selected.Mover.IsManualClimbing)
                DrawLadderPrompt();
        }

        void DrawNearbyPrompt()
        {
            var screen = cam.WorldToScreenPoint(Nearby.transform.position + Vector3.up * 1.4f);
            if (screen.z < 0f) return;

            var at = UiTheme.ToGui(screen);
            float x = at.x;
            float y = at.y;
            string text = $"[{GameInput.Prompt(InputPrompt.Interact)}] {Nearby.ActionLabel}: {Nearby.DisplayName}";
            float width = Mathf.Max(160f, promptStyle.CalcSize(new GUIContent(text)).x + 16f);
            GUI.Box(new Rect(x - width * 0.5f, y - 28f, width, 26f), text, promptStyle);

            // Ayrıntılar (neden, getiri, süre, engel) bilgi kartında (InfoCard) gösterilir.
        }

        void DrawLadderPrompt()
        {
            var anchor = new Vector3(nearbyLadder.X, Selected.transform.position.y + 2.2f, nearbyLadder.Z);
            var screen = cam.WorldToScreenPoint(anchor);
            if (screen.z < 0f) return;

            string direction = nearbyLadder.TargetFloor > Selected.Mover.CurrentFloor ? "Yukarı çık" : "Aşağı in";
            // F yok: merdivenin yönüne yürümek yeter (ok o yönü gösterir).
            string arrow = nearbyLadder.EntryDirection > 0f ? "▲" : "▼";
            string key = nearbyLadder.EntryDirection > 0f ? "W" : "S";
            string text = $"{arrow} [{key}] {direction}: {building.GetFloorName(nearbyLadder.TargetFloor)}";
            float width = Mathf.Max(160f, promptStyle.CalcSize(new GUIContent(text)).x + 16f);
            var at = UiTheme.ToGui(screen);
            GUI.Box(new Rect(at.x - width * 0.5f, at.y - 13f, width, 26f), text, promptStyle);
        }
    }
}
