using System.Collections.Generic;

namespace Game.Map
{
    public enum MapNodeType
    {
        /// <summary>普通战斗。</summary>
        Battle,
        /// <summary>精英战斗，掉落遗物。</summary>
        Elite,
        /// <summary>休息点：回血或升级一张牌。</summary>
        Rest,
        Shop,
        /// <summary>随机事件。</summary>
        Event,
        /// <summary>宝箱：白给一个遗物 / 金币。</summary>
        Treasure,
        Boss
    }

    /// <summary>
    /// 地图上的一个节点。★ 纯数据，无 Unity 依赖，可以直接序列化进存档。
    /// </summary>
    public class MapNode
    {
        /// <summary>在 <see cref="GameMap.Nodes"/> 里的下标，同时也是节点的唯一 Id。</summary>
        public int Id;

        /// <summary>第几行（0 = 最底下的起始行）。</summary>
        public int Row;

        /// <summary>在本行中的列位置，用于画图。</summary>
        public int Column;

        public MapNodeType Type;

        /// <summary>这个节点对应的战斗 / 事件的资产 id。战斗节点为 EncounterDefinition.Id。</summary>
        public string ContentId;

        /// <summary>可以从本节点走到的下一行节点 Id。</summary>
        public readonly List<int> Next = new List<int>(3);

        /// <summary>能走到本节点的上一行节点 Id（画连线与判断可达性用）。</summary>
        public readonly List<int> Prev = new List<int>(3);

        public override string ToString() => $"#{Id} R{Row}C{Column} {Type}({ContentId})";
    }
}
