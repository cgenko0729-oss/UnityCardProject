using System.Collections.Generic;
using Game.Core;

namespace Game.Map
{
    /// <summary>
    /// 地图生成的输入。★ 刻意只收 id 列表而不是 GameDatabase：
    /// 这样 MapGenerator 完全不依赖资产层，EditMode 测试可以直接喂几个假 id 跑。
    /// </summary>
    public struct MapGenerationConfig
    {
        public int RowCount;
        public int ColumnCount;
        public int PathCount;

        /// <summary>普通战斗可用的 EncounterDefinition.Id。</summary>
        public IReadOnlyList<string> NormalEncounterIds;
        /// <summary>精英战斗可用的 id。为空则精英节点降级为普通战斗。</summary>
        public IReadOnlyList<string> EliteEncounterIds;
        /// <summary>Boss 可用的 id。</summary>
        public IReadOnlyList<string> BossEncounterIds;
        /// <summary>事件可用的 EventDefinition.Id。为空则事件节点降级为普通战斗。</summary>
        public IReadOnlyList<string> EventIds;

        public static MapGenerationConfig Default => new MapGenerationConfig
        {
            RowCount = 15,
            ColumnCount = 7,
            PathCount = 6,
        };
    }

    /// <summary>
    /// 《杀戮尖塔》式分层随机地图生成。
    ///
    /// 算法：先拉 <c>PathCount</c> 条从第 0 行走到倒数第二行的路径，每步只允许左右偏移 1 列；
    /// 路径经过的格子才会真正生成节点，因此天然保证「每个节点都在某条从起点到 Boss 的通路上」。
    /// 最后一行固定是唯一的 Boss 节点，倒数第二行的所有节点都连向它。
    ///
    /// ★ 全部随机走 <c>RngStream.Map</c>：同一个种子必定生成一模一样的地图，
    ///   且重打一场战斗不会因为消耗了别的随机流而改变地图。
    /// </summary>
    public static class MapGenerator
    {
        // ---- 节点类型的随机权重（仅用于「自由行」，固定行不参与）
        private const int WeightBattle = 45;
        private const int WeightEvent = 22;
        private const int WeightElite = 16;
        private const int WeightRest = 12;
        private const int WeightShop = 5;

        /// <summary>精英 / 休息 / 商店最早可以出现在第几行。太早出现会破坏开局节奏。</summary>
        private const int MinRowForSpecial = 5;

        public static GameMap Generate(Rng rng, MapGenerationConfig cfg)
        {
            var map = new GameMap();

            int rowCount = cfg.RowCount > 2 ? cfg.RowCount : 3;
            int columnCount = cfg.ColumnCount > 0 ? cfg.ColumnCount : 7;
            int pathCount = cfg.PathCount > 0 ? cfg.PathCount : 6;
            int lastNormalRow = rowCount - 2;   // 最后一行留给 Boss

            for (int r = 0; r < rowCount; r++) map.Rows.Add(new List<int>(4));

            // (row, column) -> nodeId，用于同一格被多条路径经过时复用节点
            var grid = new Dictionary<int, int>(rowCount * columnCount);

            BuildPaths(map, grid, rng, rowCount, columnCount, pathCount, lastNormalRow);

            // ---- Boss 节点：最后创建，保证它是 Nodes 的最后一个（GameMap.Boss 依赖这一点）
            var boss = NewNode(map, rowCount - 1, columnCount / 2, MapNodeType.Boss);
            var lastRow = map.Rows[lastNormalRow];
            for (int i = 0; i < lastRow.Count; i++) Link(map, lastRow[i], boss.Id);

            // ---- 按列排序，画图时从左到右
            for (int r = 0; r < map.Rows.Count; r++)
                SortRowByColumn(map, map.Rows[r]);

            AssignTypes(map, rng, rowCount);
            AssignContent(map, rng, cfg);

            return map;
        }

        // ================================================================= 路径

        private static void BuildPaths(GameMap map, Dictionary<int, int> grid, Rng rng,
                                       int rowCount, int columnCount, int pathCount, int lastNormalRow)
        {
            int firstStartColumn = -1;

            for (int p = 0; p < pathCount; p++)
            {
                int col = rng.Range(RngStream.Map, 0, columnCount);

                // 保证至少有两个不同的起点，否则地图开局没有选择
                if (p == 0) firstStartColumn = col;
                else if (p == 1 && col == firstStartColumn)
                    col = (col + 1 + rng.Range(RngStream.Map, 0, columnCount - 1)) % columnCount;

                int prevId = GetOrCreate(map, grid, 0, col, columnCount).Id;

                for (int row = 1; row <= lastNormalRow; row++)
                {
                    col = NextColumn(rng, col, columnCount);
                    var node = GetOrCreate(map, grid, row, col, columnCount);
                    Link(map, prevId, node.Id);
                    prevId = node.Id;
                }
            }
        }

        /// <summary>下一行往左 / 直行 / 往右，边界处收敛回来。</summary>
        private static int NextColumn(Rng rng, int col, int columnCount)
        {
            int delta = rng.Range(RngStream.Map, -1, 2);   // -1 / 0 / +1
            int next = col + delta;
            if (next < 0) next = 0;
            if (next >= columnCount) next = columnCount - 1;
            return next;
        }

        private static MapNode GetOrCreate(GameMap map, Dictionary<int, int> grid,
                                           int row, int column, int columnCount)
        {
            int key = row * columnCount + column;
            if (grid.TryGetValue(key, out int existing)) return map.Nodes[existing];

            var node = NewNode(map, row, column, MapNodeType.Battle);
            grid[key] = node.Id;
            return node;
        }

