using System;
using Hodba.Core;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Плавающий центр мира. Всё авторитетное живёт в WorldPos (мм), а Unity видит только
    /// ближайшие километры в float. Центр переносится шагами по 512 м — так текстуры земли не прыгают.
    /// </summary>
    public sealed class FloatingOrigin
    {
        /// <summary>Кратно длинам ряби (640 и 100 мм): шейдер считает её фазу от локальной x (HodbaRipple.hlsl).</summary>
        public const long SnapMm = 512_000;
        const double ShiftDistance = 1024.0;
        const long TexturePeriodMm = 4_096_000; // все тайлы земли делят 4096 м

        static readonly int OriginModId = Shader.PropertyToID("_HodbaOriginMod");

        public WorldPos Origin { get; private set; }

        /// <summary>Сдвиг, который надо прибавить ко всем локальным позициям.</summary>
        public event Action<Vector3> Shifted;

        public FloatingOrigin(WorldPos start)
        {
            Origin = start.Snap(SnapMm);
            PushShaderGlobal();
        }

        public Vector3 ToLocal(WorldPos p, float y = 0f) =>
            new Vector3((float)((p.X - Origin.X) / 1000.0), y, (float)((p.Z - Origin.Z) / 1000.0));

        public Vector3 ToLocal(long xMm, long zMm, float y = 0f) =>
            new Vector3((float)((xMm - Origin.X) / 1000.0), y, (float)((zMm - Origin.Z) / 1000.0));

        public WorldPos ToWorld(Vector3 local) =>
            new WorldPos(Origin.X + (long)Math.Round(local.x * 1000.0), Origin.Z + (long)Math.Round(local.z * 1000.0));

        public void Tick(WorldPos focus)
        {
            double dx = (focus.X - Origin.X) / 1000.0;
            double dz = (focus.Z - Origin.Z) / 1000.0;
            if (dx * dx + dz * dz < ShiftDistance * ShiftDistance) return;
            Rebase(focus);
        }

        public void Rebase(WorldPos focus)
        {
            var next = focus.Snap(SnapMm);
            if (next == Origin) return;
            var delta = new Vector3((float)((Origin.X - next.X) / 1000.0), 0f, (float)((Origin.Z - next.Z) / 1000.0));
            Origin = next;
            PushShaderGlobal();
            Shifted?.Invoke(delta);
        }

        void PushShaderGlobal()
        {
            float mx = WorldPos.FloorMod(Origin.X, TexturePeriodMm) / 1000f;
            float mz = WorldPos.FloorMod(Origin.Z, TexturePeriodMm) / 1000f;
            Shader.SetGlobalVector(OriginModId, new Vector4(mx, mz, 0f, 0f));
        }
    }
}
