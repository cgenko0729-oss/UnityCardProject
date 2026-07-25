using System;
using System.Collections.Generic;
using Game.RunEffects;
using UnityEngine;

namespace Game.Events
{
    /// <summary>事件的一个选项。</summary>
    [Serializable]
    public class EventOption
    {
        [Tooltip("按钮上的文字")]
        public string Text = "……";

        [Tooltip("不满足条件时按钮变灰。默认 Always。")]
        public RunCondition Condition;

        [Tooltip("变灰时显示的原因，例如「金币不足」")]
        public string DisabledHint;

        [Tooltip("选择后显示的旁白。留空则只显示效果自动生成的文本。")]
        [TextArea(2, 4)]
        public string ResultText;

        [Tooltip("选择后执行的效果")]
        [SerializeReference]
        public List<RunEffect> Effects = new List<RunEffect>();

        [Tooltip("勾选后，选择此项会直接离开事件；否则事件停留、可继续选（用于「再看看」）")]
        public bool EndsEvent = true;
    }

    /// <summary>
    /// 一个随机事件的静态配置。★ 不存任何运行时状态。
    /// 文案 + 若干选项 + 每个选项的 RunEffect 列表，加一个新事件不需要写任何代码。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Event", fileName = "Event_")]
    public class EventDefinition : ScriptableObject
    {
        public string Id;
        public string Title;
        public Sprite Art;

        [TextArea(4, 10)]
        public string Description;

        public List<EventOption> Options = new List<EventOption>();

        [Tooltip("出现在地图上的最低行数。用来让危险事件晚点出现。")]
        public int MinRow;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(Id))
                Id = name.StartsWith("Event_") ? name.Substring(6).ToLowerInvariant() : name.ToLowerInvariant();
        }
#endif
    }
}
