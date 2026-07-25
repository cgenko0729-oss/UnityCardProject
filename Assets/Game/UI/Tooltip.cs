using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.UI
{
    /// <summary>Tooltip 面板里的一条词条。</summary>
    public readonly struct TooltipEntry
    {
        /// <summary>词条名，例如「易伤」「消耗」。会被加粗并染成 <see cref="Accent"/>。</summary>
        public readonly string Title;

        /// <summary>解释正文。</summary>
        public readonly string Body;

        public readonly Color Accent;

        public TooltipEntry(string title, string body, Color accent)
        {
            Title = title;
            Body = body;
            Accent = accent;
        }
    }

    /// <summary>
    /// 能提供 tooltip 内容的东西。
    ///
    /// ★ 做成「每帧现算」而不是「悬停时算一次存下来」：
    ///   状态层数、能量、力量都会在面板挂着的时候变，
    ///   算一次存下来的话玩家会盯着一段过期的数字。
    /// </summary>
    public interface ITooltipSource
    {
        /// <summary>往 buffer 里追加词条。返回 false 表示这次没有内容，别弹面板。</summary>
        bool BuildTooltip(List<TooltipEntry> buffer);
    }

    /// <summary>内容固定不变的词条源（遗物、关键字这类）。</summary>
    public sealed class StaticTooltipSource : ITooltipSource
    {
        private readonly TooltipEntry _entry;

        public StaticTooltipSource(string title, string body, Color accent)
            => _entry = new TooltipEntry(title, body, accent);

        public bool BuildTooltip(List<TooltipEntry> buffer)
        {
            buffer.Add(_entry);
            return true;
        }
    }

    /// <summary>
    /// 挂在任何 UI 节点上，让它可以悬停出 tooltip。
    ///
    /// ★ 与宿主组件（CardView / Button 之类）共存没有问题：
    ///   EventSystem 会把 Enter/Exit 发给该 GameObject 上**所有**实现了对应接口的组件。
    /// </summary>
    public class TooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private ITooltipSource _source;

        public static TooltipTarget Attach(GameObject go, ITooltipSource source)
        {
            var t = go.GetComponent<TooltipTarget>();
            if (t == null) t = go.AddComponent<TooltipTarget>();
            t._source = source;
            return t;
        }

        public void SetSource(ITooltipSource source) => _source = source;

        public void OnPointerEnter(PointerEventData e)
            => TooltipView.Request(this, _source, (RectTransform)transform);

        public void OnPointerExit(PointerEventData e) => TooltipView.Cancel(this);

        /// <summary>
        /// ★ 必须有：光标还停在上面时这个节点就被销毁了（打出这张牌、状态掉光、切界面），
        ///   那时 <see cref="OnPointerExit"/> 永远不会来，面板会一直挂在屏幕上
        ///   指着一个已经不存在的东西。销毁前 Unity 一定会调 OnDisable，这是唯一可靠的挂钩点。
        /// </summary>
        private void OnDisable() => TooltipView.Cancel(this);
    }
}
