using System.Collections.Generic;
using Game.Battle;
using Game.Localization;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// ★ 全项目唯一消费 BattleContext.Events 的类。
    /// 把逻辑层产出的事件翻译成动画 / 飘字 / 日志。
    /// 关掉这个组件，战斗逻辑依然完整运行——这是「逻辑可无 UI 运行」的直接体现。
    /// </summary>
    public class BattlePresenter : MonoBehaviour
    {
        [Tooltip("每条事件之间的播放间隔（秒）。0 表示瞬间播完。")]
        public float EventInterval = 0.12f;

        private BattleScreen _screen;
        private BattleContext _ctx;
        private float _timer;

        private readonly List<string> _log = new List<string>(64);
        public IReadOnlyList<string> Log => _log;

        public void Init(BattleScreen screen, BattleContext ctx)
        {
            _screen = screen;
            _ctx = ctx;
            _log.Clear();
            _timer = 0f;
        }

        private void Update()
        {
            if (_ctx == null) return;

            _timer -= Time.deltaTime;
            int guard = 0;

            // 队列积压太多时加速消费，避免动画跟不上逻辑
            while (_ctx.Events.Count > 0 && (_timer <= 0f || _ctx.Events.Count > 20))
            {
                Play(_ctx.Events.Dequeue());
                _timer = EventInterval;
                if (++guard > 64) break;
            }
        }

        private void Play(in BattleEvent e)
        {
            switch (e.Type)
            {
                case BattleEventType.DamageDealt:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null)
                    {
                        view.Flash();
                        FloatingText.Spawn(_screen.PopupLayer, _screen.AnchoredPosOf(view), $"-{e.Value}",
                            new Color(1f, 0.45f, 0.4f));
                    }
                    AddLog(Loc.T("log.damaged", "{0} 受到 {1} 点伤害", NameOf(e.TargetUid), e.Value));
                    break;
                }

                case BattleEventType.DamageBlocked:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null)
                        FloatingText.Spawn(_screen.PopupLayer, _screen.AnchoredPosOf(view), Loc.T("float.blocked", "挡下 {0}", e.Value),
                            new Color(0.6f, 0.85f, 1f), 26);
                    break;
                }

                case BattleEventType.BlockGained:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null)
                        FloatingText.Spawn(_screen.PopupLayer, _screen.AnchoredPosOf(view), Loc.T("float.block_gain", "+{0} 护甲", e.Value),
                            new Color(0.6f, 0.85f, 1f), 26);
                    AddLog(Loc.T("log.block_gain", "{0} 获得 {1} 点护甲", NameOf(e.TargetUid), e.Value));
                    break;
                }

                case BattleEventType.Healed:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null)
                        FloatingText.Spawn(_screen.PopupLayer, _screen.AnchoredPosOf(view), $"+{e.Value}",
                            new Color(0.5f, 1f, 0.5f));
                    AddLog(Loc.T("log.healed", "{0} 回复 {1} 点生命", NameOf(e.TargetUid), e.Value));
                    break;
                }

                case BattleEventType.StatusApplied:
                    AddLog(Loc.T("log.status_applied", "{0} 获得 [{1}] x{2}", NameOf(e.TargetUid), e.Id, e.Value));
                    break;

                case BattleEventType.StatusTriggered:
                    AddLog(Loc.T("log.status_triggered", "{0} 的 [{1}] 触发（{2}）", NameOf(e.TargetUid), e.Id, e.Value));
                    break;

                case BattleEventType.CardPlayed:
                    AddLog(Loc.T("log.card_played", "打出「{0}」（消耗 {1} 能量）", e.Id, e.Value));
                    break;

                case BattleEventType.CardExhausted:
                    AddLog(Loc.T("log.card_exhausted", "「{0}」被消耗", e.Id));
                    break;

                case BattleEventType.CardDiscarded:
                    AddLog(Loc.T("log.card_discarded", "「{0}」被弃掉", e.Id));
                    break;

                case BattleEventType.CardRetained:
                    AddLog(Loc.T("log.card_retained", "「{0}」将保留到下回合", e.Id));
                    break;

                case BattleEventType.CardSelectionRequested:
                    AddLog(Loc.T("log.awaiting_selection", "等待选择 {0} 张牌…", e.Value));
                    break;

                case BattleEventType.PotionUsed:
                    AddLog(Loc.T("log.potion_used", "喝下药水「{0}」", e.Id));
                    break;

                case BattleEventType.PotionDiscarded:
                    AddLog(Loc.T("log.potion_discarded", "倒掉了药水「{0}」", e.Id));
                    break;

                case BattleEventType.DeckShuffled:
                    AddLog(Loc.T("log.shuffle", "洗牌"));
                    break;

                case BattleEventType.TurnStarted:
                    AddLog(Loc.T("log.turn_start", "—— 第 {0} 回合 ——", e.Value));
                    break;

                case BattleEventType.EnemyTurnStarted:
                    AddLog(Loc.T("log.enemy_turn", "敌人行动"));
                    break;

                case BattleEventType.UnitDied:
                    AddLog(Loc.T("log.died", "{0} 倒下了", NameOf(e.TargetUid)));
                    break;

                case BattleEventType.BattleEnded:
                    AddLog(e.Value == 1 ? Loc.T("log.victory", "★ 战斗胜利 ★") : Loc.T("log.defeat", "☠ 战斗失败 ☠"));
                    break;

                case BattleEventType.Message:
                    AddLog($"[{e.Id}]");
                    break;
            }
        }

        private string NameOf(int uid)
        {
            var u = _ctx.FindUnit(uid);
            return u != null ? u.Name : "?";
        }

        private void AddLog(string line)
        {
            _log.Add(line);
            if (_log.Count > 200) _log.RemoveAt(0);
        }
    }
}
