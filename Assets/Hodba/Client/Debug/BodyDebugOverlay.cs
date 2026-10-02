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
    /// F1 или четыре пальца — графики; F2 — запись в CSV; F3 — шкала усталости (видна сразу).
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
        EyeRender _eye;
        readonly BodyRecorder _recorder = new BodyRecorder();
        Channel[] _channels;
        bool[] _steps;
        int _head;
        bool _visible, _fourLatch, _fatigueBar = true;
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

        /// <summary>Чтобы видеть, работает ли проход глаза (веки, периферия) на этом устройстве.</summary>
        public void SetEye(EyeRender eye) => _eye = eye;

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
                Make("усталость", new Color(1f, 0.55f, 0.2f), 0f, 1f),
                Make("усилие", new Color(0.85f, 0.7f, 0.5f), 0f, 1.5f),
                Make("спешка", new Color(0.3f, 0.95f, 1f), 0f, 1f),
                Make("ритм", new Color(0.6f, 0.75f, 1f), 0f, 1f),
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
            Put(12, _sim.Fatigue);
            Put(13, _sim.Effort);
            Put(14, _sim.Haste);
            Put(15, _body.Rhythm);
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
                if (kb.f3Key.wasPressedThisFrame) _fatigueBar = !_fatigueBar;
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
            if (_fatigueBar) DrawFatigue();
            if (!_visible && !_recorder.Recording) return;
            if (_recorder.Recording) GUI.Label(new Rect(10, 10, 600, 22), $"● запись тела: {_recorder.Path}");
            if (!_visible) return;

            float w = Mathf.Min(Screen.width * 0.45f, 700f);
            float x0 = 10f, y0 = 36f;
            // Все каналы должны влезть и на телефон.
            float h = Mathf.Clamp((Screen.height - y0 - 50f) / _channels.Length - 4f, 18f, 46f);

            GUI.Label(new Rect(x0, y0 - 4f, w, 22f),
                $"скорость {_sim.Speed:0.00} м/с  уклон {_sim.Slope * 100f:0}%  шаг {_body.Gait.StepFrequency:0.00}/с  " +
                $"фаза {_body.Gait.Phase:0.00}  {_surface}  взгляд: {_body.Eyes.Kind}, камни знакомы на {_body.Habituation.Familiarity(GazeKind.Stone):0.00}");
            var per = _body.Periphery;
            GUI.Label(new Rect(x0, y0 + 16f, w, 22f),
                $"глаз: {(_eye != null && _eye.Working ? "проход работает" : "проход НЕ работает")}; " +
                $"периферия: мыло с {per.Start:0.00}, в углах {per.Blur:0.00}, цвет −{per.Desaturate:0.00}, темнее {per.Darken:0.00}");
            y0 += 40f;

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

        /// <summary>
        /// Шкала усталости в правом верхнем углу: заливка — усталость, отметки — откат (оранжевая) и где спешка
        /// слабеет и кончается (белые); под ней — спешка, которую тело выдаёт, и ритм, который просит игрок.
        /// </summary>
        void DrawFatigue()
        {
            var pace = _sim.Params.Pace;
            const float w = 280f, h = 14f;
            float x = Screen.width - w - 10f, y = 10f;
            float f = _sim.Fatigue;

            Fill(new Rect(x - 2f, y - 2f, w + 4f, h + 52f), new Color(0f, 0f, 0f, 0.45f));
            Fill(new Rect(x, y, w * f, h), Color.Lerp(new Color(0.4f, 0.85f, 0.4f), new Color(1f, 0.3f, 0.2f), f));
            Fill(new Rect(x + w * pace.DebtFrom - 1f, y, 2f, h), new Color(1f, 0.6f, 0.1f));
            Fill(new Rect(x + w * pace.HurryFadeFrom - 1f, y, 2f, h), Color.white);
            Fill(new Rect(x + w * pace.HurryFadeTo - 1f, y, 2f, h), Color.white);
            Fill(new Rect(x, y + h + 2f, w * _sim.Haste, 4f), new Color(0.3f, 0.95f, 1f));
            Fill(new Rect(x, y + h + 7f, w * _body.Rhythm, 3f), new Color(0.6f, 0.75f, 1f));

            GUI.Label(new Rect(x, y + h + 10f, w, 22f),
                $"усталость {f:0.00}  усилие {_sim.Effort:0.00}  спешка {_sim.Haste:0.00}  ритм {_body.Rhythm:0.00}");
            GUI.Label(new Rect(x, y + h + 26f, w, 22f),
                $"темп ×{_sim.PaceFactor:0.00}  {_sim.Speed:0.00} м/с (обычно {_sim.Params.BaseSpeed:0.00})");
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
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
