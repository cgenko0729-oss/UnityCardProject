using System;
using System.Collections.Generic;
using Game.Cards;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 通用选牌面板。奖励三选一、删卡、升级卡、事件里的选牌全部共用这一个。
    ///
    /// 两种数据源二选一：
    ///   - <c>deckCards</c>：从玩家牌库里选（删卡 / 升级），回调返回在牌库中的下标；
    ///   - <c>defCards</c>：从一组候选配置里选（奖励 / 事件给牌），回调返回候选列表的下标。
    /// </summary>
    public class CardPickerScreen : MonoBehaviour
    {
        private GameApp _app;
        private RectTransform _root;
        private Action<List<int>> _onConfirm;

        private int _pickCount;
        private bool _cancellable;

        private readonly List<int> _selected = new List<int>(4);
        private readonly List<CardMiniView> _views = new List<CardMiniView>();

        private Button _confirmButton;
        private TMP_Text _counterText;

        /// <summary>★ 组件挂在遮罩根自己身上，<see cref="Close"/> 一个 Destroy 就能收干净。</summary>
        public void Open(GameApp app, string title,
                         IReadOnlyList<CardInstance> deckCards,
                         IReadOnlyList<CardDefinition> defCards,
                         int pickCount, bool cancellable, Action<List<int>> onConfirm)
        {
            _app = app;
            _pickCount = Mathf.Max(1, pickCount);
            _cancellable = cancellable;
            _onConfirm = onConfirm;

            _root = (RectTransform)transform;

            var titleText = UIFactory.CreateText(_root, "Title", title, 34,
                TextAnchor.MiddleCenter, new Color(1f, 0.93f, 0.72f));
            UIFactory.SetAnchored(titleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -110), new Vector2(0, -50));

            _counterText = UIFactory.CreateText(_root, "Counter", "", 22,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.85f));
            UIFactory.SetAnchored(_counterText.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -146), new Vector2(0, -110));

            BuildGrid(deckCards, defCards);
            BuildButtons();
            RefreshSelection();
        }

        private void BuildGrid(IReadOnlyList<CardInstance> deckCards, IReadOnlyList<CardDefinition> defCards)
        {
            var viewport = UIFactory.CreateEmpty(_root, "Viewport");
            UIFactory.SetAnchored(viewport, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(80, 130), new Vector2(-80, -160));
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var content = UIFactory.CreateEmpty(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CardMiniView.Width, CardMiniView.Height);
            grid.spacing = new Vector2(16, 16);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(10, 10, 10, 10);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;
            scroll.viewport = viewport;

            int count = deckCards != null ? deckCards.Count : (defCards != null ? defCards.Count : 0);
            for (int i = 0; i < count; i++)
            {
                int index = i;
                CardMiniView view = deckCards != null
                    ? CardMiniView.Create(content, deckCards[i])
                    : CardMiniView.Create(content, defCards[i], upgraded: false);

                view.SetClickHandler(() => Toggle(index));
                _views.Add(view);
            }
        }

        private void BuildButtons()
        {
            _confirmButton = UIFactory.CreateTextButton(_root, "Confirm", "确定", 28,
                new Color(0.30f, 0.44f, 0.32f), Confirm);
            Place((RectTransform)_confirmButton.transform, _cancellable ? -180 : 0);

            if (_cancellable)
            {
                var skip = UIFactory.CreateTextButton(_root, "Skip", "跳过", 28,
                    new Color(0.34f, 0.26f, 0.26f), Cancel);
                Place((RectTransform)skip.transform, 180);
            }
        }

        private void Place(RectTransform rt, float x)
        {
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(300, 76);
            rt.anchoredPosition = new Vector2(x, 34);
        }

        // ================================================================= 选择

        private void Toggle(int index)
        {
            if (_selected.Contains(index)) _selected.Remove(index);
            else
            {
                // 只需要选一张时，点新的就换掉旧的——比强迫玩家先取消再选顺手得多
                if (_pickCount == 1) _selected.Clear();
                if (_selected.Count >= _pickCount) return;
                _selected.Add(index);
            }
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < _views.Count; i++)
                _views[i].SetSelected(_selected.Contains(i));

            _counterText.text = _pickCount > 1
                ? $"已选 {_selected.Count} / {_pickCount}"
                : "";

            UIFactory.SetInteractable(_confirmButton, _selected.Count == _pickCount,
                new Color(0.30f, 0.44f, 0.32f));
        }

        private void Confirm()
        {
            if (_selected.Count != _pickCount) return;
            var result = new List<int>(_selected);
            var cb = _onConfirm;
            _onConfirm = null;
            Close();
            cb?.Invoke(result);
        }

        private void Cancel()
        {
            var cb = _onConfirm;
            _onConfirm = null;
            Close();
            cb?.Invoke(new List<int>());
        }

        /// <summary>
        /// ★ 先清空回调再销毁：回调里常常会立刻再开一个选牌面板（事件的连续选择），
        ///   不清空的话「关闭旧面板」和「打开新面板」的顺序会互相踩。
        /// </summary>
        private void Close() => Destroy(gameObject);
    }

    /// <summary>选牌面板里的一张小卡。只显示费用 / 名字 / 描述，够做决策就行。</summary>
    public class CardMiniView : MonoBehaviour, IPointerClickHandler
    {
        public const float Width = 190f;
        public const float Height = 250f;

        private Image _bg;
        private Action _onClick;
        private Color _baseColor;

        public static CardMiniView Create(Transform parent, CardInstance card)
            => Create(parent, card.Def, card.UpgradeLevel > 0);

        public static CardMiniView Create(Transform parent, CardDefinition def, bool upgraded)
        {
            var color = ColorOf(def != null ? def.Type : CardType.Skill);
            var rt = UIFactory.CreatePanel(parent, "Mini_" + (def != null ? def.Id : "null"), color);

            var view = rt.gameObject.AddComponent<CardMiniView>();
            view._bg = rt.GetComponent<Image>();
            view._baseColor = color;

            var cost = UIFactory.CreatePanel(rt, "Cost", new Color(0.08f, 0.08f, 0.10f, 0.92f));
            UIFactory.SetAnchored(cost, new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -46), new Vector2(46, -6));
            var costText = UIFactory.CreateText(cost, "CostText", CostTextOf(def), 24);
            UIFactory.Stretch(costText.rectTransform);

            var name = UIFactory.CreateText(rt, "Name",
                (def != null ? def.DisplayName : "?") + (upgraded ? "+" : ""), 20, TextAnchor.UpperCenter);
            UIFactory.SetAnchored(name.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(50, -44), new Vector2(-6, -8));

            var desc = UIFactory.CreateText(rt, "Desc", DescriptionOf(def), 15, TextAnchor.UpperLeft);
            UIFactory.SetAnchored(desc.rectTransform, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(10, 30), new Vector2(-10, -52));

            var footer = UIFactory.CreateText(rt, "Footer", FooterOf(def), 14, TextAnchor.LowerCenter,
                new Color(1f, 1f, 1f, 0.6f));
            UIFactory.SetAnchored(footer.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(4, 4), new Vector2(-4, 26));

            return view;
        }

        public void SetClickHandler(Action onClick) => _onClick = onClick;

        public void SetSelected(bool on)
        {
            _bg.color = on ? Color.Lerp(_baseColor, new Color(1f, 0.95f, 0.6f), 0.55f) : _baseColor;
            transform.localScale = on ? Vector3.one * 1.05f : Vector3.one;
        }

        public void OnPointerClick(PointerEventData e) => _onClick?.Invoke();

        /// <summary>
        /// 战斗外没有 BattleContext，描述模板里的 {N} 用效果的静态数值填。
        /// </summary>
        private static string DescriptionOf(CardDefinition def)
        {
            if (def == null) return "";
            if (def.Effects == null || def.Effects.Count == 0) return def.DescriptionTemplate ?? "";

            var probe = new Game.Cards.CardInstance(0, def);
            return probe.GetDescription(null);
        }

        private static string CostTextOf(CardDefinition def)
        {
            if (def == null) return "?";
            return def.CostMode switch
            {
                CostMode.X => "X",
                CostMode.Unplayable => "-",
                _ => def.Cost.ToString(),
            };
        }

        private static string FooterOf(CardDefinition def)
        {
            if (def == null) return "";
            string s = TypeLabel(def.Type) + " · " + RarityLabel(def.Rarity);
            if (def.HasKeyword(CardKeyword.Exhaust)) s += " · 消耗";
            if (def.HasKeyword(CardKeyword.Retain)) s += " · 保留";
            if (def.HasKeyword(CardKeyword.Innate)) s += " · 固有";
            if (def.HasKeyword(CardKeyword.Ethereal)) s += " · 虚无";
            return s;
        }

        private static string TypeLabel(CardType t) => t switch
        {
            CardType.Attack => "攻击",
            CardType.Skill => "技能",
            CardType.Power => "能力",
            CardType.Status => "状态",
            CardType.Curse => "诅咒",
            _ => ""
        };

        private static string RarityLabel(CardRarity r) => r switch
        {
            CardRarity.Basic => "基础",
            CardRarity.Common => "普通",
            CardRarity.Uncommon => "罕见",
            CardRarity.Rare => "稀有",
            _ => "特殊"
        };

        private static Color ColorOf(CardType t) => t switch
        {
            CardType.Attack => new Color(0.50f, 0.19f, 0.19f),
            CardType.Skill => new Color(0.19f, 0.32f, 0.50f),
            CardType.Power => new Color(0.39f, 0.26f, 0.50f),
            _ => new Color(0.24f, 0.24f, 0.24f),
        };
    }
}
