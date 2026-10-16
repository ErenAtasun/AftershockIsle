using AftershockIsle.Core;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>K ile açılan geçici IMGUI yetenek ağacı penceresi. Duraklatmada da kullanılabilir.</summary>
    public class SkillTreeWindow : MonoBehaviour
    {
        const int WindowId = 4201;
        const float ColumnWidth = 220f;

        bool open;
        Rect windowRect = new Rect(0, 0, ColumnWidth * 3 + 30, 360);
        GUIStyle wrapStyle;

        void Update()
        {
            if (GameInput.SkillTreeTogglePressed) open = !open;
        }

        void OnGUI()
        {
            if (!open || SkillTree.Instance == null) return;

            wrapStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            windowRect.x = (Screen.width - windowRect.width) * 0.5f;
            windowRect.y = (Screen.height - windowRect.height) * 0.5f;
            UIBlocker.Add(windowRect);

            GUI.Window(WindowId, windowRect, DrawWindow, $"Yetenek Ağacı — puan: {SkillTree.Instance.Points}   (K: kapat)");
        }

        void DrawWindow(int id)
        {
            var tree = SkillTree.Instance;
            GUILayout.BeginHorizontal();

            foreach (SkillBranch branch in System.Enum.GetValues(typeof(SkillBranch)))
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(ColumnWidth));
                bool available = SkillTree.IsBranchAvailable(branch);
                GUILayout.Label($"{branch.ToDisplayName()} Dalı{(available ? "" : "  (uzman yok)")}");

                foreach (var skill in SkillCatalog.All)
                {
                    if (skill.Branch != branch) continue;
                    DrawSkill(tree, skill);
                }

                GUILayout.EndVertical();
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("Her yeni gün +1 puan. Bir dal, o dalın uzmanı bir kazazede sığınaktayken açılabilir.", wrapStyle);
        }

        void DrawSkill(SkillTree tree, SkillInfo skill)
        {
            bool unlocked = tree.IsUnlocked(skill.Id);
            bool canUnlock = tree.CanUnlock(skill.Id, out string reason);

            GUI.enabled = canUnlock;
            string label = unlocked ? $"[{skill.Tier}] {skill.Name}  (AÇIK)" : $"[{skill.Tier}] {skill.Name}";
            if (GUILayout.Button(label)) tree.TryUnlock(skill.Id);
            GUI.enabled = true;

            GUILayout.Label(skill.Description, wrapStyle);
            if (!unlocked && !canUnlock) GUILayout.Label($"→ {reason}", wrapStyle);
            GUILayout.Space(4);
        }
    }
}
