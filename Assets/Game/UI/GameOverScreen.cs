using System.Text;
using Game.Core;
using UnityEngine;

namespace Game.UI
{
    /// <summary>通关 / 失败结算界面。</summary>
    public class GameOverScreen : ScreenBase
    {
        public override bool ShowTopBar => false;

        protected override void Build()
        {
            bool victory = Run != null && Run.Phase == RunPhase.Victory;

            var title = UIFactory.CreateText(Root, "Title",
                victory ? "通　关" : "征程结束", 72, TextAnchor.MiddleCenter,
                victory ? new Color(1f, 0.9f, 0.45f) : new Color(0.9f, 0.42f, 0.42f));
            UIFactory.SetAnchored(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -300), new Vector2(0, -160));

            var summary = UIFactory.CreateText(Root, "Summary", BuildSummary(victory), 26,
                TextAnchor.UpperCenter, new Color(0.85f, 0.88f, 0.93f));
            UIFactory.SetAnchored(summary.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-420, -120), new Vector2(420, 160));

            var again = UIFactory.CreateTextButton(Root, "Again", "再来一局", 30,
                new Color(0.30f, 0.44f, 0.32f), () => App.StartNewRun());
            Place((RectTransform)again.transform, -230);

            var menu = UIFactory.CreateTextButton(Root, "Menu", "回到主菜单", 26,
                new Color(0.30f, 0.34f, 0.40f), () => Manager.GoToMainMenu());
            Place((RectTransform)menu.transform, -330);
        }

        private static void Place(RectTransform rt, float y)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(360, 84);
            rt.anchoredPosition = new Vector2(0, y);
        }

        private string BuildSummary(bool victory)
        {
            if (Run == null) return "";

            var sb = new StringBuilder(256);
            sb.AppendLine(victory ? "你击败了最深处的首领。" : "你倒在了半路上。").AppendLine();

            int floor = Run.Map != null && Run.CurrentNodeId >= 0
                ? Run.Map.GetNode(Run.CurrentNodeId).Row + 1 : 0;
            sb.AppendLine($"抵达层数：{floor} / {(Run.Map != null ? Run.Map.RowCount : 0)}");
            sb.AppendLine($"击败战斗：{Run.BattlesWon} 场");
            sb.AppendLine($"最终生命：{Run.Hp} / {Run.MaxHp}");
            sb.AppendLine($"剩余金币：{Run.Gold}");
            sb.AppendLine($"牌库张数：{Run.Deck.Count}");
            sb.AppendLine($"持有遗物：{Run.Relics.Count} 个");
            sb.AppendLine().Append($"种子：{Run.Seed}");

            return sb.ToString();
        }
    }
}
