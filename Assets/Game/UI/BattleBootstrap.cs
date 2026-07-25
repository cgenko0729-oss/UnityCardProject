using System;
using System.Collections.Generic;
using Game.Battle;
using Game.Core;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 战斗场景的唯一入口组件：拼一个临时 RunContext，创建 BattleController，搭 UI，开打。
    /// 阶段 4 引入 RunManager 之后，这个组件只在「单独调试一场战斗」时使用。
    /// </summary>
    public class BattleBootstrap : MonoBehaviour
    {
        [Serializable]
        public struct DeckEntry
        {
            public string CardId;
            public int Count;
        }

        [Header("数据")]
        public GameDatabase Database;

        [Tooltip("要打哪一场战斗（EncounterDefinition.Id）")]
        public string EncounterId = "slime";

        [Header("玩家")]
        public int MaxHp = 80;
        public int EnergyPerTurn = 3;
        public int CardsPerTurn = 5;

        public List<DeckEntry> StarterDeck = new List<DeckEntry>
        {
            new DeckEntry { CardId = "strike", Count = 5 },
            new DeckEntry { CardId = "defend", Count = 4 },
            new DeckEntry { CardId = "bash", Count = 1 },
        };

        [Header("随机")]
        [Tooltip("0 表示按时间随机生成一个种子")]
        public int Seed = 12345;

        [Header("调试")]
        [Tooltip("每次进入 Play 模式都用同一个种子，便于复现问题")]
        public bool DeterministicSeed = true;

        public BattleController Controller { get; private set; }
        public RunContext Run { get; private set; }

        private void Start()
        {
            if (Database == null)
            {
                Debug.LogError("[BattleBootstrap] 没有指定 GameDatabase。请在 Inspector 里拖入，" +
                               "或先执行菜单 Tools/卡牌游戏/1. 生成示例内容。");
                return;
            }

            Database.Invalidate();

            int seed = DeterministicSeed && Seed != 0 ? Seed : Environment.TickCount;
            Run = new RunContext(seed, Database)
            {
                MaxHp = MaxHp,
                Hp = MaxHp,
                EnergyPerTurn = EnergyPerTurn,
                CardsPerTurn = CardsPerTurn,
                // 单场战斗调试场景同样是真人在点，选牌要弹面板
                InteractivePlayer = true,
            };

            for (int i = 0; i < StarterDeck.Count; i++)
            {
                var def = Database.GetCard(StarterDeck[i].CardId);
                if (def == null)
                {
                    Debug.LogWarning($"[BattleBootstrap] 找不到卡「{StarterDeck[i].CardId}」，已跳过。");
                    continue;
                }
                Run.AddCards(def, StarterDeck[i].Count);
            }

            var encounter = Database.GetEncounter(EncounterId);
            if (encounter == null)
            {
                Debug.LogError($"[BattleBootstrap] 找不到战斗配置「{EncounterId}」。");
                return;
            }

            // BattleController 阶段 4 起是纯 C# 类，不再挂在 GameObject 上
            Controller = new BattleController();

            var screenGo = new GameObject("BattleScreen");
            screenGo.transform.SetParent(transform, false);
            var screen = screenGo.AddComponent<BattleScreen>();

            Controller.StartBattle(Run, encounter);
            screen.Bind(Controller);

            Debug.Log($"[BattleBootstrap] 战斗开始。种子 = {seed}，牌库 {Run.Deck.Count} 张。");
        }
    }
}
