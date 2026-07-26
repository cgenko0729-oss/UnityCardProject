using System.Collections.Generic;
using Game.Cards;
using Game.Core;
using Game.Map;
using Game.Relics;

namespace Game.Save
{
    /// <summary>
    /// <see cref="RunSave"/> + <see cref="GameDatabase"/> → <see cref="RunContext"/>。
    ///
    /// ★ 纯函数：不碰文件、不碰 Unity API。缺内容不抛异常、不打日志，
    ///   而是往 <c>warnings</c> 里追加人话，由调用方决定怎么呈现——
    ///   测试要断言「缺了哪几样」，运行时要把它们打进 Console，两边需求不同。
    ///
    /// <para>★★ 内容缺失的策略是**跳过该项并继续**（使用者拍板）：
    ///   开发期天天在改卡池，一改内容就作废自己正在跑的那一局是不可接受的。
    ///   代价是牌库可能会少一张牌，所以每一次跳过都必须留下 warning，
    ///   否则「我的牌怎么少了」就变成一桩无头案。</para>
    /// </summary>
    public static class RunSaveReader
    {
        public static RunContext Read(RunSave save, GameDatabase db, List<string> warnings = null)
        {
            if (save == null || db == null) return null;

            // 地图是唯一没得商量的东西：没有它就没有「玩家现在在哪」，整份存档失去意义
            if (save.Map == null || save.Map.Nodes.Count == 0)
            {
                Warn(warnings, "存档里没有地图数据，无法读档。");
                return null;
            }

            var run = new RunContext(save.Seed, db)
            {
                Hp = save.Hp,
                MaxHp = save.MaxHp,
                Gold = save.Gold,
                EnergyPerTurn = save.EnergyPerTurn,
                CardsPerTurn = save.CardsPerTurn,
                PotionSlots = save.PotionSlots,

                CurrentNodeId = save.CurrentNodeId,
                Phase = save.Phase,

                BattlesWon = save.BattlesWon,
                LastBattleVictory = save.LastBattleVictory,

                PendingBattleEncounterId = save.PendingBattleEncounterId,
                PendingBattleGivesReward = save.PendingBattleGivesReward,
                ActiveBattleEncounterId = save.ActiveBattleEncounterId,

                CardRemovalsPurchased = save.CardRemovalsPurchased,
            };

            ReadRng(run.Rng, save.RngStates);

            // ★ Uid 计数器必须在建任何牌之前恢复到位，否则下面 Restore 出来的牌
            //   与「读档后新造的牌」会撞号
            run.EnsureCardUidAtLeast(save.NextCardUid);
            run.EnsurePotionUidAtLeast(save.NextPotionUid);

            run.LastEncounter = db.GetEncounter(save.LastEncounterId);
            if (run.LastEncounter == null && !string.IsNullOrEmpty(save.LastEncounterId))
                Warn(warnings, $"找不到上一场战斗「{save.LastEncounterId}」，奖励规格改用普通战斗。");

            ReadDeck(run, save, db, warnings);
            ReadRelics(run, save, db, warnings);
            ReadPotions(run, save, db, warnings);

            run.Map = ReadMap(save.Map, db, warnings);
            run.VisitedNodeIds.AddRange(save.VisitedNodeIds);

            run.PendingReward = ReadReward(save.PendingReward, db, warnings);

            foreach (var kv in save.ShopStocks)
            {
                if (kv.Value == null) continue;
                run.ShopStocks[kv.Key] = ReadShop(kv.Value, db, warnings);
            }

            return run;
        }

        private static void ReadRng(Rng rng, List<RngStreamSave> saved)
        {
            if (rng == null || saved == null) return;

            var states = new List<Rng.StreamState>(saved.Count);
            for (int i = 0; i < saved.Count; i++)
                states.Add(new Rng.StreamState { Stream = saved[i].Stream, State = saved[i].State });

            // ★ Restore 只覆盖存档里出现过的流。将来新增一条 RngStream，
            //   老存档里没有它，它会保持构造时 Hash(seed, stream+1) 的初值——
            //   正是我们想要的行为，老存档天然兼容，不需要迁移。
            rng.Restore(states);
        }

        private static void ReadDeck(RunContext run, RunSave save, GameDatabase db, List<string> warnings)
        {
            for (int i = 0; i < save.Deck.Count; i++)
            {
                var c = save.Deck[i];
                if (c == null) continue;

                var def = db.GetCard(c.DefId);
                if (def == null)
                {
                    Warn(warnings, $"找不到卡牌「{c.DefId}」，已从牌库跳过。");
                    continue;
                }

                run.Deck.Add(CardInstance.Restore(c.Uid, def, c.UpgradeLevel));
            }
        }

        private static void ReadRelics(RunContext run, RunSave save, GameDatabase db, List<string> warnings)
        {
            for (int i = 0; i < save.Relics.Count; i++)
            {
                var r = save.Relics[i];
                if (r == null) continue;

                var def = db.GetRelic(r.Id);
                if (def == null)
                {
                    Warn(warnings, $"找不到遗物「{r.Id}」，已跳过。");
                    continue;
                }

                // 不走 RunContext.AddRelic：它会新建一个 Counter 为 0 的实例，
                // 而跨战斗计数（铁律 12）就活在 Counter 里
                run.Relics.Add(new RelicInstance(def) { Counter = r.Counter });
            }
        }

