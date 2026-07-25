using System.Collections.Generic;
using Game.Core;
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
        private Button _leaveButton;

        private BattleReward Reward => Run.PendingReward;

        protected override void Build()
        {
            bool treasure = Run.Phase == RunPhase.Treasure;

            var title = UIFactory.CreateText(Root, "Title", treasure ? "宝　箱" : "战斗奖励", 44,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.7f));
            UIFactory.SetAnchored(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -150), new Vector2(0, -80));

            _list = UIFactory.CreateScrollView(Root, "Rewards", 14f);
            UIFactory.SetAnchored((RectTransform)_list.parent, new Vector2(0.5f, 0), new Vector2(0.5f, 1),
                new Vector2(-420, 130), new Vector2(420, -160));

            if (Reward == null)
            {
                UIFactory.CreateText(Root, "Empty", "没有奖励。", 26);
            }
            else
            {
                if (Reward.Gold > 0)
                    _goldButton = AddRow($"◆ {Reward.Gold} 金币", new Color(0.42f, 0.36f, 0.16f), TakeGold);

                if (Reward.CardChoices.Count > 0)
                    _cardButton = AddRow($"▤ 卡牌三选一（{Reward.CardChoices.Count} 张候选）",
                        new Color(0.22f, 0.32f, 0.46f), TakeCard);

                if (Reward.Relic != null)
                    _relicButton = AddRow($"✦ 遗物：{Reward.Relic.DisplayName}",
                        new Color(0.36f, 0.26f, 0.44f), TakeRelic);
            }

            _leaveButton = UIFactory.CreateTextButton(Root, "Leave", "离　开", 30,
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

            App.ShowCardPicker("选择一张卡加入牌库", null, Reward.CardChoices,
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

        private void RefreshRows()
        {
            if (Reward == null) return;

            SetRow(_goldButton, !Reward.GoldTaken, new Color(0.42f, 0.36f, 0.16f), $"◆ {Reward.Gold} 金币");
            SetRow(_cardButton, !Reward.CardTaken, new Color(0.22f, 0.32f, 0.46f), "▤ 卡牌三选一");
            SetRow(_relicButton, !Reward.RelicTaken, new Color(0.36f, 0.26f, 0.44f),
                Reward.Relic != null ? $"✦ 遗物：{Reward.Relic.DisplayName}" : "");

            UIFactory.LabelOf(_leaveButton).text = Reward.AllTaken ? "离　开" : "跳过剩余奖励";
        }

        private static void SetRow(Button btn, bool available, Color color, string label)
        {
            if (btn == null) return;
            UIFactory.SetInteractable(btn, available, color);
            var text = UIFactory.LabelOf(btn);
            if (text != null) text.text = available ? label : label + "　（已领取）";
        }
    }
}
