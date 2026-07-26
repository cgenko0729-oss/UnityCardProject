using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace Game.UI
{
    /// <summary>
    /// 输入兼容层。本工程 ProjectSettings 里 activeInputHandler = 1（只启用新输入系统），
    /// 此时旧的 UnityEngine.Input 和 StandaloneInputModule 会在运行时抛异常。
    /// 这一层让 UI 代码在「旧 / 新 / 两者都开」三种设置下都能正常工作。
    /// </summary>
    public static class InputCompat
    {
        public static bool RightMouseDown
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null) return Mouse.current.rightButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButtonDown(1);
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 左键按下。★ 与 uGUI 的点击事件是两条路：
        /// EventSystem 只把点击发给射线命中的 Graphic，而「表现还在播的时候点一下催它快点」
        /// 需要的是**整个屏幕任何位置**都算数——包括点在背景、日志、空白处。
        /// 见 <see cref="BattleScreen"/> 的快进分支。
        /// </summary>
        public static bool LeftMouseDown
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null) return Mouse.current.leftButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButtonDown(0);
#else
                return false;
#endif
            }
        }

        public static bool EscapeDown => KeyDown(KeyCode.Escape);
        public static bool SpaceDown => KeyDown(KeyCode.Space);

        public static bool KeyDown(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                switch (key)
                {
                    case KeyCode.Escape: return kb.escapeKey.wasPressedThisFrame;
                    case KeyCode.Space: return kb.spaceKey.wasPressedThisFrame;
                    case KeyCode.E: return kb.eKey.wasPressedThisFrame;
                    default: return false;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key);
#else
            return false;
#endif
        }

        /// <summary>确保场景里有一个能用的 EventSystem（含正确的 InputModule）。</summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();

#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
