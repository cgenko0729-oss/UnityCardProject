using Game.Battle;
using Game.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 把战斗界面嵌进局外流程。本身几乎没有逻辑——
    /// 战斗的一切归 BattleScreen / BattleController，本类只负责在战斗结束后给一个「继续」按钮。
    ///
    /// ★ 为什么要玩家点一下而不是自动跳转：<c>BattleFinished</c> 触发时表现层还在播最后几个事件，
    ///   立刻切界面玩家会看不到致命一击。RunManager 因此把「确认」拆成了单独一步。
    /// </summary>
    public class BattleHostScreen : ScreenBase
    {
        /// <summary>
        /// 战斗中不显示「查看卡组」。
        ///
        /// ★ 不是因为麻烦，是因为那颗按钮在战斗里**语义有歧义**：
        ///   <c>RunContext.Deck</c> 是母牌组，而场上四堆的总和 = 母牌组 + 本场战斗中生成的牌
        ///   （<c>AddCardEffect</c> 造出来的状态牌 / 诅咒牌是新的 <c>CardInstance</c>，不在母牌组里）。
        ///   两个数字对不上，玩家只会当成 bug。
        ///   战斗里要看牌，左下角那三颗牌堆按钮才是正确答案。
        /// </summary>
        public override bool ShowDeckButton => false;

        private BattleScreen _battleScreen;
        private Button _continueButton;

        protected override void Build()
        {
            var controller = Manager.Battle;
            if (controller == null)
            {
                UIFactory.CreateText(Root, "NoBattle", Loc.T("ui.battlehost.none", "没有进行中的战斗。"), 28);
                return;
            }

            // 战斗界面自己的根节点，作为本界面的子节点——本界面被销毁时它必然跟着消失
            var battleRoot = UIFactory.CreateEmpty(Root, "BattleScreen");
            UIFactory.Stretch(battleRoot);
            _battleScreen = battleRoot.gameObject.AddComponent<BattleScreen>();
            _battleScreen.Bind(controller, battleRoot);

            _continueButton = UIFactory.CreateTextButton(Root, "Continue", Loc.T("ui.battlehost.continue", "继　续"), 32,
                new Color(0.32f, 0.42f, 0.30f), OnContinue);
            var rt = (RectTransform)_continueButton.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(320, 90);
            rt.anchoredPosition = new Vector2(0, -160);
            _continueButton.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_continueButton == null || _battleScreen == null) return;

            // 结算面板出现 = 事件队列已经播完，这时候才允许离开
            bool show = _battleScreen.ResultVisible;
            if (_continueButton.gameObject.activeSelf != show)
                _continueButton.gameObject.SetActive(show);
        }

        private void OnContinue()
        {
            _continueButton.interactable = false;
            Manager.AcknowledgeBattleEnd();
        }
    }
}
