using System.Collections.Generic;
using Game.Core;
using Game.Map;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 地图生成的测试。★ 没有任何 Unity 依赖也没有资产依赖——
    /// MapGenerator 收的是 id 字符串列表而不是 GameDatabase，就是为了能这样测。
    /// </summary>
    public class MapGeneratorTests
    {
        private static MapGenerationConfig Config()
        {
            var cfg = MapGenerationConfig.Default;
            cfg.NormalEncounterIds = new[] { "n1", "n2", "n3" };
            cfg.EliteEncounterIds = new[] { "e1", "e2" };
            cfg.BossEncounterIds = new[] { "boss" };
            cfg.EventIds = new[] { "ev1", "ev2" };
            return cfg;
        }

        private static GameMap Gen(int seed) => MapGenerator.Generate(new Rng(seed), Config());

        [Test]
        public void SameSeed_ProducesIdenticalMap()
        {
            var a = Gen(20260725);
            var b = Gen(20260725);

            Assert.AreEqual(a.Nodes.Count, b.Nodes.Count, "节点数不一致");

            for (int i = 0; i < a.Nodes.Count; i++)
            {
                Assert.AreEqual(a.Nodes[i].Row, b.Nodes[i].Row);
                Assert.AreEqual(a.Nodes[i].Column, b.Nodes[i].Column);
                Assert.AreEqual(a.Nodes[i].Type, b.Nodes[i].Type, $"节点 {i} 类型不一致");
                Assert.AreEqual(a.Nodes[i].ContentId, b.Nodes[i].ContentId, $"节点 {i} 内容不一致");
                CollectionAssert.AreEqual(a.Nodes[i].Next, b.Nodes[i].Next, $"节点 {i} 连线不一致");
            }
        }

        [Test]
        public void DifferentSeed_ProducesDifferentMap()
        {
            var a = Gen(1);
            var b = Gen(999);

            bool different = a.Nodes.Count != b.Nodes.Count;
            if (!different)
            {
                for (int i = 0; i < a.Nodes.Count && !different; i++)
                    if (a.Nodes[i].Type != b.Nodes[i].Type || a.Nodes[i].Column != b.Nodes[i].Column)
                        different = true;
            }

            Assert.IsTrue(different, "两个不同种子生成了完全一样的地图");
        }

        [Test]
        public void EveryNode_IsReachableFromStart()
        {
            // 生成算法是「沿路径造节点」，所以每个节点都必须在某条起点→Boss 的通路上。
            // 这条断言是整个地图算法最关键的不变量：漏一个孤立节点，玩家就会看到一个永远点不了的圈。
            for (int seed = 0; seed < 30; seed++)
            {
                var map = Gen(seed);
                var reachable = new HashSet<int>();
                var queue = new Queue<int>();

                for (int i = 0; i < map.Rows[0].Count; i++)
                {
                    reachable.Add(map.Rows[0][i]);
                    queue.Enqueue(map.Rows[0][i]);
                }

                while (queue.Count > 0)
                {
                    var node = map.GetNode(queue.Dequeue());
                    for (int i = 0; i < node.Next.Count; i++)
                        if (reachable.Add(node.Next[i])) queue.Enqueue(node.Next[i]);
                }

                Assert.AreEqual(map.Nodes.Count, reachable.Count,
                    $"种子 {seed}：有 {map.Nodes.Count - reachable.Count} 个节点从起点走不到");
            }
        }

        [Test]
        public void EveryNode_CanReachBoss()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var map = Gen(seed);
                int bossId = map.Boss.Id;

                // 反向 BFS：从 Boss 沿 Prev 往回走，应该能覆盖所有节点
                var canReach = new HashSet<int> { bossId };
                var queue = new Queue<int>();
                queue.Enqueue(bossId);

                while (queue.Count > 0)
                {
                    var node = map.GetNode(queue.Dequeue());
                    for (int i = 0; i < node.Prev.Count; i++)
                        if (canReach.Add(node.Prev[i])) queue.Enqueue(node.Prev[i]);
                }

                Assert.AreEqual(map.Nodes.Count, canReach.Count,
                    $"种子 {seed}：有节点走不到 Boss，玩家会走进死胡同");
            }
        }

        [Test]
        public void FirstRow_IsAlwaysBattle_AndHasMultipleChoices()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var map = Gen(seed);
                Assert.GreaterOrEqual(map.Rows[0].Count, 2, $"种子 {seed}：起始行只有一个节点，开局没有选择");

                for (int i = 0; i < map.Rows[0].Count; i++)
                    Assert.AreEqual(MapNodeType.Battle, map.GetNode(map.Rows[0][i]).Type,
                        $"种子 {seed}：第一行出现了非战斗节点");
            }
        }

        [Test]
        public void LastRow_IsSingleBoss()
        {
            var map = Gen(7);
            var lastRow = map.Rows[map.RowCount - 1];

            Assert.AreEqual(1, lastRow.Count, "最后一行应该只有一个 Boss 节点");
            Assert.AreEqual(MapNodeType.Boss, map.GetNode(lastRow[0]).Type);
            Assert.AreEqual(map.Boss.Id, lastRow[0], "GameMap.Boss 必须指向最后一行那个节点");
            Assert.AreEqual("boss", map.Boss.ContentId);
        }

        [Test]
        public void RowBeforeBoss_IsAllRest()
        {
            // Boss 前必定给一次休息，否则残血撞 Boss 会变成纯粹的运气问题
            var map = Gen(42);
            var restRow = map.Rows[map.RowCount - 2];

            for (int i = 0; i < restRow.Count; i++)
                Assert.AreEqual(MapNodeType.Rest, map.GetNode(restRow[i]).Type);
        }

        [Test]
        public void SpecialNodes_NeverAppearTooEarly()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var map = Gen(seed);
                for (int i = 0; i < map.Nodes.Count; i++)
                {
                    var n = map.Nodes[i];
                    if (n.Row >= 5) continue;
                    Assert.IsTrue(n.Type == MapNodeType.Battle || n.Type == MapNodeType.Event
                                  || n.Type == MapNodeType.Treasure,
                        $"种子 {seed}：第 {n.Row} 行出现了 {n.Type}，特殊节点不该这么早出现");
                }
            }
        }

        [Test]
        public void BattleNodes_AlwaysHaveContent()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var map = Gen(seed);
                for (int i = 0; i < map.Nodes.Count; i++)
                {
                    var n = map.Nodes[i];
                    bool needsContent = n.Type == MapNodeType.Battle || n.Type == MapNodeType.Elite
                                        || n.Type == MapNodeType.Boss || n.Type == MapNodeType.Event;
                    if (!needsContent) continue;

                    Assert.IsFalse(string.IsNullOrEmpty(n.ContentId),
                        $"种子 {seed}：{n.Type} 节点没有内容 id，进去会是空的");
                }
            }
        }

        [Test]
        public void EmptyElitePool_DegradesToBattle_RatherThanBreaking()
        {
            // 内容还没做齐时，地图必须依然可玩——降级成普通战斗，而不是留一堆进不去的节点
            var cfg = Config();
            cfg.EliteEncounterIds = new string[0];
            cfg.EventIds = new string[0];

            var map = MapGenerator.Generate(new Rng(5), cfg);

            for (int i = 0; i < map.Nodes.Count; i++)
            {
                var n = map.Nodes[i];
                Assert.AreNotEqual(MapNodeType.Elite, n.Type);
                Assert.AreNotEqual(MapNodeType.Event, n.Type);
                if (n.Type == MapNodeType.Battle)
                    Assert.IsFalse(string.IsNullOrEmpty(n.ContentId));
            }
        }

        [Test]
        public void GetAvailableNodes_StartsWithFirstRow_ThenFollowsEdges()
        {
            var map = Gen(11);
            var buffer = new List<int>();

            map.GetAvailableNodes(-1, buffer);
            CollectionAssert.AreEquivalent(map.Rows[0], buffer, "起点应当可以任选第一行的节点");

            int first = map.Rows[0][0];
            map.GetAvailableNodes(first, buffer);
            CollectionAssert.AreEquivalent(map.GetNode(first).Next, buffer);

            Assert.IsFalse(map.IsAvailable(first, map.Boss.Id), "不该能从第一行直接跳到 Boss");
        }
    }
}
