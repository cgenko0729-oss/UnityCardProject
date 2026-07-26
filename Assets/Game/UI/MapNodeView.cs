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
        private TMP_Text _icon;
        private TMP_Text _label;

        private bool _available;
        private bool _visited;
        private bool _hovered;

        private static readonly Color ColAvailable = new Color(0.30f, 0.42f, 0.30f);
        private static readonly Color ColVisited = new Color(0.42f, 0.36f, 0.20f);
        private static readonly Color ColLocked = new Color(0.16f, 0.17f, 0.20f);
        private static readonly Color ColCurrent = new Color(0.55f, 0.48f, 0.22f);

        public const float NodeSize = 64f;

        public static MapNodeView Create(Transform parent, MapScreen screen, MapNode node)
        {
            var rt = UIFactory.CreatePanel(parent, $"Node{node.Id}", ColLocked);
            UIFactory.SetSize(rt, NodeSize, NodeSize);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var view = rt.gameObject.AddComponent<MapNodeView>();
            view._screen = screen;
            view.Node = node;
            view._bg = rt.GetComponent<Image>();

            view._icon = UIFactory.CreateText(rt, "Icon", IconOf(node.Type), 30);
            UIFactory.Stretch(view._icon.rectTransform);

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
            _icon.color = available || visited || isCurrent ? Color.white : new Color(0.5f, 0.52f, 0.58f);
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
            MapNodeType.Event => "？",
            MapNodeType.Treasure => "▣",
            MapNodeType.Boss => "王",
            _ => "?"
        };

        public static string LabelOf(MapNodeType t) => t switch
        {
            MapNodeType.Battle => "战斗",
            MapNodeType.Elite => "精英",
            MapNodeType.Rest => "休息",
            MapNodeType.Shop => "商店",
            MapNodeType.Event => "事件",
            MapNodeType.Treasure => "宝箱",
            MapNodeType.Boss => "首领",
            _ => ""
        };
    }
}
