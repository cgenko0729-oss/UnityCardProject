namespace Game.Enemies
{
    public enum IntentKind
    {
        Unknown, Attack, AttackDefend, AttackDebuff, Defend, Buff, Debuff, Special, Sleep
    }

    /// <summary>给 UI 看的意图快照。</summary>
    public readonly struct Intent
    {
        public readonly IntentKind Kind;
        /// <summary>显示的伤害 / 护甲数值。0 表示不显示数值。</summary>
        public readonly int Value;
        /// <summary>多段攻击次数。&lt;=1 时不显示。</summary>
        public readonly int Times;
        /// <summary>对应 EnemyDefinition.Actions 的下标。</summary>
        public readonly int ActionIndex;

        public Intent(IntentKind kind, int value, int times, int actionIndex)
        {
            Kind = kind;
            Value = value;
            Times = times;
            ActionIndex = actionIndex;
        }

        public string ToDisplayString()
        {
            if (Value <= 0) return Kind.ToString();
            return Times > 1 ? $"{Kind} {Value}x{Times}" : $"{Kind} {Value}";
        }
    }
}