        private static void ReadPotions(RunContext run, RunSave save, GameDatabase db, List<string> warnings)
        {
            for (int i = 0; i < save.Potions.Count; i++)
            {
                var p = save.Potions[i];
                if (p == null) continue;

                var def = db.GetPotion(p.Id);
                if (def == null)
                {
                    Warn(warnings, $"找不到药水「{p.Id}」，已跳过。");
                    continue;
                }

                // 同样不走 AddPotion：那会重新分配 Uid，并且受 PotionSlots 限制。
                // 存档里的槽位数可能被遗物改大过，而 PotionSlots 是在上面才赋的值——
                // 这里直接还原，不做二次校验。
                run.Potions.Add(new Potions.PotionInstance(p.Uid, def));
            }
        }

        private static GameMap ReadMap(MapSave save, GameDatabase db, List<string> warnings)
        {
            var map = new GameMap();

            for (int i = 0; i < save.Nodes.Count; i++)
            {
                var n = save.Nodes[i];
                if (n == null) continue;

                var node = new MapNode
                {
                    Id = n.Id,
                    Row = n.Row,
                    Column = n.Column,
                    Type = n.Type,
                    ContentId = n.ContentId,
                };
                node.Next.AddRange(n.Next);
                node.Prev.AddRange(n.Prev);
                map.Nodes.Add(node);

                // ★ 节点缺内容不能像卡牌那样「跳过」——跳掉一个节点会让 Nodes 的下标
                //   与 Id 错位，而 GameMap.GetNode 正是拿 Id 当下标用的。
                //   保留节点、只报警告：真走到它时 RunManager.StartBattle 已经有
                //   「找不到战斗配置就回地图」的兜底。
                if (!string.IsNullOrEmpty(n.ContentId) && !ContentExists(n, db))
                    Warn(warnings, $"地图节点 #{n.Id}（{n.Type}）的内容「{n.ContentId}」已不存在。");
            }

            for (int r = 0; r < save.Rows.Count; r++)
                map.Rows.Add(new List<int>(save.Rows[r]));

            return map;
        }

        private static bool ContentExists(MapNodeSave n, GameDatabase db)
        {
            switch (n.Type)
            {
                case MapNodeType.Battle:
                case MapNodeType.Elite:
                case MapNodeType.Boss:
                    return db.GetEncounter(n.ContentId) != null;
                case MapNodeType.Event:
                    return db.GetEvent(n.ContentId) != null;
                default:
                    return true;   // 休息 / 商店 / 宝箱不看 ContentId
            }
        }

        private static BattleReward ReadReward(RewardSave save, GameDatabase db, List<string> warnings)
        {
            if (save == null) return null;

            var reward = new BattleReward
            {
                Gold = save.Gold,
                CardTaken = save.CardTaken,
                GoldTaken = save.GoldTaken,
                RelicTaken = save.RelicTaken,
                PotionTaken = save.PotionTaken,
            };

            for (int i = 0; i < save.CardChoiceIds.Count; i++)
            {
                var def = db.GetCard(save.CardChoiceIds[i]);
                if (def == null)
                {
                    Warn(warnings, $"奖励候选卡「{save.CardChoiceIds[i]}」已不存在，本次三选一少一张。");
                    continue;
                }
                reward.CardChoices.Add(def);
            }

            if (!string.IsNullOrEmpty(save.RelicId))
            {
                reward.Relic = db.GetRelic(save.RelicId);
                if (reward.Relic == null)
                {
                    Warn(warnings, $"奖励遗物「{save.RelicId}」已不存在。");
                    // ★ 必须一并标成「已领」：否则奖励界面会永远停在
                    //   「还有东西没领」的状态，而那件东西根本画不出来
                    reward.RelicTaken = true;
                }
            }

            if (!string.IsNullOrEmpty(save.PotionId))
            {
                reward.Potion = db.GetPotion(save.PotionId);
                if (reward.Potion == null)
                {
                    Warn(warnings, $"奖励药水「{save.PotionId}」已不存在。");
                    reward.PotionTaken = true;
                }
            }

            return reward;
        }

        private static ShopStock ReadShop(ShopStockSave save, GameDatabase db, List<string> warnings)
        {
            var stock = new ShopStock();

            for (int i = 0; i < save.Items.Count; i++)
            {
                var s = save.Items[i];
                if (s == null) continue;

                var item = new ShopItem
                {
                    IsCardRemoval = s.IsCardRemoval,
                    Price = s.Price,
                    Sold = s.Sold,
                };

                if (!s.IsCardRemoval)
                {
                    if (!string.IsNullOrEmpty(s.CardId)) item.Card = db.GetCard(s.CardId);
                    if (!string.IsNullOrEmpty(s.RelicId)) item.Relic = db.GetRelic(s.RelicId);
                    if (!string.IsNullOrEmpty(s.PotionId)) item.Potion = db.GetPotion(s.PotionId);

                    if (item.Card == null && item.Relic == null && item.Potion == null)
                    {
                        Warn(warnings, $"商店商品「{s.CardId ?? s.RelicId ?? s.PotionId}」已不存在，该格已下架。");
                        continue;
                    }
                }

                stock.Items.Add(item);
            }

            return stock;
        }

        private static void Warn(List<string> warnings, string message) => warnings?.Add(message);
    }
}
