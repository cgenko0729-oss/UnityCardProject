using System.Collections.Generic;

namespace Game.Map
{
    /// <summary>
    /// 一层完整的地图。★ 纯数据，无 Unity 依赖。
    /// 节点按行组织，第 0 行是起点（玩家可以任选其一进入），最后一行只有一个 Boss 节点。
    /// </summary>
    public class GameMap
    {
        /// <summary>全部节点。下标即 <see cref="MapNode.Id"/>。</summary>
        public readonly List<MapNode> Nodes = new List<MapNode>(64);

        /// <summary>按行索引的节点 Id。Rows[r] 是第 r 行的全部节点。</summary>
        public readonly List<List<int>> Rows = new List<List<int>>(16);

        public int RowCount => Rows.Count;

        public MapNode GetNode(int id)
            => id >= 0 && id < Nodes.Count ? Nodes[id] : null;

        public MapNode Boss => Nodes.Count > 0 ? Nodes[Nodes.Count - 1] : null;

        /// <summary>
        /// 从当前位置出发，下一步可以进入哪些节点。
        /// currentNodeId 为 -1 表示还在起点外，此时第 0 行全部可选。
        /// </summary>
        public void GetAvailableNodes(int currentNodeId, List<int> buffer)
        {
            buffer.Clear();

            if (currentNodeId < 0)
            {
                if (Rows.Count > 0) buffer.AddRange(Rows[0]);
                return;
            }

            var node = GetNode(currentNodeId);
            if (node != null) buffer.AddRange(node.Next);
        }

        public bool IsAvailable(int currentNodeId, int targetNodeId)
        {
            if (currentNodeId < 0)
                return Rows.Count > 0 && Rows[0].Contains(targetNodeId);

            var node = GetNode(currentNodeId);
            return node != null && node.Next.Contains(targetNodeId);
        }
    }
}
