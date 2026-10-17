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
    /// (odanın içine / dışına), F / A en yakın nesneye iş verir ya da merdivenden çıkar/iner, X / B durdurur,
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

        /// <summary>F / A'nın şu an etkileşeceği nesne (seçili karakterin katında, en yakın). Merdiven daha yakınsa null.</summary>
        public Interactable Nearby { get; private set; }

        struct LadderTarget
        {
            public bool Valid;
            public int TargetFloor;
            public float X;
        }

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

            // Ölen ya da sefere çıkan (sahnede pasif) kazazedenin seçimi kalkar.
            if (Selected != null && (Selected.IsDead || !Selected.isActiveAndEnabled)) Select(null);

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
                var clicked = Closest<Survivor>();
                Select(clicked != null && !clicked.IsDead ? clicked : null);
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

            if (GameInput.InteractPressed && CanCommand(survivor))
            {
                if (Nearby != null) Nearby.HandleCommand(survivor, GameInput.QueueModifierHeld);
                else if (nearbyLadder.Valid) Climb(survivor, nearbyLadder);
            }

            Vector2 move = GameInput.CharacterMove;
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

        void StopSteering()
        {
            if (!steering) return;
            steering = false;
            if (Selected != null) Selected.Mover.Steer(Vector2.zero);
        }

        void Climb(Survivor survivor, LadderTarget ladder)
        {
            if (FireSpot.IsSmokeBlocked(ladder.TargetFloor))
            {
                Notice(survivor, $"Duman yüzünden {building.GetFloorName(ladder.TargetFloor)} katına girilemiyor.");
                return;
            }

            survivor.Tasks.CancelAll();
            survivor.Mover.MoveTo(ladder.TargetFloor, ladder.X);
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
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.Floor != floor) continue;

                float distance = FootprintDistance(candidate, here);
                if (distance >= bestDistance) continue;
                Nearby = candidate;
                bestDistance = distance;
            }

            foreach (var ladder in building.Ladders)
            {
                int target;
                if (ladder.lowerFloor == floor) target = floor + 1;
                else if (ladder.lowerFloor == floor - 1) target = floor - 1;
                else continue;

                float x = building.transform.position.x + ladder.x;
                float distance = Vector2.Distance(here, new Vector2(x, building.LadderZ));
                if (distance >= bestDistance) continue;

                Nearby = null;
                nearbyLadder = new LadderTarget { Valid = true, TargetFloor = target, X = x };
                bestDistance = distance;
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
            else if (Selected != null && nearbyLadder.Valid) DrawLadderPrompt();
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

            string status = Nearby.StatusText;
            if (string.IsNullOrEmpty(status)) return;
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 130f, y, 260f, 34f), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(x - 128f, y + 1f, 256f, 34f), status, statusStyle);
        }

        void DrawLadderPrompt()
        {
            var anchor = new Vector3(nearbyLadder.X, Selected.transform.position.y + 2.2f, building.LadderZ);
            var screen = cam.WorldToScreenPoint(anchor);
            if (screen.z < 0f) return;

            string direction = nearbyLadder.TargetFloor > Selected.Mover.CurrentFloor ? "Yukarı çık" : "Aşağı in";
            string text = $"[{GameInput.Prompt(InputPrompt.Interact)}] {direction}: {building.GetFloorName(nearbyLadder.TargetFloor)}";
            float width = Mathf.Max(160f, promptStyle.CalcSize(new GUIContent(text)).x + 16f);
            var at = UiTheme.ToGui(screen);
            GUI.Box(new Rect(at.x - width * 0.5f, at.y - 13f, width, 26f), text, promptStyle);
        }
    }
}
