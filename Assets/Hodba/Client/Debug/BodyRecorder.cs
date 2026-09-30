using System;
using System.Globalization;
using System.IO;
using Hodba.Client.Body;
using Hodba.Sim.Walk;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Пишет каналы тела в CSV, чтобы проверить глазами и цифрами: нет ли в спектре узких пиков,
    /// кроме частоты шага, и правда ли соседние шаги различаются. Только для разработки.
    /// </summary>
    public sealed class BodyRecorder : IDisposable
    {
        const double MaxSeconds = 30 * 60;

        StreamWriter _writer;
        double _started;
        int _stepFlag;

        public bool Recording => _writer != null;
        public string Path { get; private set; }

        public void Toggle(double now)
        {
            if (Recording) Stop();
            else Start(now);
        }

        public void Start(double now)
        {
            string dir = Application.isEditor
                ? System.IO.Path.Combine(Application.dataPath, "..", "Logs")
                : Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            Path = System.IO.Path.Combine(dir, $"body-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _writer = new StreamWriter(Path);
            _writer.WriteLine("time,dt,up_mm,side_mm,fwd_mm,pitch,yaw,roll,phase,step,speed,looseness,slope,load,caution,lungs,event,duck,openness,squint,eye_yaw,eye_pitch");
            _started = now;
            Debug.Log($"[Hodba] Запись тела: {Path}");
        }

        public void Stop()
        {
            if (_writer == null) return;
            _writer.Dispose();
            _writer = null;
            Debug.Log($"[Hodba] Запись тела остановлена: {Path}");
        }

        public void MarkStep(in StepEvent e)
        {
            if (e.Felt) _stepFlag = e.Left ? -1 : 1;
        }

        public void Write(double now, float dt, WalkerBody body, WalkSim sim)
        {
            if (_writer == null) return;
            if (now - _started > MaxSeconds)
            {
                Stop();
                return;
            }
            var p = body.Pose;
            var g = body.Gait;
            var x = body.Exertion;
            var c = CultureInfo.InvariantCulture;
            _writer.WriteLine(string.Join(",",
                (now - _started).ToString("0.0000", c), dt.ToString("0.0000", c),
                (p.Up * 1000f).ToString("0.000", c), (p.Side * 1000f).ToString("0.000", c), (p.Forward * 1000f).ToString("0.000", c),
                p.Pitch.ToString("0.0000", c), p.Yaw.ToString("0.0000", c), p.Roll.ToString("0.0000", c),
                g.Phase.ToString("0.000", c), _stepFlag.ToString(c),
                sim.Speed.ToString("0.000", c), sim.Looseness.ToString("0.000", c), sim.Slope.ToString("0.000", c),
                x.Load.ToString("0.000", c), x.Caution.ToString("0.000", c), x.Lungs.ToString("0.000", c),
                g.EventStrength.ToString("0.000", c), body.Duck.ToString("0.000", c),
                body.Eyelids.Openness.ToString("0.000", c), body.Eyelids.Squint.ToString("0.000", c),
                body.Eyes.Yaw.ToString("0.000", c), body.Eyes.Pitch.ToString("0.000", c)));
            _stepFlag = 0;
        }

        public void Dispose() => Stop();
    }
}
