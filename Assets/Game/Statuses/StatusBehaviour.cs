using System;
using Game.Battle;

namespace Game.Statuses
{
    /// <summary>
    /// 状态的行为。按需实现 Game.Battle 里的 8 个 Hook 接口之一或多个。
    /// ★ 铁律：不得有任何在运行时被写入的实例字段——同一个 StatusDefinition 的行为实例
    ///   被所有持有该状态的单位共享。可变数据放 StatusInstance / BattleContext。
    /// </summary>
    [Serializable]
    public abstract class StatusBehaviour
    {
        public virtual int Order => HookOrder.AddFlat;
    }
}
