using Hodba.Client.Body;
using Hodba.Sim.Walk;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Hodba.Client
{
    /// <summary>
    /// Приборы для настройки тела — только в сборке для разработки. Игрок их не видит никогда (закон 3).
    /// F1 или четыре пальца — графики; F2 — запись в CSV.
    /// </summary>
    public sealed class BodyDebugOverlay : MonoBehaviour
    {
        const int Samples = 300;

        struct Channel
        {
            public string Name;
            public Color Color;
            public float Min, Max;
            public float[] Values;
        }

        WalkerBody _body;
        WalkSim _sim;
        readonly BodyRecorder _recorder = new BodyRecorder();
        Channel[] _channels;
        bool[] _steps;
        int _head;
        bool _visible, _fourLatch;
        Material _line;
        string _surface = "";

        public static BodyDebugOverlay Attach(GameObject host, WalkerBody body, WalkSim sim)
        {
            var o = host.AddComponent<BodyDebugOverlay>();
            o._body = body;
            o._sim = sim;
            body.Events.Step += o.OnStep;
            return o;
        }

        void Awake()
        {
            _channels = new[]
            {
                Make("вверх, мм", new Color(1f, 0.85f, 0.3f), -30f, 30f),
                Make("вбок, мм", new Color(0.4f, 0.8f, 1f), -20f, 20f),
                Make("тангаж, °", new Color(1f, 0.5f, 0.4f), -4f, 4f),
                Make("крен, °", new Color(0.7f, 1f, 0.5f), -3f, 3f),
                Make("грудь", new Color(0.9f, 0.9f, 0.9f), 0f, 1f),
                Make("одышка", new Color(1f, 0.4f, 0.8f), 0f, 1f),
                Make("осторожность", new Color(0.5f, 0.6f, 1f), 0f, 1f),
                Make("событие", new Color(1f, 0.3f, 0.2f), 0f, 1f),
                Make("веки открыты", new Color(0.95f, 0.75f, 0.6f), 0f, 1f),
                Make("прищур", new Color(1f, 1f, 0.5f), 0f, 1f),
                Make("глаз вбок, °", new Color(0.5f, 1f, 0.9f), -12f, 12f),
                Make("глаз вниз, °", new Color(0.3f, 0.9f, 0.7f), -5f, 25f),
            };
            _steps = new bool[Samples];
        }

        static Channel Make(string name, Color color, float min, float max) =>
            new Channel { Name = name, Color = color, Min = min, Max = max, Values = new float[Samples] };

        void OnStep(StepEvent e)
        {
            _recorder.MarkStep(e);
            if (e.Felt) _steps[_head] = true;
            _surface = $"{e.Surface} {e.Looseness:0.00}{(e.OnStone ? " камень" : "")}{(e.Stumble ? " СПОТЫКНУЛСЯ" : "")}";
        }

        /// <summary>Снять кадр. Зовёт Bootstrap после тела.</summary>
        public void Sample(float dt)
        {
            HandleInput();
            var p = _body.Pose;
            var x = _body.Exertion;
            _head = (_head + 1) % Samples;
            _steps[_head] = false;
            Put(0, p.Up * 1000f);
            Put(1, p.Side * 1000f);
            Put(2, p.Pitch);
            Put(3, p.Roll);
            Put(4, x.Lungs);
            Put(5, x.Load);
            Put(6, x.Caution);
            Put(7, _body.Gait.EventStrength);
            Put(8, _body.Eyelids.Openness);
            Put(9, _body.Eyelids.Squint);
            Put(10, _body.Eyes.Yaw);
            Put(11, _body.Eyes.Pitch);
            _recorder.Write(_body.Time, dt, _body, _sim);
        }

        void Put(int c, float v) => _channels[c].Values[_head] = v;

        void HandleInput()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) _visible = !_visible;
                if (kb.f2Key.wasPressedThisFrame) _recorder.Toggle(_body.Time);
            }
            if (EnhancedTouchSupport.enabled)
            {
                bool four = ETouch.activeTouches.Count >= 4;
                if (four && !_fourLatch) _visible = !_visible;
                _fourLatch = four;
            }
        }

        void OnGUI()
        {
            if (!_visible && !_recorder.Recording) return;
            if (_recorder.Recording) GUI.Label(new Rect(10, 10, 600, 22), $"● запись тела: {_recorder.Path}");
            if (!_visible) return;

            float w = Mathf.Min(Screen.width * 0.45f, 700f);
            float h = 46f;
            float x0 = 10f, y0 = 36f;

            GUI.Label(new Rect(x0, y0 - 4f, w, 22f),
                $"скорость {_sim.Speed:0.00} м/с  уклон {_sim.Slope * 100f:0}%  шаг {_body.Gait.StepFrequency:0.00}/с  " +
                $"фаза {_body.Gait.Phase:0.00}  {_surface}  взгляд: {_body.Eyes.Kind}, камни знакомы на {_body.Habituation.Familiarity(GazeKind.Stone):0.00}");
            y0 += 20f;

            if (Event.current.type == EventType.Repaint)
            {
                if (_line == null) _line = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                _line.SetPass(0);
                GL.PushMatrix();
                GL.LoadPixelMatrix();
                for (int c = 0; c < _channels.Length; c++) DrawChannel(_channels[c], x0, y0 + c * (h + 4f), w, h);
                GL.PopMatrix();
            }

            for (int c = 0; c < _channels.Length; c++)
            {
                var ch = _channels[c];
                GUI.Label(new Rect(x0 + w + 6f, y0 + c * (h + 4f) + h * 0.3f, 200f, 22f),
                    $"{ch.Name}: {ch.Values[_head]:0.00}");
            }
        }

        void DrawChannel(in Channel ch, float x0, float y0, float w, float h)
        {
            // GL в пикселях с началом внизу, GUI — вверху.
            float top = Screen.height - y0, bottom = top - h;
            GL.Begin(GL.LINES);
            GL.Color(new Color(1f, 1f, 1f, 0.12f));
            float zero = Mathf.Lerp(bottom, top, Mathf.InverseLerp(ch.Min, ch.Max, 0f));
            GL.Vertex3(x0, zero, 0f);
            GL.Vertex3(x0 + w, zero, 0f);

            GL.Color(new Color(1f, 1f, 1f, 0.25f));
            for (int i = 0; i < Samples; i++)
            {
                int k = (_head + 1 + i) % Samples;
                if (!_steps[k]) continue;
                float sx = x0 + w * i / (Samples - 1f);
                GL.Vertex3(sx, bottom, 0f);
                GL.Vertex3(sx, top, 0f);
            }

            GL.Color(ch.Color);
            for (int i = 1; i < Samples; i++)
            {
                int a = (_head + i) % Samples, b = (_head + 1 + i) % Samples;
                float ya = Mathf.Lerp(bottom, top, Mathf.InverseLerp(ch.Min, ch.Max, ch.Values[a]));
                float yb = Mathf.Lerp(bottom, top, Mathf.InverseLerp(ch.Min, ch.Max, ch.Values[b]));
                GL.Vertex3(x0 + w * (i - 1) / (Samples - 1f), ya, 0f);
                GL.Vertex3(x0 + w * i / (Samples - 1f), yb, 0f);
            }
            GL.End();
        }

        void OnDestroy()
        {
            if (_body != null) _body.Events.Step -= OnStep;
            _recorder.Dispose();
            if (_line != null) Destroy(_line);
        }
    }
}
