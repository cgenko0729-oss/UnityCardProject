using System.Collections.Generic;
using Game.Core;
using Game.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 顶部常驻状态栏：生命 / 金币 / 层数 / 遗物。
    /// 由 GameApp 持有，界面切换时不重建，避免每次切界面都闪一下。
    /// </summary>
    public class TopBarView : MonoBehaviour
    {
        private GameApp _app;
        private RectTransform _root;
        private TMP_Text _hpText;
        private TMP_Text _goldText;
        private TMP_Text _floorText;
        private RectTransform _relicRow;

        private readonly List<string> _shownRelics = new List<string>();

        public static TopBarView Create(RectTransform parent, GameApp app)
        {
            var rt = UIFactory.CreatePanel(parent, "TopBar", new Color(0f, 0f, 0f, 0.45f));
            UIFactory.SetAnchored(rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -56), Vector2.zero);

            var view = rt.gameObject.AddComponent<TopBarView>();
            view._app = app;
            view._root = rt;

            view._hpText = UIFactory.CreateText(rt, "Hp", "", 24, TextAnchor.MiddleLeft, new Color(1f, 0.5f, 0.5f));
            UIFactory.SetAnchored(view._hpText.rectTransform, new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(24, 0), new Vector2(220, 0));

            view._goldText = UIFactory.CreateText(rt, "Gold", "", 24, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.35f));
            UIFactory.SetAnchored(view._goldText.rectTransform, new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(230, 0), new Vector2(390, 0));

            view._floorText = UIFactory.CreateText(rt, "Floor", "", 22, TextAnchor.MiddleRight,
                new Color(0.8f, 0.85f, 0.95f));
            UIFactory.SetAnchored(view._floorText.rectTransform, new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(-260, 0), new Vector2(-24, 0));

            view._relicRow = UIFactory.CreateHorizontalGroup(rt, "Relics", 6f);
            UIFactory.SetAnchored(view._relicRow, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(400, 6), new Vector2(-270, -6));

            return view;
        }

        public void Refresh(RunContext run, bool visible)
        {
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible || run == null) return;

            _hpText.text = Loc.T("ui.topbar.hp", "♥ {0} / {1}", run.Hp, run.MaxHp);
            _goldText.text = $"◆ {run.Gold}";

            int floor = run.Map != null && run.CurrentNodeId >= 0
                ? run.Map.GetNode(run.CurrentNodeId).Row + 1
                : 0;
            int total = run.Map != null ? run.Map.RowCount : 0;
            _floorText.text = total > 0 ? Loc.T("ui.topbar.floor", "第 {0} / {1} 层", floor, total) : "";

            RefreshRelics(run);
        }

        private void RefreshRelics(RunContext run)
        {
            // 只在遗物列表真的变了的时候重建，否则每帧销毁重建会吃掉一大块 CPU
            bool changed = run.Relics.Count != _shownRelics.Count;
            if (!changed)
            {
                for (int i = 0; i < run.Relics.Count; i++)
                    if (run.Relics[i].Id != _shownRelics[i]) { changed = true; break; }
            }
            if (!changed) return;

            for (int i = _relicRow.childCount - 1; i >= 0; i--)
                Destroy(_relicRow.GetChild(i).gameObject);

            _shownRelics.Clear();
            for (int i = 0; i < run.Relics.Count; i++)
            {
                var relic = run.Relics[i];
                _shownRelics.Add(relic.Id);

                var chip = UIFactory.CreatePanel(_relicRow, "Relic_" + relic.Id, new Color(0.35f, 0.30f, 0.16f));
                UIFactory.SetSize(chip, 40, 40);
                var le = chip.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 40; le.minWidth = 40;

                // 没有图标就用名字首字占位——总比一个空方块强
                var label = UIFactory.CreateText(chip, "Ch", ShortLabel(relic.DisplayName), 20);
                UIFactory.Stretch(label.rectTransform);

                // ★ 原本这里挂的是本类私有的 RelicHover + 一个位置写死的 Text。
                //   现在统一走 TooltipView：全局只有一套样式、一处摆放逻辑，
                //   遗物 / 关键字 / 状态 / 意图 / 药水不会长出五种不同的提示框。
                TooltipTarget.Attach(chip.gameObject, new StaticTooltipSource(
                    relic.DisplayName,
                    relic.Def != null ? relic.Def.LocalizedDescription : "",
                    TooltipContent.KeywordAccent));
            }
        }

        private static string ShortLabel(string name)
            => string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);
    }
}
