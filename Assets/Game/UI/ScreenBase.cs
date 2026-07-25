using Game.Core;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 所有局外界面的基类。
    ///
    /// 约定：<see cref="GameApp"/> 在阶段切换时销毁旧界面、创建新界面并调用 <see cref="Build"/>。
    /// 子类只需要在 Build 里把自己的 UI 搭进 <see cref="Root"/>，不需要管生命周期。
    /// ★ 界面可以读 RunContext，但改动流程一律通过 <see cref="Manager"/> 的公开方法，
    ///   与战斗层「UI 只能走 BattleController 三个入口」是同一条纪律。
    /// </summary>
    public abstract class ScreenBase : MonoBehaviour
    {
        protected GameApp App { get; private set; }
        protected RunManager Manager => App.Manager;
        protected RunContext Run => App.Manager.Run;
        protected GameDatabase Db => App.Database;

        /// <summary>本界面的根节点，已铺满屏幕。</summary>
        protected RectTransform Root { get; private set; }

        /// <summary>
        /// 由 <see cref="GameApp"/> 在创建界面后立刻调用。
        ///
        /// ★ <see cref="Root"/> 就是本组件自己的 RectTransform，不是另建的子节点。
        ///   曾经的写法是「组件挂在 A 上、UI 建在 B 下」，于是 GameApp 销毁 A 时
        ///   整棵 UI 树 B 留在场景里——每切一次界面就叠一层，主菜单、结算和战斗界面会同时显示。
        ///   把两者合并成同一个 GameObject 之后，销毁组件必然连带销毁它的 UI。
        /// </summary>
        public void Initialize(GameApp app)
        {
            App = app;
            Root = (RectTransform)transform;
            Build();
        }

        /// <summary>搭建界面。只会被调用一次。</summary>
        protected abstract void Build();

        /// <summary>是否显示顶部状态栏（生命 / 金币 / 遗物）。主菜单不显示。</summary>
        public virtual bool ShowTopBar => true;
    }
}
