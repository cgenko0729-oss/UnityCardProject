using System.Collections.Generic;
using Game.Enemies;
using UnityEngine;

namespace Game.Core
{
    /// <summary>一场战斗的敌人组合。</summary>
    [CreateAssetMenu(menuName = "Game/Encounter", fileName = "Encounter_")]
    public class EncounterDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public List<EnemyDefinition> Enemies = new List<EnemyDefinition>();
        public bool IsElite;
        public bool IsBoss;
    }
}
