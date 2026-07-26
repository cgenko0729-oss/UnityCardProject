using System.Collections.Generic;
using Game.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class MainMenuScreen : ScreenBase
    {
        public override bool ShowTopBar => false;

        protected override void Build()
        {
            var title = UIFactory.CreateText(Root, "Title", Loc.T("ui.menu.title", "卡 牌 构 筑"), 88,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.7f));
            UIFactory.SetAnchored(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -320), new Vector2(0, -140));

            var subtitle = UIFactory.CreateText(Root, "Subtitle", Loc.T("ui.menu.subtitle", "阶段 4 · 地图与奖励"), 24,
                TextAnchor.MiddleCenter, new Color(0.65f, 0.7f, 0.8f));
            UIFactory.SetAnchored(subtitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -370), new Vector2(0, -320));

            var newRun = UIFactory.CreateTextButton(Root, "NewRun", Loc.T("ui.menu.new_run", "开始新游戏"), 32,
                new Color(0.30f, 0.45f, 0.32f), () => App.StartNewRun());
            Center((RectTransform)newRun.transform, 0, 380, 100);

            var seedInfo = UIFactory.CreateText(Root, "SeedInfo",
                App.FixedSeed != 0
                    ? Loc.T("ui.menu.seed_fixed", "固定种子：{0}", App.FixedSeed)
                    : Loc.T("ui.menu.seed_random", "种子：每局随机"), 20,
                TextAnchor.MiddleCenter, new Color(0.55f, 0.6f, 0.7f));
            UIFactory.SetAnchored(seedInfo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-260, -110), new Vector2(260, -70));

            BuildLanguageRow();

            var quit = UIFactory.CreateTextButton(Root, "Quit", Loc.T("ui.menu.quit", "退出"), 26,
                new Color(0.32f, 0.22f, 0.22f), Quit);
            Center((RectTransform)quit.transform, -280, 260, 70);

            var hint = UIFactory.CreateText(Root, "Hint",
                Loc.T("ui.menu.hint", "地图上点亮的节点可以进入 ／ 战斗中：点牌选目标，空格结束回合"), 20,
                TextAnchor.MiddleCenter, new Color(0.5f, 0.55f, 0.62f));
            UIFactory.SetAnchored(hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 40), new Vector2(0, 90));
        }

        /// <summary>
        /// 语言切换行。
        ///
        /// ★ 只放在主菜单，不做成随处可开的设置面板：切语言会把整棵界面重建一遍
        ///   （<see cref="GameApp.OnLanguageChanged"/>），在战斗里那么做要额外考虑
        ///   拖拽状态、挂起中的选牌面板、正在播的表现事件——收益不值那个复杂度。
        ///
        /// ★ 每种语言的按钮用它<b>自己的语言</b>写自己的名字（English / 日本語），
        ///   而不是用当前语言去描述它。看不懂当前语言的人才最需要这个按钮。
        /// </summary>
        private void BuildLanguageRow()
        {
            var langs = new List<(string Code, string Label)>
            {
                (Loc.SourceLanguage, "简体中文"),
            };

            var db = App.Database;
            if (db != null)
            {
                for (int i = 0; i < db.Locales.Count; i++)
                {
                    var t = db.Locales[i];
                    if (t == null || string.IsNullOrEmpty(t.LanguageCode)) continue;
                    langs.Add((t.LanguageCode, string.IsNullOrEmpty(t.DisplayName) ? t.LanguageCode : t.DisplayName));
                }
            }

            // 只有源语言时整行不建——一个语言的「语言选择」是纯噪音
            if (langs.Count <= 1) return;

            const float ButtonWidth = 150f;
            const float Spacing = 12f;
            float totalWidth = langs.Count * ButtonWidth + (langs.Count - 1) * Spacing;
            float x = -totalWidth * 0.5f + ButtonWidth * 0.5f;

            for (int i = 0; i < langs.Count; i++)
            {
                var (code, label) = langs[i];
                bool active = code == Loc.Current;

                var btn = UIFactory.CreateTextButton(Root, "Lang_" + code, label, 22,
                    active ? new Color(0.34f, 0.40f, 0.52f) : new Color(0.22f, 0.24f, 0.28f),
                    () => App.SetLanguage(code));

                var rt = (RectTransform)btn.transform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(ButtonWidth, 56);
                rt.anchoredPosition = new Vector2(x + i * (ButtonWidth + Spacing), -180);

                // 当前语言那颗按不动——按下去只会重建一遍界面，什么也不变
                if (active) btn.interactable = false;
            }
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
