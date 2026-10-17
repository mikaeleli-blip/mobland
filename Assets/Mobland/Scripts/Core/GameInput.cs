using UnityEngine;
#if MOBLAND_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Mobland.Core
{
    /// <summary>
    /// Thin input wrapper. Works with the new Input System package (if installed and enabled)
    /// or the legacy Input Manager, so the project compiles whatever the Player setting is.
    /// </summary>
    public static class GameInput
    {
        enum K { W, A, S, D, Q, E, Up, Down, Left, Right, LeftShift, RightShift, R, Tab, Home, Escape, F1, T, N }

#if MOBLAND_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        static Key Map(K k)
        {
            switch (k)
            {
                case K.W: return Key.W;
                case K.A: return Key.A;
                case K.S: return Key.S;
                case K.D: return Key.D;
                case K.Q: return Key.Q;
                case K.E: return Key.E;
                case K.Up: return Key.UpArrow;
                case K.Down: return Key.DownArrow;
                case K.Left: return Key.LeftArrow;
                case K.Right: return Key.RightArrow;
                case K.LeftShift: return Key.LeftShift;
                case K.RightShift: return Key.RightShift;
                case K.R: return Key.R;
                case K.Tab: return Key.Tab;
                case K.Home: return Key.Home;
                case K.Escape: return Key.Escape;
                case K.F1: return Key.F1;
                case K.T: return Key.T;
                case K.N: return Key.N;
                default: return Key.None;
            }
        }

        static bool Held(K k) { var kb = Keyboard.current; return kb != null && kb[Map(k)].isPressed; }
        static bool Pressed(K k) { var kb = Keyboard.current; return kb != null && kb[Map(k)].wasPressedThisFrame; }

        static ButtonControl MouseButton(Mouse m, int b) =>
            b == 0 ? m.leftButton : b == 1 ? m.rightButton : m.middleButton;

        public static Vector2 MousePosition() { var m = Mouse.current; return m != null ? m.position.ReadValue() : Vector2.zero; }
        public static bool MouseHeld(int b) { var m = Mouse.current; return m != null && MouseButton(m, b).isPressed; }
        public static bool MouseDown(int b) { var m = Mouse.current; return m != null && MouseButton(m, b).wasPressedThisFrame; }
        public static bool MouseUp(int b) { var m = Mouse.current; return m != null && MouseButton(m, b).wasReleasedThisFrame; }

        /// <summary>Scroll in "notches" (+1 = one wheel step away from the user).</summary>
        public static float ScrollSteps()
        {
            var m = Mouse.current;
            if (m == null) return 0f;
            float y = m.scroll.ReadValue().y;
            // Depending on Input System version/platform a notch is either ~1 or ~120.
            if (Mathf.Abs(y) > 20f) y /= 120f;
            return Mathf.Clamp(y, -3f, 3f);
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        static KeyCode Map(K k)
        {
            switch (k)
            {
                case K.W: return KeyCode.W;
                case K.A: return KeyCode.A;
                case K.S: return KeyCode.S;
                case K.D: return KeyCode.D;
                case K.Q: return KeyCode.Q;
                case K.E: return KeyCode.E;
                case K.Up: return KeyCode.UpArrow;
                case K.Down: return KeyCode.DownArrow;
                case K.Left: return KeyCode.LeftArrow;
                case K.Right: return KeyCode.RightArrow;
                case K.LeftShift: return KeyCode.LeftShift;
                case K.RightShift: return KeyCode.RightShift;
                case K.R: return KeyCode.R;
                case K.Tab: return KeyCode.Tab;
                case K.Home: return KeyCode.Home;
                case K.Escape: return KeyCode.Escape;
                case K.F1: return KeyCode.F1;
                case K.T: return KeyCode.T;
                case K.N: return KeyCode.N;
                default: return KeyCode.None;
            }
        }

        static bool Held(K k) => Input.GetKey(Map(k));
        static bool Pressed(K k) => Input.GetKeyDown(Map(k));

        public static Vector2 MousePosition() => Input.mousePosition;
        public static bool MouseHeld(int b) => Input.GetMouseButton(b);
        public static bool MouseDown(int b) => Input.GetMouseButtonDown(b);
        public static bool MouseUp(int b) => Input.GetMouseButtonUp(b);
        public static float ScrollSteps() => Mathf.Clamp(Input.mouseScrollDelta.y, -3f, 3f);
#else
        static bool Held(K k) => false;
        static bool Pressed(K k) => false;
        public static Vector2 MousePosition() => Vector2.zero;
        public static bool MouseHeld(int b) => false;
        public static bool MouseDown(int b) => false;
        public static bool MouseUp(int b) => false;
        public static float ScrollSteps() => 0f;
#endif

        // ---- Game actions -------------------------------------------------

        public static Vector2 PanAxis()
        {
            Vector2 v = Vector2.zero;
            if (Held(K.A) || Held(K.Left)) v.x -= 1f;
            if (Held(K.D) || Held(K.Right)) v.x += 1f;
            if (Held(K.S) || Held(K.Down)) v.y -= 1f;
            if (Held(K.W) || Held(K.Up)) v.y += 1f;
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        /// <summary>-1 = rotate left (Q), +1 = rotate right (E).</summary>
        public static float RotateAxis()
        {
            float r = 0f;
            if (Held(K.Q)) r -= 1f;
            if (Held(K.E)) r += 1f;
            return r;
        }

        public static bool FastModifier() => Held(K.LeftShift) || Held(K.RightShift);
        public static bool RegenerateDown() => Pressed(K.R);
        public static bool OverlayToggleDown() => Pressed(K.Tab);
        public static bool ResetCameraDown() => Pressed(K.Home);
        public static bool CancelDown() => Pressed(K.Escape);
        public static bool HelpToggleDown() => Pressed(K.F1);
        public static bool TimePauseDown() => Pressed(K.T);
        public static bool TimeSkipDown() => Pressed(K.N);
    }
}
