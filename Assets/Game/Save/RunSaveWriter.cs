using System.Collections.Generic;
using Game.Core;
using Game.Map;

namespace Game.Save
{
    /// <summary>
    /// <see cref="RunContext"/> → <see cref="RunSave"/>。
    ///
    /// ★ 纯函数：不碰文件、不碰 Unity API、不写日志。
    ///   存档系统的全部风险都集中在这里和 <see cref="RunSaveReader"/>，
    ///   拆成纯函数就是为了让它们 100% 能在 EditMode 里被断言。
    /// </summary>
    public static class RunSaveWriter
    {
        public static RunSave Write(RunContext run)
        {
            if (run == null) return null;

            var save = new RunSave
            {
                Version = SaveConstants.CurrentVersion,
                Seed = run.Seed,

                Hp = run.Hp,
                MaxHp = run.MaxHp,
                Gold = run.Gold,
                EnergyPerTurn = run.EnergyPerTurn,
                CardsPerTurn = run.CardsPerTurn,
                PotionSlots = run.PotionSlots,

                NextCardUid = run.PeekNextCardUid,
                NextPotionUid = run.PeekNextPotionUid,

                CurrentNodeId = run.CurrentNodeId,
                Phase = run.Phase,

                BattlesWon = run.BattlesWon,
                LastBattleVictory = run.LastBattleVictory,
                LastEncounterId = run.LastEncounter != null ? run.LastEncounter.Id : null,

                PendingBattleEncounterId = run.PendingBattleEncounterId,
                PendingBattleGivesReward = run.PendingBattleGivesReward,
                ActiveBattleEncounterId = run.ActiveBattleEncounterId,

                CardRemovalsPurchased = run.CardRemovalsPurchased,

                Map = WriteMap(run.Map),
                PendingReward = WriteReward(run.PendingReward),
            };

            WriteRng(run.Rng, save.RngStates);

            for (int i = 0; i < run.Deck.Count; i++)
            {
                var c = run.Deck[i];
                if (c == null || c.Def == null) continue;
                save.Deck.Add(new CardSave
                {
                    Uid = c.Uid,
                    // ★ 当前 Def 的 Id，不是基础版的——见 CardSave 的注释
                    DefId = c.Def.Id,
                    UpgradeLevel = c.UpgradeLevel,
                });
            }

            for (int i = 0; i < run.Relics.Count; i++)
            {
                var r = run.Relics[i];
                if (r == null || r.Def == null) continue;
                save.Relics.Add(new RelicSave { Id = r.Def.Id, Counter = r.Counter });
            }

            for (int i = 0; i < run.Potions.Count; i++)
            {
                var p = run.Potions[i];
                if (p == null || p.Def == null) continue;
                save.Potions.Add(new PotionSave { Uid = p.Uid, Id = p.Def.Id });
            }

            save.VisitedNodeIds.AddRange(run.VisitedNodeIds);

            foreach (var kv in run.ShopStocks)
            {
                if (kv.Value == null) continue;
                save.ShopStocks[kv.Key] = WriteShop(kv.Value);
            }

            return save;
        }

        private static void WriteRng(Rng rng, List<RngStreamSave> into)
        {
            if (rng == null) return;
            var states = rng.Save();
            for (int i = 0; i < states.Count; i++)
                into.Add(new RngStreamSave { Stream = states[i].Stream, State = states[i].State });
        }

        private static MapSave WriteMap(GameMap map)
        {
            if (map == null) return null;

            var save = new MapSave();

            for (int i = 0; i < map.Nodes.Count; i++)
            {
                var n = map.Nodes[i];
                if (n == null) continue;

                var node = new MapNodeSave
                {
                    Id = n.Id,
                    Row = n.Row,
                    Column = n.Column,
                    Type = n.Type,
                    ContentId = n.ContentId,
                };
                node.Next.AddRange(n.Next);
                node.Prev.AddRange(n.Prev);
                save.Nodes.Add(node);
            }

            for (int r = 0; r < map.Rows.Count; r++)
                save.Rows.Add(new List<int>(map.Rows[r]));

            return save;
        }

        private static RewardSave WriteReward(BattleReward reward)
        {
            if (reward == null) return null;

            var save = new RewardSave
            {
                Gold = reward.Gold,
                RelicId = reward.Relic != null ? reward.Relic.Id : null,
                PotionId = reward.Potion != null ? reward.Potion.Id : null,
                CardTaken = reward.CardTaken,
                GoldTaken = reward.GoldTaken,
                RelicTaken = reward.RelicTaken,
                PotionTaken = reward.PotionTaken,
            };

            for (int i = 0; i < reward.CardChoices.Count; i++)
                if (reward.CardChoices[i] != null)
                    save.CardChoiceIds.Add(reward.CardChoices[i].Id);

            return save;
        }

        private static ShopStockSave WriteShop(ShopStock stock)
        {
            var save = new ShopStockSave();

            for (int i = 0; i < stock.Items.Count; i++)
            {
                var it = stock.Items[i];
                if (it == null) continue;
                save.Items.Add(new ShopItemSave
                {
                    CardId = it.Card != null ? it.Card.Id : null,
                    RelicId = it.Relic != null ? it.Relic.Id : null,
                    PotionId = it.Potion != null ? it.Potion.Id : null,
                    IsCardRemoval = it.IsCardRemoval,
                    Price = it.Price,
                    Sold = it.Sold,
                });
            }

            return save;
        }
    }
}
