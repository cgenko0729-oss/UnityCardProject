using System.Collections.Generic;
using Game.Cards;
using Game.Relics;

namespace Game.Core
{
    /// <summary>商店里的一件商品。</summary>
    public class ShopItem
    {
        public CardDefinition Card;
        public RelicDefinition Relic;

        /// <summary>true 表示这是「删卡服务」而不是实物。</summary>
        public bool IsCardRemoval;

        public int Price;
        public bool Sold;

        public string DisplayName
        {
            get
            {
                if (IsCardRemoval) return "移除一张卡";
                if (Card != null) return Card.DisplayName;
                if (Relic != null) return Relic.DisplayName;
                return "?";
            }
        }
    }

    /// <summary>
    /// 一次商店的库存。★ 在进入商店节点时生成一次并存进 RunContext，
    /// 不能每帧或每次打开重新生成——否则玩家反复进出就能刷到想要的商品。
    /// 全部随机走 <c>RngStream.Shop</c>。
    /// </summary>
    public class ShopStock
    {
        public readonly List<ShopItem> Items = new List<ShopItem>(10);

        public const int CardCount = 5;
        public const int RelicCount = 2;

        public const int CardRemovalBasePrice = 75;

        /// <summary>删卡服务每次使用后涨价，防止无限洗牌库。</summary>
        public const int CardRemovalPriceStep = 25;

        public static int PriceOf(CardRarity rarity) => rarity switch
        {
            CardRarity.Rare => 150,
            CardRarity.Uncommon => 75,
            _ => 50,
        };

        public static int PriceOf(RelicRarity rarity) => rarity switch
        {
            RelicRarity.Boss => 300,
            RelicRarity.Rare => 250,
            RelicRarity.Uncommon => 180,
            RelicRarity.Shop => 200,
            _ => 150,
        };

        public static ShopStock Generate(RunContext run)
        {
            var stock = new ShopStock();
            if (run == null) return stock;

            var cards = new List<CardDefinition>();
            ContentPicker.PickCards(run.Rng, run.Database, RngStream.Shop, CardCount, cards);
            for (int i = 0; i < cards.Count; i++)
            {
                // ±10% 的价格浮动，让每家商店略有不同
                int basePrice = PriceOf(cards[i].Rarity);
                int jitter = run.Rng.Range(RngStream.Shop, -10, 11);
                stock.Items.Add(new ShopItem
                {
                    Card = cards[i],
                    Price = UnityEngine.Mathf.Max(1, basePrice + basePrice * jitter / 100),
                });
            }

            // ★ guard 是必须的：PickRelic 只排除「玩家已拥有」，不排除「本店已上架」，
            //   可选遗物只剩一个时不加保护会死循环。
            int placed = 0;
            for (int guard = 0; placed < RelicCount && guard < RelicCount * 8; guard++)
            {
                var relic = ContentPicker.PickRelic(run.Rng, run.Database, RngStream.Shop, run);
                if (relic == null) break;
                if (stock.HasRelic(relic)) continue;   // 同一家店不重复上架

                stock.Items.Add(new ShopItem
                {
                    Relic = relic,
                    Price = relic.ShopPrice > 0 ? relic.ShopPrice : PriceOf(relic.Rarity),
                });
                placed++;
            }

            stock.Items.Add(new ShopItem
            {
                IsCardRemoval = true,
                Price = CardRemovalBasePrice + run.CardRemovalsPurchased * CardRemovalPriceStep,
            });

            return stock;
        }

        private bool HasRelic(RelicDefinition def)
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Relic == def) return true;
            return false;
        }
    }
}
