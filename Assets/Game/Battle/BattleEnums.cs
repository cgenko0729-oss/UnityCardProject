namespace Game.Battle
{
    public enum BattlePhase
    {
        None, BattleStart, TurnStart, PlayerTurn, TurnEnd, EnemyTurn, Victory, Defeat
    }

    /// <summary>表现层事件类型。只影响播放，不影响结算。</summary>
    public enum BattleEventType
    {
        BattleStarted, BattleEnded,
        TurnStarted, TurnEnded, EnemyTurnStarted,
        CardDrawn, CardPlayed, CardDiscarded, CardExhausted, CardAdded, DeckShuffled,
        /// <summary>一张牌临时获得「保留」。</summary>
        CardRetained,
        /// <summary>结算挂起，正在等玩家选牌。Value 为要选几张。</summary>
        CardSelectionRequested,
        DamageDealt, DamageBlocked, Healed, BlockGained, BlockLost,
        StatusApplied, StatusRemoved, StatusTriggered,
        EnergyChanged, IntentChanged,
        PotionUsed, PotionDiscarded,
        UnitDied, Message
    }

    /// <summary>Hook 执行顺序。数值小的先跑。</summary>
    public static class HookOrder
    {
        /// <summary>先于一切的修正（例如「伤害改为固定值」）。</summary>
        public const int Early = -100;
        /// <summary>加法修正（力量）。</summary>
        public const int AddFlat = 0;
        /// <summary>乘法修正（易伤 / 虚弱）。</summary>
        public const int Multiply = 100;
        /// <summary>最后的钳制。</summary>
        public const int Late = 200;
    }

    public enum PlayFailReason
    {
        None, NotPlayerTurn, NotInHand, NotEnoughEnergy, Unplayable,
        NeedTarget, InvalidTarget, EffectCannotApply, BattleEnded,
        /// <summary>被 <see cref="ICardFlowHook.PreCardPlay"/> 拦下（能量未消耗）。</summary>
        Cancelled,
        /// <summary>上一张牌的结算正挂起等玩家选牌。</summary>
        WaitingForSelection
    }

    public enum PotionFailReason
    {
        None, NotPlayerTurn, BattleEnded, WaitingForSelection,
        /// <summary>这瓶药水不在玩家背包里。</summary>
        NotHeld,
        NeedTarget, InvalidTarget
    }

    public enum DamageKind { Attack, Status, Thorns, Loss }

    /// <summary>延迟效果的触发时机。</summary>
    public enum DelayTiming { EndOfThisTurn, StartOfNextTurn }
}
