using System.Text;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Interaction;
using AftershockIsle.Player;
using AftershockIsle.Skills;
using UnityEngine;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Bilgi kartı (uGUI): fareyle üstüne gelinen ya da yakınındaki (F / A ile etkileşilecek) nesnenin adı, ne işe yaradığı,
    /// durumu, süresi (alet etkisiyle), kazancı ve şu an yapılamıyorsa nedeni. Yanmayan yangın odağı gibi etkileşilecek bir şey
    /// olmayan nesneler için kart açılmaz (Interactable.IsInteractive). Fare ucundaki kart imleci izler; yakın nesne kartı sol altta durur.
    /// </summary>
    public class InfoCard : MonoBehaviour
    {
        const float RefreshSeconds = 0.2f;
        const float NearbyVisibleSeconds = 4f;
        const float NearbyCardSeconds = 3.5f;
        const float Width = 420f;

        CommandController commands;
        RectTransform canvasRect;
        RectTransform panel;
        Text body;

        Interactable shown;
        bool shownHovered;
        Interactable lastNearby;
        float nearbyShownAt;
        float refreshAt;
        float lastActivity = -100f;
        readonly StringBuilder text = new StringBuilder();

        void Start()
        {
            commands = FindFirstObjectByType<CommandController>();
            var canvas = UiFactory.Canvas("[Bilgi Kartı]", 6, transform);
            canvasRect = (RectTransform)canvas.transform;

            var plate = UiFactory.Image(canvas.transform, "Kart", null, new Color(0.08f, 0.09f, 0.12f, 0.94f));
            plate.raycastTarget = false;
            panel = plate.rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 1f);

            body = UiFactory.Label(panel, "", 17, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            var rect = body.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(14f, -10f);
            rect.sizeDelta = new Vector2(Width - 28f, 100f);

            panel.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (panel == null) return;

            var selected = commands != null ? commands.Selected : null;
            if (selected != null && (selected.Mover.IsSteering || GameInput.InteractPressed)) lastActivity = Time.unscaledTime;

            bool blocked = SideViewCamera.ViewingField || ModalMenu.IsOpen || PauseMenu.IsOpen || PlacementController.IsPlacementActive ||
                           commands == null;
            Interactable subject = null;
            bool hovered = false;
            if (!blocked)
            {
                if (commands.Hovered != null && commands.Hovered.IsInteractive)
                {
                    subject = commands.Hovered;
                    hovered = true;
                }
                else if (selected != null && commands.Nearby != null && commands.Nearby.IsInteractive &&
                         (GameInput.UsingGamepad || Time.unscaledTime - lastActivity < NearbyVisibleSeconds))
                {
                    subject = commands.Nearby;
                }
            }

            // Yakındaki nesnenin kartı kısa süre gösterilir, sonra kendiliğinden kapanır (ekranda sürekli açık kalmasın).
            // Gamepad'de ve fareyle üstüne gelindiğinde süre sınırı yoktur.
            var nearbyNow = commands != null ? commands.Nearby : null;
            if (nearbyNow == null) lastNearby = null;
            if (subject != null && !hovered && !GameInput.UsingGamepad)
            {
                if (subject != lastNearby)
                {
                    lastNearby = subject;
                    nearbyShownAt = Time.unscaledTime;
                }
                else if (Time.unscaledTime - nearbyShownAt > NearbyCardSeconds) subject = null;
            }

            if (subject == null)
            {
                if (panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
                shown = null;
                return;
            }

            if (subject != shown || hovered != shownHovered || Time.unscaledTime >= refreshAt)
            {
                shown = subject;
                shownHovered = hovered;
                refreshAt = Time.unscaledTime + RefreshSeconds;
                body.text = Describe(subject, selected, hovered);
                panel.sizeDelta = new Vector2(Width, body.preferredHeight + 22f);
            }

            if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
            Place(hovered);
        }

        void Place(bool followPointer)
        {
            var half = canvasRect.rect.size * 0.5f;
            var size = panel.sizeDelta;
            Vector2 position;

            if (followPointer)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, GameInput.PointerPosition, null, out var local);
                position = local + new Vector2(22f, -18f);
                // Sağa taşarsa imlecin soluna, alta taşarsa yukarı al.
                if (position.x + size.x > half.x - 8f) position.x = local.x - size.x - 22f;
                if (position.y - size.y < -half.y + 8f) position.y = local.y + size.y + 18f;
            }
            else
            {
                position = new Vector2(-half.x + 14f, -half.y + 176f + size.y);
            }

            position.x = Mathf.Clamp(position.x, -half.x + 8f, half.x - size.x - 8f);
            position.y = Mathf.Clamp(position.y, -half.y + size.y + 8f, half.y - 8f);
            panel.anchoredPosition = position;
        }

        string Describe(Interactable thing, Survivor selected, bool pointer)
        {
            var guide = InteractableGuide.Of(thing);
            string titleColor = guide.Kind == MarkerKind.Urgent ? "#ff8f7a" : guide.Kind == MarkerKind.Important ? "#ffd75a" : "#ffcc33";

            text.Clear();
            text.Append($"<size=21><b><color={titleColor}>{thing.DisplayName}</color></b></size>");

            string key = pointer ? "Sağ tık" : GameInput.Prompt(InputPrompt.Interact);
            text.Append($"\n<color=#ffd75a>{key}: {thing.ActionLabel}</color>");

            if (!string.IsNullOrEmpty(guide.Why)) text.Append($"\n<color=#d9d9d2>{guide.Why}</color>");

            string status = thing.StatusText;
            if (!string.IsNullOrEmpty(status)) text.Append($"\n{status}");

            if (!string.IsNullOrEmpty(guide.Reward)) text.Append($"\n<color=#9be37a>Kazanç: {guide.Reward}</color>");

            if (guide.ShowDuration && selected != null)
            {
                var category = thing.Category;
                float multiplier = ToolKit.WorkSpeed(category) * SkillTree.WorkSpeedBonus(category) *
                                   TraitCatalog.GetWorkSpeedMultiplier(selected, category) * selected.Condition.SpeedMultiplier;
                float seconds = thing.WorkDuration;
                float effective = multiplier > 0.01f ? seconds / multiplier : seconds;
                text.Append(Mathf.Abs(effective - seconds) > seconds * 0.05f
                    ? $"\nSüre: {seconds:0.#} sn  →  <color=#9be37a>{effective:0.#} sn</color> ({selected.DisplayName})"
                    : $"\nSüre: {seconds:0.#} sn");

                string tool = ToolKit.Hint(category);
                if (!string.IsNullOrEmpty(tool)) text.Append($"\n<color=#cfe3a0>Alet: {tool}</color>");

                if (!thing.CanStart(selected, out string reason) && !string.IsNullOrEmpty(reason))
                    text.Append($"\n<color=#ff8f7a>Şu an yapılamaz: {reason}</color>");
            }

            return text.ToString();
        }
    }
}
