using System.Collections.Generic;
using Game.Battle;
using Game.Cards;
using Game.Core;
using Game.Units;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 所有战斗测试的公共脚手架。
    /// ★ 这里没有任何 UI，也没有协程——战斗系统能完整跑完，就是架构承诺的核心兑现。
    /// </summary>
    public abstract class BattleTestFixture
    {
        protected TestContent Content;
        protected RunContext Run;
        protected BattleController Ctrl;
        protected BattleContext Ctx => Ctrl.Ctx;
        protected BattleUnit Player => Ctx.Player;

        [SetUp]
        public void BaseSetUp()
        {
            Content = TestContent.Build();

            // ★ 阶段 4 起 BattleController 是纯 C# 类，测试不再需要建 GameObject。
            Ctrl = new BattleController();
        }

        [TearDown]
        public void BaseTearDown()
        {
            Content?.Dispose();
        }

        /// <summary>造一局游戏。deck 用 "cardId xN" 的形式描述。</summary>
        protected RunContext MakeRun(int seed = 12345, int maxHp = 80, params (string id, int count)[] deck)
        {
            Run = new RunContext(seed, Content.Db)
            {
                MaxHp = maxHp,
                Hp = maxHp,
                EnergyPerTurn = 3,
                CardsPerTurn = 5,
            };

            if (deck == null || deck.Length == 0)
            {
                deck = new[] { ("strike", 5), ("defend", 5) };
            }

            for (int i = 0; i < deck.Length; i++)
                Run.AddCards(Content.Cards[deck[i].id], deck[i].count);

            return Run;
        }

        protected void StartBattle(string encounterId = "dummy", int seed = 12345,
                                   int maxHp = 80, params (string id, int count)[] deck)
        {
            MakeRun(seed, maxHp, deck);
            Ctrl.StartBattle(Run, Content.Encounters[encounterId]);
        }

        protected BattleUnit Enemy(int index = 0)
        {
            int n = 0;
            for (int i = 0; i < Ctx.AllUnits.Count; i++)
            {
                if (Ctx.AllUnits[i].IsPlayer) continue;
                if (n == index) return Ctx.AllUnits[i];
                n++;
            }
            return null;
        }

        /// <summary>把一张牌塞进手牌（绕过抽牌），方便精确测试。</summary>
        protected CardInstance GiveCard(string cardId)
        {
            var inst = Run.NewCard(Content.Cards[cardId]);
            Ctx.Deck.Hand.Add(inst);
            return inst;
        }

        protected CardInstance HandCard(string cardId)
        {
            var c = Ctx.Deck.FindInHand(cardId);
            Assert.IsNotNull(c, $"手牌里没有「{cardId}」");
            return c;
        }

        protected void PlayCard(string cardId, BattleUnit target = null)
        {
            var c = Ctx.Deck.FindInHand(cardId) ?? GiveCard(cardId);
            Assert.IsTrue(Ctrl.TryPlayCard(c, target, out var reason), $"打不出「{cardId}」：{reason}");
        }

        protected List<BattleEventType> DrainEventTypes()
        {
            var list = new List<BattleEventType>();
            while (Ctx.Events.Count > 0) list.Add(Ctx.Events.Dequeue().Type);
            return list;
        }

        /// <summary>
        /// 取出整条事件（不只是类型）。表现层要靠事件里的 Kind / Flags 决定播什么，
        /// 那些字段只有在这里才断言得到。
        /// </summary>
        protected List<BattleEvent> DrainEvents()
        {
            var list = new List<BattleEvent>();
            while (Ctx.Events.Count > 0) list.Add(Ctx.Events.Dequeue());
            return list;
        }

        /// <summary>取某一类事件里的最后一条。找不到返回 null。</summary>
        protected BattleEvent? LastEventOf(List<BattleEvent> events, BattleEventType type)
        {
            for (int i = events.Count - 1; i >= 0; i--)
                if (events[i].Type == type) return events[i];
            return null;
        }
    }
}
