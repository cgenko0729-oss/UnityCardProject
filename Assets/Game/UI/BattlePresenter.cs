using System.Collections.Generic;
using Game.Battle;
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
                    AddLog($"{NameOf(e.TargetUid)} 受到 {e.Value} 点伤害");
                    break;
                }

                case BattleEventType.DamageBlocked:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null)
                        FloatingText.Spawn(_screen.PopupLayer, _screen.AnchoredPosOf(view), $"挡下 {e.Value}",
                            new Color(0.6f, 0.85f, 1f), 26);
                    break;
                }

                case BattleEventType.BlockGained:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null)
                        FloatingText.Spawn(_screen.PopupLayer, _screen.AnchoredPosOf(view), $"+{e.Value} 护甲",
                            new Color(0.6f, 0.85f, 1f), 26);
                    AddLog($"{NameOf(e.TargetUid)} 获得 {e.Value} 点护甲");
                    break;
                }

                case BattleEventType.Healed:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null)
                        FloatingText.Spawn(_screen.PopupLayer, _screen.AnchoredPosOf(view), $"+{e.Value}",
                            new Color(0.5f, 1f, 0.5f));
                    AddLog($"{NameOf(e.TargetUid)} 回复 {e.Value} 点生命");
                    break;
                }

                case BattleEventType.StatusApplied:
                    AddLog($"{NameOf(e.TargetUid)} 获得 [{e.Id}] x{e.Value}");
                    break;

                case BattleEventType.StatusTriggered:
                    AddLog($"{NameOf(e.TargetUid)} 的 [{e.Id}] 触发（{e.Value}）");
                    break;

                case BattleEventType.CardPlayed:
                    AddLog($"打出「{e.Id}」（消耗 {e.Value} 能量）");
                    break;

                case BattleEventType.CardExhausted:
                    AddLog($"「{e.Id}」被消耗");
                    break;

                case BattleEventType.DeckShuffled:
                    AddLog("洗牌");
                    break;

                case BattleEventType.TurnStarted:
                    AddLog($"—— 第 {e.Value} 回合 ——");
                    break;

                case BattleEventType.EnemyTurnStarted:
                    AddLog("敌人行动");
                    break;

                case BattleEventType.UnitDied:
                    AddLog($"{NameOf(e.TargetUid)} 倒下了");
                    break;

                case BattleEventType.BattleEnded:
                    AddLog(e.Value == 1 ? "★ 战斗胜利 ★" : "☠ 战斗失败 ☠");
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
