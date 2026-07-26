using System.Collections.Generic;
using Game.Core;
using Game.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 战斗奖励 / 宝箱界面。金币、三选一卡牌、遗物各是一行，领过的行会置灰。
    /// ★ 卡牌三选一复用 <see cref="CardPickerScreen"/>，不再单写一套选牌 UI。
    /// </summary>
    public class RewardScreen : ScreenBase
    {
        private RectTransform _list;
        private Button _goldButton;
        private Button _cardButton;
        private Button _relicButton;
        private Button _potionButton;
        private Button _leaveButton;

        private BattleReward Reward => Run.PendingReward;

        protected override void Build()
        {
            bool treasure = Run.Phase == RunPhase.Treasure;

            var title = UIFactory.CreateText(Root, "Title", treasure ? Loc.T("ui.reward.title_treasure", "宝　箱") : Loc.T("ui.reward.title_battle", "战斗奖励"), 44,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.7f));
            UIFactory.SetAnchored(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -150), new Vector2(0, -80));

            _list = UIFactory.CreateScrollView(Root, "Rewards", 14f);
            UIFactory.SetAnchored((RectTransform)_list.parent, new Vector2(0.5f, 0), new Vector2(0.5f, 1),
                new Vector2(-420, 130), new Vector2(420, -160));

            if (Reward == null)
            {
                UIFactory.CreateText(Root, "Empty", Loc.T("ui.reward.empty", "没有奖励。"), 26);
            }
            else
            {
                if (Reward.Gold > 0)
                    _goldButton = AddRow(Loc.T("ui.reward.gold", "◆ {0} 金币", Reward.Gold), new Color(0.42f, 0.36f, 0.16f), TakeGold);

                if (Reward.CardChoices.Count > 0)
                    _cardButton = AddRow(Loc.T("ui.reward.card_choices", "▤ 卡牌三选一（{0} 张候选）", Reward.CardChoices.Count),
                        new Color(0.22f, 0.32f, 0.46f), TakeCard);

                if (Reward.Relic != null)
                    _relicButton = AddRow(Loc.T("ui.reward.relic", "✦ 遗物：{0}", Reward.Relic.LocalizedName),
                        new Color(0.36f, 0.26f, 0.44f), TakeRelic);

                if (Reward.Potion != null)
                    _potionButton = AddRow(PotionLabel(), new Color(0.20f, 0.40f, 0.34f), TakePotion);
            }

            _leaveButton = UIFactory.CreateTextButton(Root, "Leave", Loc.T("ui.reward.leave", "离　开"), 30,
                new Color(0.30f, 0.36f, 0.42f), () => Manager.ReturnToMap());
            var rt = (RectTransform)_leaveButton.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(340, 80);
            rt.anchoredPosition = new Vector2(0, 30);

            RefreshRows();
        }

        private Button AddRow(string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var btn = UIFactory.CreateTextButton(_list, "Row", label, 26, color, onClick);
            UIFactory.SetLayoutHeight((RectTransform)btn.transform, 78);
            return btn;
        }

        // ================================================================= 领取

        private void TakeGold()
        {
            if (Reward == null || Reward.GoldTaken) return;
            Run.Gold += Reward.Gold;
            Reward.GoldTaken = true;
            RefreshRows();
        }

        private void TakeCard()
        {
            if (Reward == null || Reward.CardTaken) return;

            App.ShowCardPicker(Loc.T("run.addcard.choose", "选择一张卡加入牌库"), null, Reward.CardChoices,
                pickCount: 1, cancellable: true, onConfirm: picks =>
                {
                    if (picks.Count > 0)
                    {
                        var def = Reward.CardChoices[picks[0]];
                        Run.AddCard(def);
                    }
                    // 跳过也算领过——否则玩家可以反复打开面板看牌
                    Reward.CardTaken = true;
                    RefreshRows();
                });
        }

        private void TakeRelic()
        {
            if (Reward == null || Reward.RelicTaken || Reward.Relic == null) return;
            Run.AddRelic(Reward.Relic);
            Reward.RelicTaken = true;
            RefreshRows();
        }

        /// <summary>
        /// 背包满时把原因写在标签上。★ 只是把按钮置灰而不说为什么，
        /// 玩家会以为奖励界面坏了。
        /// </summary>
        private string PotionLabel()
        {
            if (Reward?.Potion == null) return "";
            string s = Loc.T("ui.reward.potion", "♒ 药水：{0}", Reward.Potion.LocalizedName);
            if (!Reward.PotionTaken && !Run.HasPotionSpace) s += Loc.T("ui.reward.potion_full_suffix", "　（药水槽已满）");
            return s;
        }

        private void TakePotion()
        {
            if (Reward?.Potion == null || Reward.PotionTaken) return;
            if (!Run.HasPotionSpace) return;

            Run.AddPotion(Reward.Potion);
            Reward.PotionTaken = true;
            RefreshRows();
        }

        private void RefreshRows()
        {
            if (Reward == null) return;

            SetRow(_goldButton, !Reward.GoldTaken, new Color(0.42f, 0.36f, 0.16f), Loc.T("ui.reward.gold", "◆ {0} 金币", Reward.Gold));
            SetRow(_cardButton, !Reward.CardTaken, new Color(0.22f, 0.32f, 0.46f), Loc.T("ui.reward.card_choices_short", "▤ 卡牌三选一"));
            SetRow(_relicButton, !Reward.RelicTaken, new Color(0.36f, 0.26f, 0.44f),
                Reward.Relic != null ? Loc.T("ui.reward.relic", "✦ 遗物：{0}", Reward.Relic.LocalizedName) : "");
            // ★ 药水行不能套用「已领取」后缀：它变灰的原因可能是槽位满而不是已领。
            //   标签在 PotionLabel() 里已经写清楚了，这里不再追加任何后缀。
            SetRow(_potionButton, !Reward.PotionTaken && Run.HasPotionSpace,
                new Color(0.20f, 0.40f, 0.34f), PotionLabel(),
                disabledSuffix: Reward.PotionTaken ? Loc.T("ui.reward.taken_suffix", "　（已领取）") : "");

            UIFactory.LabelOf(_leaveButton).text = Reward.AllTaken ? Loc.T("ui.reward.leave", "离　开") : Loc.T("ui.reward.skip_rest", "跳过剩余奖励");
        }

        /// <summary>
        /// ★ <paramref name="disabledSuffix"/> 的默认值从字面量改成了 null：
        ///   可选参数的默认值必须是编译期常量，而 <c>Loc.T(...)</c> 是运行时调用。
        ///   传 null 表示「用默认的『已领取』后缀」，传空串表示「不要后缀」。
        /// </summary>
        private static void SetRow(Button btn, bool available, Color color, string label,
                                   string disabledSuffix = null)
        {
            if (btn == null) return;
            UIFactory.SetInteractable(btn, available, color);
            var text = UIFactory.LabelOf(btn);
            if (text == null) return;

            string suffix = disabledSuffix ?? Loc.T("ui.reward.taken_suffix", "　（已领取）");
            text.text = available ? label : label + suffix;
        }
    }
}
