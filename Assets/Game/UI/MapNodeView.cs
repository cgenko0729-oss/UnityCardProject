using Game.Localization;
using Game.Map;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>地图上的一个节点按钮。★ 只读 MapNode，点击后把 Id 交给 MapScreen。</summary>
    public class MapNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public MapNode Node { get; private set; }

        private MapScreen _screen;
        private Image _bg;

        /// <summary>符号图标。★ 与 <see cref="_iconImage"/> 二选一，配了 Sprite 时它为 null。</summary>
        private TMP_Text _icon;

        /// <summary>Sprite 图标。★ 与 <see cref="_icon"/> 二选一。</summary>
        private Image _iconImage;

        private TMP_Text _label;

        private bool _available;
        private bool _visited;
        private bool _hovered;

        private static readonly Color ColAvailable = new Color(0.30f, 0.42f, 0.30f);
        private static readonly Color ColVisited = new Color(0.42f, 0.36f, 0.20f);
        private static readonly Color ColLocked = new Color(0.16f, 0.17f, 0.20f);
        private static readonly Color ColCurrent = new Color(0.55f, 0.48f, 0.22f);

        public const float NodeSize = 64f;

        /// <summary>图标四周留的边。留着底色才看得出节点是「可走 / 已走 / 锁着」。</summary>
        private const float IconInset = 8f;

        public static MapNodeView Create(Transform parent, MapScreen screen, MapNode node, Sprite icon = null)
        {
            // ★ 地图节点是全界面**最密**的一片方块阵，而它们之间只靠几条细连线区分。
            //   圆角 + 投影把每个节点从背景里拎起来，一眼就能数清有几个、哪些连着。
            //   ★ 用 Chip 而不是 Card：节点是「一枚棋子」不是「一张牌」，
            //     投影必须收得很小，否则相邻节点的投影会连成一片糊。
            var rt = UIFactory.CreateRoundedPanel(parent, $"Node{node.Id}",
                ColLocked, RoundedStyle.Chip).rectTransform;
            UIFactory.SetSize(rt, NodeSize, NodeSize);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var view = rt.gameObject.AddComponent<MapNodeView>();
            view._screen = screen;
            view.Node = node;
            view._bg = rt.GetComponent<Image>();

            // 配了图标就用图，没配就还是那几个符号。
            // ★ 顺带绕开一个真实存在的坑：⚔ ☠ ♨ ▣ 这些符号在很多系统字体里根本没有字形，
            //   上一次已经为它们修过一轮兜底字体链。换成 Sprite 之后这条路彻底不依赖字体。
            var iconWindow = UIFactory.CreateArtWindow(rt, "IconArt", icon,
                NodeSize - IconInset * 2f, NodeSize - IconInset * 2f, anchorY: 0.5f);

            if (iconWindow != null)
            {
                iconWindow.anchorMin = iconWindow.anchorMax = iconWindow.pivot = new Vector2(0.5f, 0.5f);
                iconWindow.anchoredPosition = Vector2.zero;
                view._iconImage = iconWindow.GetComponentInChildren<Image>();
            }
            else
            {
                view._icon = UIFactory.CreateText(rt, "Icon", IconOf(node.Type), 30);
                UIFactory.Stretch(view._icon.rectTransform);
            }

            view._label = UIFactory.CreateText(rt, "Label", LabelOf(node.Type), 15,
                TextAnchor.UpperCenter, new Color(0.75f, 0.78f, 0.85f));
            UIFactory.SetAnchored(view._label.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(-20, -26), new Vector2(20, -4));

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = view._bg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => screen.OnNodeClicked(view));

            return view;
        }

        public void Refresh(bool available, bool visited, bool isCurrent)
        {
            _available = available;
            _visited = visited;

            Color c = ColLocked;
            if (isCurrent) c = ColCurrent;
            else if (available) c = ColAvailable;
            else if (visited) c = ColVisited;

            if (_hovered && available) c = Color.Lerp(c, Color.white, 0.30f);

            _bg.color = c;

            // 未点亮的节点整体压暗。★ 图标与符号只会有一个存在，两个都要照顾到——
            //   只写 _icon 的话，换成 Sprite 之后「走不到的节点」就再也不会变灰了。
            var iconColor = available || visited || isCurrent ? Color.white : new Color(0.5f, 0.52f, 0.58f);
            if (_icon != null) _icon.color = iconColor;
            if (_iconImage != null) _iconImage.color = iconColor;

            transform.localScale = (_hovered && available) ? Vector3.one * 1.12f : Vector3.one;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _hovered = true;
            _screen.ShowNodeHint(Node, _available);
        }

        public void OnPointerExit(PointerEventData e)
        {
            _hovered = false;
            _screen.ShowNodeHint(null, false);
        }

        public static string IconOf(MapNodeType t) => t switch
        {
            MapNodeType.Battle => "⚔",
            MapNodeType.Elite => "☠",
            MapNodeType.Rest => "♨",
            MapNodeType.Shop => "◆",
            // ★ 这一列是图标不是文案，多数是符号、不需要翻译。
            //   但「？」是全角问号、「王」是汉字——换到英文界面里这两个会显得很突兀，
            //   所以只有这两条留了 key。
            MapNodeType.Event => Loc.T("map.icon.event", "？"),
            MapNodeType.Treasure => "▣",
            MapNodeType.Boss => Loc.T("map.icon.boss", "王"),
            _ => "?"
        };

        public static string LabelOf(MapNodeType t) => t switch
        {
            MapNodeType.Battle => Loc.T("map.node.battle", "战斗"),
            MapNodeType.Elite => Loc.T("map.node.elite", "精英"),
            MapNodeType.Rest => Loc.T("map.node.rest", "休息"),
            MapNodeType.Shop => Loc.T("map.node.shop", "商店"),
            MapNodeType.Event => Loc.T("map.node.event", "事件"),
            MapNodeType.Treasure => Loc.T("map.node.treasure", "宝箱"),
            MapNodeType.Boss => Loc.T("map.node.boss", "首领"),
            _ => ""
        };
    }
}
