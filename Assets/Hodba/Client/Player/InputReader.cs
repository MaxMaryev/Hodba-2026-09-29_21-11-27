using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Hodba.Client
{
    /// <summary>
    /// Жесты → простые сигналы. Никакого интерфейса на экране.
    /// Телефон: касание в нижней трети — идти/стоять; вести пальцем — взгляд; два пальца — всмотреться;
    /// три пальца (только для разработки) — листать время суток.
    /// Редактор: ЛКМ + мышь — взгляд; Space/W — идти/стоять; ПКМ — всмотреться; T — время суток.
    /// </summary>
    public sealed class InputReader
    {
        public Vector2 LookDegrees { get; private set; }
        public bool ToggleWalk { get; private set; }
        public bool Focus { get; private set; }
        public bool CycleTime { get; private set; }
        public bool Back { get; private set; }

        const float TapMaxTime = 0.3f;
        const float TapSlopFraction = 0.02f;
        const float FocusHoldTime = 0.15f;

        int _dragFinger = -1;
        bool _dragging;
        Vector2 _dragStart;
        double _dragStartTime;
        double _twoFingerSince = -1;
        bool _threeFingerLatch;

        public InputReader()
        {
            EnhancedTouchSupport.Enable();
        }

        public void Dispose()
        {
            EnhancedTouchSupport.Disable();
        }

        public void Tick(FieldConfig config)
        {
            LookDegrees = Vector2.zero;
            ToggleWalk = false;
            CycleTime = false;
            Back = false;
            bool focus = false;

            float degPerPixel = config.lookSensitivity / Mathf.Max(1, Screen.width);
            double now = Time.realtimeSinceStartupAsDouble;

            var touches = ETouch.activeTouches;
            int count = touches.Count;

            if (count == 1)
            {
                var t = touches[0];
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Began || _dragFinger != t.finger.index)
                {
                    _dragFinger = t.finger.index;
                    _dragging = false;
                    _dragStart = t.screenPosition;
                    _dragStartTime = now;
                }

                if (!_dragging && (t.screenPosition - _dragStart).magnitude > Screen.width * TapSlopFraction)
                    _dragging = true;

                if (_dragging && t.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                    LookDegrees += new Vector2(t.delta.x, t.delta.y) * degPerPixel;

                if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended && !_dragging &&
                    now - _dragStartTime < TapMaxTime && _dragStart.y < Screen.height / 3f)
                    ToggleWalk = true;
            }
            else
            {
                _dragFinger = -1;
            }

            if (count == 2)
            {
                if (_twoFingerSince < 0) _twoFingerSince = now;
                focus |= now - _twoFingerSince > FocusHoldTime;
            }
            else _twoFingerSince = -1;

            if (count >= 3)
            {
                if (!_threeFingerLatch && Debug.isDebugBuild) CycleTime = true;
                _threeFingerLatch = true;
            }
            else _threeFingerLatch = false;

            var mouse = Mouse.current;
            if (mouse != null && count == 0)
            {
                if (mouse.leftButton.isPressed) LookDegrees += mouse.delta.ReadValue() * degPerPixel;
                focus |= mouse.rightButton.isPressed;
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) ToggleWalk = true;
                if (kb.tKey.wasPressedThisFrame) CycleTime = true;
                // На Android системная «назад» приходит как Escape.
                if (kb.escapeKey.wasPressedThisFrame) Back = true;
            }

            Focus = focus;
        }
    }
}
