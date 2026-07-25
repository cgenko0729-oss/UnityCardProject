namespace Game.Battle
{
    /// <summary>
    /// 表现层事件。纯值类型。逻辑层只 Post，永不读取；UI 层（BattlePresenter）负责消费。
    /// 因为逻辑层不读它，随便加字段都不会影响结算。
    /// </summary>
    public readonly struct BattleEvent
    {
        public readonly BattleEventType Type;
        /// <summary>来源的 BattleUnit.Uid。</summary>
        public readonly int SourceUid;
        /// <summary>目标的 BattleUnit.Uid 或 CardInstance.Uid（卡牌类事件）。</summary>
        public readonly int TargetUid;
        public readonly int Value;
        /// <summary>状态 id / 卡 id / 文本 key。</summary>
        public readonly string Id;

        public BattleEvent(BattleEventType type, int sourceUid = 0, int targetUid = 0, int value = 0, string id = null)
        {
            Type = type;
            SourceUid = sourceUid;
            TargetUid = targetUid;
            Value = value;
            Id = id;
        }

        public override string ToString() => $"{Type}(src={SourceUid}, tgt={TargetUid}, v={Value}, id={Id})";
    }
}
