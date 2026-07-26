using System.Collections.Generic;
using DG.Tweening;
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

        /// <summary>遗物 Id → 它那个小方块，触发闪光靠它反查。</summary>
        private readonly Dictionary<string, RectTransform> _chipByRelic = new Dictionary<string, RectTransform>();

        // ============================================================ 跨层的桥

        /// <summary>
        /// 当前活着的顶栏。
        ///
        /// ★ 为什么需要这个静态引用：遗物栏挂在 <see cref="GameApp"/> 下（切界面不重建），
        ///   而「遗物触发了」这条事件由 <see cref="BattlePresenter"/> 消费，
        ///   后者挂在会被反复销毁的战斗界面上。两者隔着一整层，没有现成的通路。
        ///
        /// ★ 静态可变引用是有风险的一类东西（同 <c>TooltipView.Suppressed</c>，铁律 31），
        ///   所以它只在 <see cref="Create"/> 里赋值、在 <see cref="OnDestroy"/> 里清掉，
        ///   并且所有使用点都必须容忍它是 null——单场战斗调试场景（Battle.unity）里
        ///   根本没有 GameApp，也就没有顶栏，那里一切照常，只是不闪。
        /// </summary>
        private static TopBarView _current;

        /// <summary>闪一下某个遗物的小方块。找不到（没有顶栏 / 没有这个遗物）就什么也不做。</summary>
        public static void FlashRelic(string relicId)
        {
            if (_current == null || string.IsNullOrEmpty(relicId)) return;
            _current.FlashRelicChip(relicId);
        }

        private void OnDestroy()
        {
            foreach (var kv in _chipByRelic)
                if (kv.Value != null) DOTween.Kill(kv.Value);

            if (_current == this) _current = null;
        }

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

            // ★ 切语言时顶栏会被销毁重建（GameApp.OnLanguageChanged），所以这里是赋值而不是「只赋一次」。
            _current = view;
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

            foreach (var kv in _chipByRelic)
                if (kv.Value != null) DOTween.Kill(kv.Value);
            _chipByRelic.Clear();

            for (int i = _relicRow.childCount - 1; i >= 0; i--)
                Destroy(_relicRow.GetChild(i).gameObject);

            _shownRelics.Clear();
            for (int i = 0; i < run.Relics.Count; i++)
            {
                var relic = run.Relics[i];
                _shownRelics.Add(relic.Id);

                var chip = UIFactory.CreatePanel(_relicRow, "Relic_" + relic.Id, new Color(0.35f, 0.30f, 0.16f));
                UIFactory.SetSize(chip, RelicChipSize, RelicChipSize);
                var le = chip.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = RelicChipSize; le.minWidth = RelicChipSize;

                // ★ 图标**留 3px 内边距**，不铺满整格。看着是审美，实际是功能：
                //   FlashRelic 靠 tween 这块 chip 的底色来表示「这个遗物刚刚触发了」，
                //   图标若铺满，底色被完全盖住，有图标的遗物触发时就什么也看不见了。
                //   留一圈边之后，闪光变成一道发亮的外框，照样成立。
                var icon = UIFactory.CreateArtWindow(chip, "Icon",
                    relic.Def != null ? relic.Def.Icon : null,
                    RelicChipSize - RelicIconInset * 2f, RelicChipSize - RelicIconInset * 2f, anchorY: 0.5f);

                if (icon != null)
                {
                    icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
                    icon.anchoredPosition = Vector2.zero;
                }
                else
                {
                    // 没有图标就用名字首字占位——总比一个空方块强
                    var label = UIFactory.CreateText(chip, "Ch", ShortLabel(relic.DisplayName), 20);
                    UIFactory.Stretch(label.rectTransform);
                }

                // ★ 原本这里挂的是本类私有的 RelicHover + 一个位置写死的 Text。
                //   现在统一走 TooltipView：全局只有一套样式、一处摆放逻辑，
                //   遗物 / 关键字 / 状态 / 意图 / 药水不会长出五种不同的提示框。
                TooltipTarget.Attach(chip.gameObject, new StaticTooltipSource(
                    relic.DisplayName,
                    relic.Def != null ? relic.Def.LocalizedDescription : "",
                    TooltipContent.KeywordAccent));

                _chipByRelic[relic.Id] = chip;
            }
        }

        // ============================================================ 触发闪光

        private const float RelicChipSize = 40f;

        /// <summary>图标四周留的边。见 RefreshRelics 里的注释——这圈边是触发闪光唯一的载体。</summary>
        private const float RelicIconInset = 3f;

        private static readonly Color RelicIdleColor = new Color(0.35f, 0.30f, 0.16f);
        private static readonly Color RelicFlashColor = new Color(1f, 0.92f, 0.55f);

        private const float RelicFlashTime = 0.55f;

        /// <summary>
        /// 遗物生效时闪一下并弹一下。
        ///
        /// ★ 遗物的效果全在 Hook 里，画面上**只有结果没有作者**：
        ///   金刚杵开局给了 1 点力量、回响护符让第一张攻击牌打了两次，
        ///   玩家看到的只是「力量 +1」和「打了两次」，完全不知道是哪个遗物干的，
        ///   甚至会怀疑自己那个遗物到底有没有在工作。
        /// </summary>
        private void FlashRelicChip(string relicId)
        {
            if (!_chipByRelic.TryGetValue(relicId, out var chip) || chip == null) return;

            var img = chip.GetComponent<Image>();
            if (img == null) return;

            DOTween.Kill(chip);
            chip.localScale = Vector3.one;
            img.color = RelicIdleColor;

            var seq = DOTween.Sequence().SetTarget(chip);
            seq.Append(DOTween.To(() => img.color, c => img.color = c, RelicFlashColor, 0.10f)
                              .SetTarget(chip));
            seq.Append(DOTween.To(() => img.color, c => img.color = c, RelicIdleColor, RelicFlashTime)
                              .SetTarget(chip));

            if (FeedbackSettings.HitMotionScale > 0.001f)
                seq.Join(chip.DOPunchScale(Vector3.one * 0.4f, 0.4f, 6, 0.6f));
        }

        private static string ShortLabel(string name)
            => string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);
    }
}
