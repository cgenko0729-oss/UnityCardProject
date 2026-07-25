namespace Game.Relics
{
    /// <summary>
    /// 遗物的运行时实例。与 StatusInstance 的定位完全一致：Definition 只读，可变数据在这里。
    ///
    /// <see cref="Counter"/> 是给「需要计数的遗物」用的唯一合法可变存储
    /// （行为类本身被所有存档共享，绝不能有可变字段）。
    /// 例：「每打出 3 张技能牌回 1 点血」把已打出数记在这里。
    /// ★ 计数器的生命周期由行为类自己负责重置——需要每场战斗清零的，
    ///   在 <c>IBattleFlowHook.OnBattleStart</c> 里置 0。
    /// </summary>
    public class RelicInstance
    {
        public readonly RelicDefinition Def;

        public int Counter;

        public RelicInstance(RelicDefinition def)
        {
            Def = def;
        }

        public string Id => Def != null ? Def.Id : null;
        public string DisplayName => Def != null ? Def.DisplayName : "<null>";

        public override string ToString() => Id;
    }
}
