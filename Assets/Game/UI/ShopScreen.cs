using System.Collections.Generic;
using Game.Cards;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 商店界面。库存来自 <see cref="RunContext.ShopStocks"/>——
    /// ★ 绝不能在这里现生成，否则玩家反复进出商店就能刷到想要的商品。
    /// </summary>
    public class ShopScreen : ScreenBase
    {
        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<ShopItem> _items = new List<ShopItem>();
        private RectTransform _list;
        private TMP_Text _hint;

        private ShopStock Stock => Manager.CurrentShop;

        protected override void Build()
        {
            var title = UIFactory.CreateText(Root, "Title", "商　店", 44,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.7f));
            UIFactory.SetAnchored(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -140), new Vector2(0, -76));

            _list = UIFactory.CreateScrollView(Root, "Items", 10f);
            UIFactory.SetAnchored((RectTransform)_list.parent, new Vector2(0.5f, 0), new Vector2(0.5f, 1),
                new Vector2(-480, 150), new Vector2(480, -150));

            var stock = Stock;
            if (stock == null || stock.Items.Count == 0)
            {
                UIFactory.CreateText(Root, "Empty", "店主今天没开门。", 26);
            }
            else
            {
                for (int i = 0; i < stock.Items.Count; i++)
                {
                    int index = i;
                    var item = stock.Items[i];
                    var btn = UIFactory.CreateTextButton(_list, "Item" + i, "", 24,
                        ColorOf(item), () => Buy(index));
                    UIFactory.SetLayoutHeight((RectTransform)btn.transform, 70);

                    var label = UIFactory.LabelOf(btn);
                    UIFactory.SetAlignment(label, TextAnchor.MiddleLeft);
                    var lrt = label.rectTransform;
                    lrt.offsetMin = new Vector2(18, 0);
                    lrt.offsetMax = new Vector2(-18, 0);

                    _buttons.Add(btn);
                    _items.Add(item);
                }
            }

            _hint = UIFactory.CreateText(Root, "Hint", "", 22,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.55f));
            UIFactory.SetAnchored(_hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 108), new Vector2(0, 146));

            var leave = UIFactory.CreateTextButton(Root, "Leave", "离　开", 30,
                new Color(0.30f, 0.36f, 0.42f), () => Manager.ReturnToMap());
            var rt = (RectTransform)leave.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(340, 80);
            rt.anchoredPosition = new Vector2(0, 24);

            RefreshRows();
        }

        private static Color ColorOf(ShopItem item)
        {
            if (item.IsCardRemoval) return new Color(0.36f, 0.22f, 0.28f);
            if (item.Relic != null) return new Color(0.36f, 0.26f, 0.44f);
            if (item.Potion != null) return new Color(0.20f, 0.40f, 0.34f);
            return new Color(0.22f, 0.32f, 0.46f);
        }

        // ================================================================= 购买

        private void Buy(int index)
        {
            var item = _items[index];
            if (item.Sold) return;

            if (Run.Gold < item.Price)
            {
                ShowHint($"金币不足，还差 {item.Price - Run.Gold}。");
                return;
            }

            if (item.IsCardRemoval) { BuyCardRemoval(item); return; }

            // ★ 槽位检查必须在扣钱之前：先扣钱再发现装不下，玩家等于白付钱。
            if (item.Potion != null && !Run.HasPotionSpace)
            {
                ShowHint("药水槽已满，先喝掉或倒掉一瓶再来。");
                return;
            }

            Run.Gold -= item.Price;
            item.Sold = true;

            if (item.Card != null)
            {
                Run.AddCard(item.Card);
                ShowHint($"买下了「{item.Card.DisplayName}」。");
            }
            else if (item.Relic != null)
            {
                Run.AddRelic(item.Relic);
                ShowHint($"买下了遗物「{item.Relic.DisplayName}」。");
            }
            else if (item.Potion != null)
            {
                Run.AddPotion(item.Potion);
                ShowHint($"买下了药水「{item.Potion.DisplayName}」。");
            }

            RefreshRows();
        }

        private void BuyCardRemoval(ShopItem item)
        {
            if (Run.Deck.Count <= 1)
            {
                ShowHint("牌库里的牌太少了，不能再移除。");
                return;
            }

            App.ShowCardPicker("选择要移除的卡", Run.Deck, null,
                pickCount: 1, cancellable: true, onConfirm: picks =>
                {
                    if (picks.Count == 0) return;   // 取消不扣钱

                    // ★ 扣钱放在玩家真的选完之后：先扣钱再让玩家取消是最容易被投诉的实现
                    Run.Gold -= item.Price;
                    var card = Run.Deck[picks[0]];
                    Run.RemoveCard(card);
                    Run.CardRemovalsPurchased++;
                    item.Sold = true;

                    ShowHint($"移除了「{card.DisplayName}」。");
                    RefreshRows();
                });
        }

        private void ShowHint(string text)
        {
            if (_hint != null) _hint.text = text;
        }

        private void RefreshRows()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                var item = _items[i];
                var btn = _buttons[i];

                bool full = item.Potion != null && !Run.HasPotionSpace;
                bool affordable = !item.Sold && Run.Gold >= item.Price && !full;
                UIFactory.SetInteractable(btn, affordable, ColorOf(item));

                var label = UIFactory.LabelOf(btn);
                if (label == null) continue;

                if (item.Sold) label.text = $"{item.DisplayName}　—　已售出";
                else if (full) label.text = $"{Kind(item)}　{item.DisplayName}　—　◆ {item.Price}　（药水槽已满）";
                else label.text = $"{Kind(item)}　{item.DisplayName}　—　◆ {item.Price}";
            }
        }

        private static string Kind(ShopItem item)
        {
            if (item.IsCardRemoval) return "[服务]";
            if (item.Relic != null) return "[遗物]";
            if (item.Potion != null) return "[药水]";
            return "[卡牌]";
        }

        private void LateUpdate()
        {
            // 金币会因为购买而变化，可购买状态要跟着变
            RefreshRows();
        }
    }
}