        private static MapNode NewNode(GameMap map, int row, int column, MapNodeType type)
        {
            var node = new MapNode
            {
                Id = map.Nodes.Count,
                Row = row,
                Column = column,
                Type = type,
            };
            map.Nodes.Add(node);
            map.Rows[row].Add(node.Id);
            return node;
        }

        private static void Link(GameMap map, int fromId, int toId)
        {
            var from = map.Nodes[fromId];
            if (from.Next.Contains(toId)) return;
            from.Next.Add(toId);
            map.Nodes[toId].Prev.Add(fromId);
        }

        /// <summary>插入排序：稳定，保证同种子下行内顺序完全一致。</summary>
        private static void SortRowByColumn(GameMap map, List<int> row)
        {
            for (int i = 1; i < row.Count; i++)
            {
                int key = row[i];
                int col = map.Nodes[key].Column;
                int j = i - 1;
                while (j >= 0 && map.Nodes[row[j]].Column > col)
                {
                    row[j + 1] = row[j];
                    j--;
                }
                row[j + 1] = key;
            }
        }

        // ================================================================= 节点类型

        private static void AssignTypes(GameMap map, Rng rng, int rowCount)
        {
            int treasureRow = rowCount / 2;          // 中间一层白给宝箱，作为节奏上的喘息点
            int restRow = rowCount - 2;              // Boss 前一行必定是休息点

            for (int r = 0; r < rowCount - 1; r++)
            {
                var row = map.Rows[r];
                for (int i = 0; i < row.Count; i++)
                {
                    var node = map.Nodes[row[i]];

                    if (r == 0) { node.Type = MapNodeType.Battle; continue; }         // 开局必定是战斗
                    if (r == treasureRow) { node.Type = MapNodeType.Treasure; continue; }
                    if (r == restRow) { node.Type = MapNodeType.Rest; continue; }

                    node.Type = RollType(map, rng, node, r);
                }
            }
        }

        private static MapNodeType RollType(GameMap map, Rng rng, MapNode node, int row)
        {
            // 最多重掷几次以躲开约束；掷不出来就退回普通战斗，保证一定能收敛。
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var t = WeightedType(rng);

                bool special = t == MapNodeType.Elite || t == MapNodeType.Rest || t == MapNodeType.Shop;
                if (special && row < MinRowForSpecial) continue;

                // 不允许与任一父节点同类型：避免出现「连着两个商店」这种浪费的路径
                if (special && AnyParentIsType(map, node, t)) continue;

                return t;
            }
            return MapNodeType.Battle;
        }

        private static MapNodeType WeightedType(Rng rng)
        {
            int total = WeightBattle + WeightEvent + WeightElite + WeightRest + WeightShop;
            int roll = rng.Range(RngStream.Map, 0, total);

            roll -= WeightBattle; if (roll < 0) return MapNodeType.Battle;
            roll -= WeightEvent; if (roll < 0) return MapNodeType.Event;
            roll -= WeightElite; if (roll < 0) return MapNodeType.Elite;
            roll -= WeightRest; if (roll < 0) return MapNodeType.Rest;
            return MapNodeType.Shop;
        }

        private static bool AnyParentIsType(GameMap map, MapNode node, MapNodeType type)
        {
            for (int i = 0; i < node.Prev.Count; i++)
                if (map.Nodes[node.Prev[i]].Type == type) return true;
            return false;
        }

        // ================================================================= 内容分配

        private static void AssignContent(GameMap map, Rng rng, MapGenerationConfig cfg)
        {
            var normalBag = new Bag(cfg.NormalEncounterIds);
            var eliteBag = new Bag(cfg.EliteEncounterIds);
            var eventBag = new Bag(cfg.EventIds);
            var bossBag = new Bag(cfg.BossEncounterIds);

            for (int i = 0; i < map.Nodes.Count; i++)
            {
                var node = map.Nodes[i];
                switch (node.Type)
                {
                    case MapNodeType.Battle:
                        node.ContentId = normalBag.Draw(rng);
                        break;

                    case MapNodeType.Elite:
                        node.ContentId = eliteBag.Draw(rng);
                        // 没配精英战斗就降级成普通战斗，而不是留一个点不进去的死节点
                        if (node.ContentId == null)
                        {
                            node.Type = MapNodeType.Battle;
                            node.ContentId = normalBag.Draw(rng);
                        }
                        break;

                    case MapNodeType.Boss:
                        node.ContentId = bossBag.Draw(rng) ?? normalBag.Draw(rng);
                        break;

                    case MapNodeType.Event:
                        node.ContentId = eventBag.Draw(rng);
                        if (node.ContentId == null)
                        {
                            node.Type = MapNodeType.Battle;
                            node.ContentId = normalBag.Draw(rng);
                        }
                        break;

                    default:
                        node.ContentId = null;   // 休息 / 商店 / 宝箱不需要内容 id
                        break;
                }
            }
        }

        /// <summary>
        /// 洗牌袋：抽空之前不会重复。比每次独立随机更能保证内容多样性
        /// （独立随机很容易连着排三个同样的战斗）。
        /// </summary>
        private struct Bag
        {
            private readonly List<string> _source;
            private List<string> _remaining;

            public Bag(IReadOnlyList<string> source)
            {
                _source = new List<string>();
                if (source != null)
                    for (int i = 0; i < source.Count; i++)
                        if (!string.IsNullOrEmpty(source[i])) _source.Add(source[i]);
                _remaining = null;
            }

            public string Draw(Rng rng)
            {
                if (_source == null || _source.Count == 0) return null;

                if (_remaining == null || _remaining.Count == 0)
                {
                    _remaining = new List<string>(_source);
                    rng.Shuffle(RngStream.Map, _remaining);
                }

                int last = _remaining.Count - 1;
                var pick = _remaining[last];
                _remaining.RemoveAt(last);
                return pick;
            }
        }
    }
}
