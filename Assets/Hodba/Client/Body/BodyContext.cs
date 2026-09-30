using Hodba.Sim.Walk;
using Hodba.World;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Всё, что тело знает о мире в этом кадре. Собирается один раз и раздаётся модулям.
    /// Модули не читают Time, Input и синглтоны — только это. Поэтому их можно гонять в тестах
    /// с любым dt и они одинаково ведут себя при 30 и 60 fps.
    /// </summary>
    public readonly struct BodyContext
    {
        public readonly float Dt;
        /// <summary>Время тела, с. Своё, не Time.time: в тестах его крутят руками.</summary>
        public readonly double Time;
        public readonly WalkSim Sim;
        public readonly IWorldQuery World;

        /// <summary>Скорость воздуха, м/с, в мировых осях (x — восток, z — север).</summary>
        public readonly Vector3 Wind;
        /// <summary>0..1.</summary>
        public readonly float WindStrength;
        /// <summary>0..1 — сколько в ветре порыва.</summary>
        public readonly float WindGust;

        /// <summary>Направление на солнце, мировые оси.</summary>
        public readonly Vector3 SunDirection;
        /// <summary>Высота солнца, °.</summary>
        public readonly float SunElevation;

        /// <summary>Куда игрок повернул голову, ° (0 — север).</summary>
        public readonly float HeadYaw;
        /// <summary>Наклон головы, ° (плюс — вниз, как в Unity).</summary>
        public readonly float HeadPitch;
        /// <summary>Игрок прямо сейчас ведёт взгляд.</summary>
        public readonly bool LookInput;
        /// <summary>0..1 — насколько всмотрелся.</summary>
        public readonly float Focus;

        public BodyContext(float dt, double time, WalkSim sim, IWorldQuery world,
            Vector3 wind, float windStrength, float windGust,
            Vector3 sunDirection, float sunElevation,
            float headYaw, float headPitch, bool lookInput, float focus)
        {
            Dt = dt;
            Time = time;
            Sim = sim;
            World = world;
            Wind = wind;
            WindStrength = windStrength;
            WindGust = windGust;
            SunDirection = sunDirection;
            SunElevation = sunElevation;
            HeadYaw = headYaw;
            HeadPitch = headPitch;
            LookInput = lookInput;
            Focus = focus;
        }

        /// <summary>Только ходьба, без погоды и взгляда: для тестов и фоновых прогонов.</summary>
        public static BodyContext Walk(float dt, double time, WalkSim sim, IWorldQuery world) =>
            new BodyContext(dt, time, sim, world, Vector3.zero, 0f, 0f, Vector3.up, 45f, sim.Course, 4f, false, 0f);

        /// <summary>0..1 — насколько идёт в полную силу.</summary>
        public float SpeedNorm =>
            Sim.Params.BaseSpeed > 0f ? Mathf.Clamp01(Sim.Speed / Sim.Params.BaseSpeed) : 0f;

        /// <summary>Куда смотрит голова, мировые оси.</summary>
        public Vector3 HeadForward => Direction(HeadYaw, HeadPitch);

        /// <summary>Направление по углам: рыск от севера по часовой, тангаж плюс — вниз.</summary>
        public static Vector3 Direction(float yaw, float pitch)
        {
            float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
            float c = Mathf.Cos(p);
            return new Vector3(Mathf.Sin(y) * c, -Mathf.Sin(p), Mathf.Cos(y) * c);
        }

        /// <summary>Вперёд по курсу, мировые оси.</summary>
        public Vector3 Forward
        {
            get
            {
                float rad = Sim.Course * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            }
        }

        /// <summary>Вправо от курса, мировые оси.</summary>
        public Vector3 Right
        {
            get
            {
                float rad = Sim.Course * Mathf.Deg2Rad;
                return new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));
            }
        }
    }
}
