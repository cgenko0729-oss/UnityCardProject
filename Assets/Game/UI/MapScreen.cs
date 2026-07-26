using System.Collections.Generic;
using Game.Localization;
using Game.Map;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 地图界面。节点按行从下往上排列，玩家从第 0 行进入，一路走到顶层的 Boss。
    /// ★ 只读 GameMap，进入节点一律走 <c>RunManager.EnterNode</c>。
    /// </summary>
    public class MapScreen : ScreenBase
    {
        private const float RowSpacing = 130f;
        private const float ColumnSpacing = 150f;
        private const float BottomMargin = 90f;

        private readonly List<MapNodeView> _views = new List<MapNodeView>();
        private readonly List<int> _available = new List<int>(8);

        private TMP_Text _hintText;
        private ScrollRect _scroll;

        protected override void Build()
        {
            var map = Run?.Map;
            if (map == null)
            {
                UIFactory.CreateText(Root, "NoMap", Loc.T("ui.map.no_data", "没有地图数据。"), 28);
                return;
            }

            // ---- 滚动容器
            var viewport = UIFactory.CreateEmpty(Root, "Viewport");
            UIFactory.SetAnchored(viewport, new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(0, 70), new Vector2(0, -60));
            var vpImage = viewport.gameObject.AddComponent<Image>();
            vpImage.color = new Color(0f, 0f, 0f, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();

            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;

            float contentHeight = BottomMargin * 2 + (map.RowCount - 1) * RowSpacing;
            var content = UIFactory.CreateEmpty(viewport, "MapContent");
            content.anchorMin = new Vector2(0, 0);
            content.anchorMax = new Vector2(1, 0);
            content.pivot = new Vector2(0.5f, 0f);
            content.offsetMin = new Vector2(0, 0);
            content.offsetMax = new Vector2(0, 0);
            content.sizeDelta = new Vector2(0, contentHeight);

            _scroll.content = content;
            _scroll.viewport = viewport;

            // ---- 先画连线，再画节点，保证节点压在线上面
            BuildEdges(content, map);
            BuildNodes(content, map);

            // ---- 底部提示条
            _hintText = UIFactory.CreateText(Root, "Hint", "", 22,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.65f));
            UIFactory.SetAnchored(_hintText.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 16), new Vector2(0, 62));

            RefreshNodes();

            // 玩家在底部，一进来就把视图滚到底
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        private void BuildNodes(RectTransform content, GameMap map)
        {
            for (int i = 0; i < map.Nodes.Count; i++)
            {
                var node = map.Nodes[i];
                var view = MapNodeView.Create(content, this, node, Db != null ? Db.GetMapIcon(node.Type) : null);
                ((RectTransform)view.transform).anchoredPosition = PositionOf(node, map);
                _views.Add(view);
            }
        }

        private void BuildEdges(RectTransform content, GameMap map)
        {
            for (int i = 0; i < map.Nodes.Count; i++)
            {
                var from = map.Nodes[i];
                for (int n = 0; n < from.Next.Count; n++)
                {
                    var to = map.Nodes[from.Next[n]];
                    CreateEdge(content, PositionOf(from, map), PositionOf(to, map));
                }
            }
        }

        /// <summary>两点之间画一条细长条。uGUI 没有画线组件，用旋转的 Image 是最省事的做法。</summary>
        private static void CreateEdge(RectTransform parent, Vector2 a, Vector2 b)
        {
            var rt = UIFactory.CreatePanel(parent, "Edge", new Color(0.35f, 0.37f, 0.44f, 0.75f));
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0f, 0.5f);

            var delta = b - a;
            rt.anchoredPosition = a;
            rt.sizeDelta = new Vector2(delta.magnitude, 4f);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

            var img = rt.GetComponent<Image>();
            img.raycastTarget = false;
        }

        private static Vector2 PositionOf(MapNode node, GameMap map)
        {
            // 同一行的节点按列居中铺开
            var row = map.Rows[node.Row];
            int index = row.IndexOf(node.Id);
            int count = row.Count;
            float x = (index - (count - 1) * 0.5f) * ColumnSpacing;
            float y = BottomMargin + node.Row * RowSpacing;
            return new Vector2(x, y);
        }

        // ================================================================= 交互

        public void OnNodeClicked(MapNodeView view)
        {
            if (view?.Node == null) return;

            if (!Run.Map.IsAvailable(Run.CurrentNodeId, view.Node.Id))
            {
                ShowHint(Loc.T("ui.map.unreachable", "这个节点现在走不到。"));
                return;
            }

            Manager.EnterNode(view.Node.Id);
        }

        public void ShowNodeHint(MapNode node, bool available)
        {
            if (node == null) { ShowHint(DefaultHint()); return; }

            string name = MapNodeView.LabelOf(node.Type);
            string extra = "";

            if (node.Type == MapNodeType.Battle || node.Type == MapNodeType.Elite || node.Type == MapNodeType.Boss)
            {
                var enc = Db != null ? Db.GetEncounter(node.ContentId) : null;
                if (enc != null) extra = $"：{enc.DisplayName}";
            }

            ShowHint(available ? Loc.T("ui.map.node_enter", "{0}{1}　—　点击进入", name, extra) : Loc.T("ui.map.node_blocked", "{0}{1}　—　无法到达", name, extra));
        }

        private string DefaultHint()
            => Run.CurrentNodeId < 0 ? Loc.T("ui.map.pick_start", "选择一个起点。") : Loc.T("ui.map.pick_next", "选择下一个要前往的节点。");

        private void ShowHint(string text)
        {
            if (_hintText != null) _hintText.text = text;
        }

        // ================================================================= 刷新

        private void LateUpdate()
        {
            if (Run?.Map != null) RefreshNodes();
        }

        private void RefreshNodes()
        {
            Manager.GetAvailableNodes(_available);

            for (int i = 0; i < _views.Count; i++)
            {
                var v = _views[i];
                int id = v.Node.Id;
                v.Refresh(
                    available: _available.Contains(id),
                    visited: Run.VisitedNodeIds.Contains(id),
                    isCurrent: Run.CurrentNodeId == id);
            }
        }
    }
}
