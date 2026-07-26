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

        /// <summary>
        /// 伤害类事件的伤害类型（攻击 / 中毒 / 荆棘 / 流失）。
        /// ★ 非伤害事件上这个字段没有意义，恒为默认的 <see cref="DamageKind.Attack"/>，不要去读。
        ///   有它，表现层才分得清「被砍了一刀」和「中毒掉血」——两者该给完全不同的
        ///   闪光颜色、飘字样式和音色。
        /// </summary>
        public readonly DamageKind Kind;

        /// <summary>附加标记（致命 / 过量击杀）。见 <see cref="BattleEventFlags"/> 的注释。</summary>
        public readonly BattleEventFlags Flags;

        /// <summary>
        /// ★ <paramref name="kind"/> / <paramref name="flags"/> 是**追加**的可选参数，
        ///   所以全工程既有的 40 多处 Post 调用一行都不用改。
        /// </summary>
        public BattleEvent(BattleEventType type, int sourceUid = 0, int targetUid = 0, int value = 0,
                           string id = null, DamageKind kind = DamageKind.Attack,
                           BattleEventFlags flags = BattleEventFlags.None)
        {
            Type = type;
            SourceUid = sourceUid;
            TargetUid = targetUid;
            Value = value;
            Id = id;
            Kind = kind;
            Flags = flags;
        }

        public bool Has(BattleEventFlags f) => (Flags & f) != 0;

        public override string ToString()
            => $"{Type}(src={SourceUid}, tgt={TargetUid}, v={Value}, id={Id}, kind={Kind}, flags={Flags})";
    }
}
