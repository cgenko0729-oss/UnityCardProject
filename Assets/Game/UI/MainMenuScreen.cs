using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class MainMenuScreen : ScreenBase
    {
        public override bool ShowTopBar => false;

        protected override void Build()
        {
            var title = UIFactory.CreateText(Root, "Title", "卡 牌 构 筑", 88,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.7f));
            UIFactory.SetAnchored(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -320), new Vector2(0, -140));

            var subtitle = UIFactory.CreateText(Root, "Subtitle", "阶段 4 · 地图与奖励", 24,
                TextAnchor.MiddleCenter, new Color(0.65f, 0.7f, 0.8f));
            UIFactory.SetAnchored(subtitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -370), new Vector2(0, -320));

            var newRun = UIFactory.CreateTextButton(Root, "NewRun", "开始新游戏", 32,
                new Color(0.30f, 0.45f, 0.32f), () => App.StartNewRun());
            Center((RectTransform)newRun.transform, 0, 380, 100);

            var seedInfo = UIFactory.CreateText(Root, "SeedInfo",
                App.FixedSeed != 0 ? $"固定种子：{App.FixedSeed}" : "种子：每局随机", 20,
                TextAnchor.MiddleCenter, new Color(0.55f, 0.6f, 0.7f));
            UIFactory.SetAnchored(seedInfo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-260, -110), new Vector2(260, -70));

            var quit = UIFactory.CreateTextButton(Root, "Quit", "退出", 26,
                new Color(0.32f, 0.22f, 0.22f), Quit);
            Center((RectTransform)quit.transform, -190, 260, 70);

            var hint = UIFactory.CreateText(Root, "Hint",
                "地图上点亮的节点可以进入 ／ 战斗中：点牌选目标，空格结束回合", 20,
                TextAnchor.MiddleCenter, new Color(0.5f, 0.55f, 0.62f));
            UIFactory.SetAnchored(hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 40), new Vector2(0, 90));
        }

        private static void Center(RectTransform rt, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(0, y);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
