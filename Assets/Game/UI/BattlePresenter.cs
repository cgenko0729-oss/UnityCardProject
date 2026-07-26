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
        [Tooltip("所有事件时长的统一倍率。调试用，正式取值请改 DurationOf 里的常量。")]
        public float DurationScale = 1f;

        private BattleScreen _screen;
        private BattleContext _ctx;

        /// <summary>当前这条事件还欠多少「表现时间」没走完。</summary>
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

        // ============================================================ 播放节奏

        // ★ 这一段的取值全部集中在这里，改手感只改这几个常量（同 HandFanLayout 的写法）。

        /// <summary>一次伤害占用的时间。闪白 + 飘字要看得清，这是全表里最长的常规项。</summary>
        private const float DurDamage = 0.18f;

        /// <summary>护甲挡下 / 获得护甲。比伤害短——它不该抢走攻击的节奏。</summary>
        private const float DurBlock = 0.10f;

        private const float DurHeal = 0.14f;
        private const float DurStatus = 0.10f;
        private const float DurCardPlay = 0.10f;

        /// <summary>死亡要单独留一拍，否则倒下的动作会被下一条事件盖过去。</summary>
        private const float DurDeath = 0.60f;

        private const float DurTurnBanner = 0.25f;
        private const float DurEnemyTurn = 0.30f;
        private const float DurBattleEnd = 0.40f;

        /// <summary>队列超过这个条数才开始压缩。</summary>
        private const int BacklogSoftLimit = 12;

        /// <summary>每超一条，播放速率加多少。</summary>
        private const float BacklogRatePerEvent = 0.15f;

        /// <summary>
        /// 压缩速率的上限。
        /// ★ 就算积压一百条也只是加速到 3 倍，**绝不跳过任何一条**：
        ///   原来的写法是「队列超过 20 条就一帧全部播完」，
        ///   于是五段攻击、全场中毒结算这些最该有打击感的时刻，反而是唯一什么都看不见的时刻。
        ///   压缩之后一次伤害仍有 0.06 秒 ≈ 4 帧，闪白至少能看见一下。
        /// </summary>
        private const float MaxBacklogRate = 3f;

        /// <summary>一帧最多播几条，防止 0 时长事件连成死循环。</summary>
        private const int MaxEventsPerFrame = 64;

        /// <summary>
        /// 每种事件占用多少「表现时间」。0 表示不占时间——纯日志类当场播完，
        /// 不该为了「洗牌」两个字让玩家等 0.12 秒。
        /// </summary>
        private static float DurationOf(BattleEventType type)
        {
            switch (type)
            {
                case BattleEventType.DamageDealt: return DurDamage;
                case BattleEventType.DamageBlocked: return DurBlock;
                case BattleEventType.BlockGained: return DurBlock;
                case BattleEventType.Healed: return DurHeal;
                case BattleEventType.StatusApplied: return DurStatus;
                case BattleEventType.StatusTriggered: return DurStatus;
                case BattleEventType.CardPlayed: return DurCardPlay;
                case BattleEventType.UnitDied: return DurDeath;
                case BattleEventType.TurnStarted: return DurTurnBanner;
                case BattleEventType.EnemyTurnStarted: return DurEnemyTurn;
                case BattleEventType.BattleEnded: return DurBattleEnd;
                default: return 0f;
            }
        }

        /// <summary>
        /// 当前的播放速率 = 玩家设的倍速 × 积压压缩。
        /// ★ 用 <c>Time.deltaTime</c> 而不是 unscaled：致命一击的慢放走 <c>Time.timeScale</c>，
        ///   事件推进必须跟着一起慢下来，否则「世界慢放了但飘字照常一条条蹦」会很怪。
        /// </summary>
        private float PlaybackRate
        {
            get
            {
                float rate = FeedbackSettings.SpeedMultiplier;

                int backlog = _ctx.Events.Count;
                if (backlog > BacklogSoftLimit)
                {
                    rate *= Mathf.Min(MaxBacklogRate,
                                      1f + (backlog - BacklogSoftLimit) * BacklogRatePerEvent);
                }
                return rate;
            }
        }

        private void Update()
        {
            if (_ctx == null) return;

            _timer -= Time.deltaTime * PlaybackRate;

            int guard = 0;
            while (_ctx.Events.Count > 0 && _timer <= 0f)
            {
                var e = _ctx.Events.Dequeue();
                Play(e);

                // ★ 累加而不是覆盖：0 时长的事件加完仍然 <= 0，循环会继续，
                //   于是一串纯日志事件在同一帧全部播完，正是想要的效果。
                _timer += DurationOf(e.Type) * Mathf.Max(0.01f, DurationScale);

                if (++guard > MaxEventsPerFrame) break;
            }

            // 欠的时间不累积到下一帧，免得掉一次帧之后节奏永久追不回来
            if (_timer < 0f) _timer = 0f;
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
                        // ★ 血条在这一刻才掉，不是在逻辑结算的那一帧。见 UnitView._shownHp 的注释。
                        view.OnDamaged(e.Value);
                        PlayHitFeedback(e, view);
                    }
                    AddLog(Loc.T("log.damaged", "{0} 受到 {1} 点伤害", NameOf(e.TargetUid), e.Value));
                    break;
                }

                case BattleEventType.DamageBlocked:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null)
                    {
                        view.OnBlockConsumed(e.Value);
                        FloatingText.Spawn(_screen.PopupLayer, _screen.AnchoredPosOf(view), Loc.T("float.blocked", "挡下 {0}", e.Value),
                            new Color(0.6f, 0.85f, 1f), 26);
                    }
                    break;
                }

                case BattleEventType.BlockGained:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null)
                    {
                        view.OnBlockGained(e.Value);
                        FloatingText.Spawn(_screen.PopupLayer, _screen.AnchoredPosOf(view), Loc.T("float.block_gain", "+{0} 护甲", e.Value),
                            new Color(0.6f, 0.85f, 1f), 26);
                    }
                    AddLog(Loc.T("log.block_gain", "{0} 获得 {1} 点护甲", NameOf(e.TargetUid), e.Value));
                    break;
                }

                // 回合开始时护甲清零。★ 原本这条事件根本没人接，护甲数字全靠每帧直读逻辑值，
                //   现在改成表现值之后，不接它护甲就会一直挂在屏幕上不消失。
                case BattleEventType.BlockLost:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null) view.OnBlockCleared();
                    break;
                }

                case BattleEventType.Healed:
                {
                    var view = _screen.FindUnitView(e.TargetUid);
                    if (view != null)
                    {
                        view.OnHealed(e.Value);
                        FloatingText.Spawn(_screen.PopupLayer, _screen.AnchoredPosOf(view), $"+{e.Value}",
                            new Color(0.5f, 1f, 0.5f));
                    }
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
                    // 倒下也震一下——它和挨打是两件事，中间隔着完整的一拍
                    _screen.Shake(DeathShake, DeathShakeTime);
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

        // ============================================================ 打击反馈

        // ★ 手感参数集中在这里。要调震幅 / 慢放就改这几个数（同 HandFanLayout 的写法）。

        /// <summary>震屏振幅（像素）：轻伤 → 重伤。</summary>
        private const float ShakeMin = 5f;
        private const float ShakeMax = 30f;
        private const float ShakeTime = 0.26f;

        /// <summary>玩家挨打时的震幅倍率。★ 挨打比打人更该有分量，不然玩家不会有紧张感。</summary>
        private const float PlayerHitShakeBoost = 1.4f;

        /// <summary>低于这个伤害占比就不震——每一下都震等于一直在震，等于没震。</summary>
        private const float ShakeThreshold = 0.025f;

        private const float DeathShake = 20f;
        private const float DeathShakeTime = 0.4f;

        /// <summary>重击的顿帧时长。超过这个伤害占比才给。</summary>
        private const float HeavyHitThreshold = 0.14f;
        private const float HeavyHitStop = 0.05f;

        /// <summary>致命一击：先冻住，再慢放。两段都是真实秒。</summary>
        private const float LethalHitStop = 0.09f;
        private const float LethalSlowScale = 0.25f;
        private const float LethalSlowTime = 0.45f;

        /// <summary>飘字字号的分级门槛（按伤害占最大生命的比例）。</summary>
        private const float BigDamageRatio = 0.09f;
        private const float HugeDamageRatio = 0.20f;

        /// <summary>
        /// 一次伤害的全部表现：闪白 + 击退 + 挤压 + 飘字 + 震屏 +（致命时）顿帧慢放。
        ///
        /// ★ 一切幅度都由「这一下占目标最大生命的比例」推出来，而不是伤害的绝对值。
        ///   30 点打在 200 血的 Boss 身上和打在 40 血的小怪身上，是两件完全不同的事；
        ///   按绝对值缩放的话，前期每一刀都像天崩地裂，后期打 Boss 反而毫无反应。
        /// </summary>
        private void PlayHitFeedback(in BattleEvent e, UnitView view)
        {
            var target = view.Unit;
            int maxHp = target != null && target.MaxHp > 0 ? target.MaxHp : 1;
            float severity = Mathf.Clamp01((float)e.Value / maxHp);
            bool lethal = e.Has(BattleEventFlags.Lethal);

            // ---- 受击反应
            view.PlayHit(severity, KnockbackDir(e.SourceUid, e.TargetUid), lethal);

            // ---- 飘字
            FloatingText.Spawn(
                _screen.PopupLayer,
                _screen.AnchoredPosOf(view) + view.NextFloatOffset(),
                $"-{e.Value}",
                DamageColor(e.Kind, lethal),
                DamageFontSize(severity, lethal),
                lethal ? FloatKind.Lethal : FloatKind.Damage);

            // ---- 震屏
            if (severity >= ShakeThreshold)
            {
                float amp = Mathf.Lerp(ShakeMin, ShakeMax, severity);
                if (target != null && target.IsPlayer) amp *= PlayerHitShakeBoost;
                _screen.Shake(amp, ShakeTime);
            }

            // ---- 顿帧 / 慢放
            if (lethal)
            {
                var tf = TimeFeedback.Instance;
                tf.HitStop(LethalHitStop);
                tf.SlowMotion(LethalSlowScale, LethalSlowTime);
            }
            else if (severity >= HeavyHitThreshold)
            {
                TimeFeedback.Instance.HitStop(HeavyHitStop);
            }
        }

        /// <summary>
        /// 击退方向：从攻击者指向被打者，取近似水平。
        ///
        /// ★ 竖直分量被压得很扁：单位面板上的血条和数字是要读的，
        ///   上下猛跳会让它们在最需要看清的那一刻糊掉。
        ///
        /// ★ 没有攻击者（<c>SourceUid == 0</c>，中毒 / 灼烧）或自伤时返回零向量，
        ///   由 <see cref="UnitView.PlayHit"/> 退化成「只挤压不击退」——
        ///   中毒把人推开一段在观感上完全说不通。
        /// </summary>
        private Vector2 KnockbackDir(int sourceUid, int targetUid)
        {
            if (sourceUid == 0 || sourceUid == targetUid) return Vector2.zero;

            var from = _screen.FindUnitView(sourceUid);
            var to = _screen.FindUnitView(targetUid);
            if (from == null || to == null) return Vector2.zero;

            Vector2 d = _screen.AnchoredPosOf(to) - _screen.AnchoredPosOf(from);
            if (Mathf.Abs(d.x) < 1f) return Vector2.zero;

            return new Vector2(Mathf.Sign(d.x) * 0.95f, Mathf.Clamp(d.y * 0.002f, -0.3f, 0.3f));
        }

        /// <summary>
        /// 飘字配色。★ 这是第 0 层给 <see cref="BattleEvent.Kind"/> 补字段最直接的回报：
        ///   「被砍了一刀」「中毒掉血」「踩到荆棘」在事件里的 Value 是一样的整数，
        ///   没有 Kind 就只能一律画成红色，玩家分不出自己到底在被什么打。
        /// </summary>
        private static Color DamageColor(DamageKind kind, bool lethal)
        {
            if (lethal) return new Color(1f, 0.95f, 0.72f);

            switch (kind)
            {
                case DamageKind.Status: return new Color(0.62f, 1f, 0.48f);   // 中毒 / 灼烧
                case DamageKind.Thorns: return new Color(1f, 0.76f, 0.34f);   // 荆棘反弹
                case DamageKind.Loss: return new Color(0.86f, 0.62f, 0.96f);  // 直接流失
                default: return new Color(1f, 0.45f, 0.4f);                   // 普通攻击
            }
        }

        private static int DamageFontSize(float severity, bool lethal)
        {
            if (lethal) return 68;
            if (severity >= HugeDamageRatio) return 52;
            if (severity >= BigDamageRatio) return 42;
            return 34;
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
