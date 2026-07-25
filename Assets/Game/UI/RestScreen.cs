using System.Collections.Generic;
using Game.Cards;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 休息点。两个互斥的选项：休整（回血）或打磨（升级一张牌）。
    /// 逻辑刻意写死而不是走 EventDefinition——休息点是固定机制，不是随机内容，
    /// 硬做成事件反而要为了「不能随机到别的选项」加一堆特判。
    /// </summary>
    public class RestScreen : ScreenBase
    {
        public const int HealPercent = 30;

        private Button _restButton;
        private Button _upgradeButton;
        private Text _resultText;
        private Button _leaveButton;

        private bool _used;

        protected override void Build()
        {
            var title = UIFactory.CreateText(Root, "Title", "篝　火", 44,
                TextAnchor.MiddleCenter, new Color(1f, 0.86f, 0.55f));
            UIFactory.SetAnchored(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -150), new Vector2(0, -80));

            var body = UIFactory.CreateText(Root, "Body",
                "火堆还在燃烧。你可以在这里歇一口气，或是借着火光打磨自己的技艺。", 24,
                TextAnchor.MiddleCenter, new Color(0.86f, 0.88f, 0.92f));
            UIFactory.SetAnchored(body.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-460, -240), new Vector2(460, -160));

            int healAmount = Mathf.Max(1, Run.MaxHp * HealPercent / 100);
            int actualHeal = Mathf.Min(healAmount, Run.MaxHp - Run.Hp);

            _restButton = UIFactory.CreateTextButton(Root, "Rest",
                $"休　整　—　回复 {healAmount} 点生命", 28, new Color(0.30f, 0.44f, 0.32f), DoRest);
            Place((RectTransform)_restButton.transform, 60);

            _upgradeButton = UIFactory.CreateTextButton(Root, "Upgrade",
                "打　磨　—　升级一张牌", 28, new Color(0.30f, 0.36f, 0.50f), DoUpgrade);
            Place((RectTransform)_upgradeButton.transform, -50);

            _resultText = UIFactory.CreateText(Root, "Result", "", 26,
                TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.6f));
            UIFactory.SetAnchored(_resultText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-460, -190), new Vector2(460, -120));

            _leaveButton = UIFactory.CreateTextButton(Root, "Leave", "离　开", 30,
                new Color(0.30f, 0.36f, 0.42f), () => Manager.ReturnToMap());
            var rt = (RectTransform)_leaveButton.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(340, 80);
            rt.anchoredPosition = new Vector2(0, 40);
            _leaveButton.gameObject.SetActive(false);

            // 已经满血 / 没有可升级的牌时，对应选项直接不可用，别让玩家白点
            if (actualHeal <= 0)
                UIFactory.SetInteractable(_restButton, false, new Color(0.30f, 0.44f, 0.32f));

            var upgradable = new List<CardInstance>();
            Run.GetUpgradableCards(upgradable);
            if (upgradable.Count == 0)
                UIFactory.SetInteractable(_upgradeButton, false, new Color(0.30f, 0.36f, 0.50f));
        }

        private static void Place(RectTransform rt, float y)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(560, 90);
            rt.anchoredPosition = new Vector2(0, y);
        }

        // ================================================================= 选项

        private void DoRest()
        {
            if (_used) return;
            _used = true;

            int amount = Mathf.Max(1, Run.MaxHp * HealPercent / 100);
            int actual = Run.ModifyHp(amount);

            _resultText.text = $"你休整了一会儿，回复了 {actual} 点生命。";
            Finish();
        }

        private void DoUpgrade()
        {
            if (_used) return;

            var upgradable = new List<CardInstance>();
            Run.GetUpgradableCards(upgradable);
            if (upgradable.Count == 0) return;

            App.ShowCardPicker("选择要升级的卡", upgradable, null, 1, true, picks =>
            {
                if (picks.Count == 0) return;   // 取消了就还能再选，不算用掉

                _used = true;
                var card = upgradable[picks[0]];
                card.Upgrade();
                _resultText.text = $"「{card.DisplayName}」已经升级。";
                Finish();
            });
        }

        private void Finish()
        {
            UIFactory.SetInteractable(_restButton, false, new Color(0.30f, 0.44f, 0.32f));
            UIFactory.SetInteractable(_upgradeButton, false, new Color(0.30f, 0.36f, 0.50f));
            _leaveButton.gameObject.SetActive(true);
        }
    }
}
