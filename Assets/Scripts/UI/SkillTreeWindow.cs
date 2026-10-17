using System.Collections.Generic;
using AftershockIsle.Core;
using AftershockIsle.Skills;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// K / D-pad sağ ile açılan yetenek ağacı (uGUI): üç dal yan yana, her yetenek bir kart. Kart rengi durumu söyler
    /// (açık / açılabilir / kilitli); karta basmak yeteneği açar. Duraklatmada da kullanılabilir.
    /// </summary>
    public class SkillTreeWindow : MonoBehaviour
    {
        const float RefreshSeconds = 0.25f;
        const float ColumnWidth = 330f;
        const float CardHeight = 150f;

        static readonly Color Unlocked = new Color(0.22f, 0.42f, 0.24f, 1f);
        static readonly Color Available = new Color(0.42f, 0.36f, 0.14f, 1f);
        static readonly Color Locked = new Color(0.2f, 0.21f, 0.24f, 1f);

        class Card
        {
            public SkillInfo Skill;
            public Button Button;
            public Image Plate;
            public Text Title;
            public Text State;
        }

        UiWindow window;
        readonly List<Card> cards = new List<Card>();
        readonly Dictionary<SkillBranch, Text> branchLabels = new Dictionary<SkillBranch, Text>();
        float refreshAt;

        void Update()
        {
            if (GameInput.SkillTreeTogglePressed && (IsOpen || !ModalMenu.IsOpen))
            {
                if (IsOpen) Close();
                else Open();
            }
            if (!IsOpen) return;

            window.Tick(cards.Count > 0 ? cards[0].Button.gameObject : null);
            if (GameInput.MenuBackPressed) Close();
            else if (Time.unscaledTime >= refreshAt) Refresh();
        }

        bool IsOpen => window != null && window.IsOpen;

        void Open()
        {
            if (SkillTree.Instance == null) return;
            if (window == null) Build();
            window.Show(true);
            Refresh();
            if (EventSystem.current != null && cards.Count > 0) EventSystem.current.SetSelectedGameObject(cards[0].Button.gameObject);
        }

        void Close()
        {
            if (window != null) window.Show(false);
        }

        void Build()
        {
            window = new UiWindow("Yetenek Ağacı", new Vector2(ColumnWidth * 3f + 80f, 680f), Close, false);
            window.FooterButton($"Kapat ({GameInput.Prompt(InputPrompt.SkillTree)} / {GameInput.Prompt(InputPrompt.Back)})", Close, null, 260f);

            var note = UiFactory.Label(window.Footer, "Her yeni gün +1 puan. Bir dal, o dalın uzmanı sığınaktayken açılabilir.", 15,
                new Color(0.78f, 0.78f, 0.74f), TextAnchor.MiddleLeft, FontStyle.Normal, false);
            note.rectTransform.sizeDelta = new Vector2(700f, 46f);
            note.transform.SetAsFirstSibling();

            int column = 0;
            foreach (SkillBranch branch in System.Enum.GetValues(typeof(SkillBranch)))
            {
                float x = 10f + column * (ColumnWidth + 12f);
                var header = UiFactory.Label(window.Content, "", 20, UiFactory.Accent, TextAnchor.MiddleLeft);
                UiFactory.Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(x, -4f), new Vector2(ColumnWidth, 30f));
                branchLabels[branch] = header;

                int row = 0;
                foreach (var skill in SkillCatalog.All)
                {
                    if (skill.Branch != branch) continue;
                    cards.Add(BuildCard(skill, new Vector2(x, -42f - row * (CardHeight + 10f))));
                    row++;
                }
                column++;
            }
        }

        Card BuildCard(SkillInfo skill, Vector2 position)
        {
            var tree = SkillTree.Instance;
            var button = UiFactory.TextButton(window.Content, "", new Vector2(ColumnWidth, CardHeight), () =>
            {
                tree.TryUnlock(skill.Id);
                Refresh();
            }, Locked);
            var rect = (RectTransform)button.transform;
            UiFactory.Place(rect, new Vector2(0f, 1f), position, new Vector2(ColumnWidth, CardHeight));

            // TextButton'ın ortalanmış yazısı yerine kart düzeni.
            Object.Destroy(button.GetComponentInChildren<Text>().gameObject);

            var title = UiFactory.Label(rect, "", 18, UiFactory.Text, TextAnchor.UpperLeft);
            UiFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(ColumnWidth - 28f, 24f));
            var description = UiFactory.Label(rect, skill.Description, 14, new Color(0.85f, 0.85f, 0.8f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(description.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -38f), new Vector2(ColumnWidth - 28f, 72f));
            var state = UiFactory.Label(rect, "", 14, UiFactory.Text, TextAnchor.LowerLeft, FontStyle.Bold, false);
            UiFactory.Place(state.rectTransform, new Vector2(0f, 0f), new Vector2(14f, 8f), new Vector2(ColumnWidth - 28f, 22f));

            return new Card { Skill = skill, Button = button, Plate = button.GetComponent<Image>(), Title = title, State = state };
        }

        void Refresh()
        {
            refreshAt = Time.unscaledTime + RefreshSeconds;
            var tree = SkillTree.Instance;
            if (tree == null) return;

            window.Title.text = $"YETENEK AĞACI   <size=18><color=#cfcfc8>puan: {tree.Points}</color></size>";

            foreach (var pair in branchLabels)
                pair.Value.text = SkillTree.IsBranchAvailable(pair.Key)
                    ? $"{pair.Key.ToDisplayName()} Dalı"
                    : $"{pair.Key.ToDisplayName()} Dalı  <size=14><color=#9a9a94>(uzman yok)</color></size>";

            foreach (var card in cards)
            {
                bool unlocked = tree.IsUnlocked(card.Skill.Id);
                bool canUnlock = tree.CanUnlock(card.Skill.Id, out string reason);

                card.Title.text = $"{card.Skill.Tier}. {card.Skill.Name}";
                card.Plate.color = unlocked ? Unlocked : canUnlock ? Available : Locked;
                card.State.text = unlocked ? "<color=#9be37a>AÇIK</color>"
                    : canUnlock ? "<color=#ffd75a>Açmak için bas (1 puan)</color>"
                    : $"<color=#9a9a94>{reason}</color>";
            }
        }
    }
}
