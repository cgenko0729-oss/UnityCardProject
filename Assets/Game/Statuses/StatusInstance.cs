namespace Game.Statuses
{
    /// <summary>状态的运行时实例。层数、剩余时间、来源都在这里。</summary>
    public class StatusInstance
    {
        public readonly StatusDefinition Def;
        public int Stacks;

        /// <summary>施加者的 BattleUnit.Uid。借鉴 Chrono Ark 的 Buff.StackInfo[i].UseState。</summary>
        public readonly int SourceUid;

        public StatusInstance(StatusDefinition def, int stacks, int sourceUid)
        {
            Def = def;
            Stacks = stacks;
            SourceUid = sourceUid;
        }

        public string Id => Def != null ? Def.Id : null;

        public void AddStacks(int n)
        {
            Stacks += n;
            if (Def != null && Stacks > Def.MaxStacks) Stacks = Def.MaxStacks;
        }

        public override string ToString() => $"{Id}x{Stacks}";
    }
}
