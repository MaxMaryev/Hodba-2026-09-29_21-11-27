using System;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Настоящие часы. Утро в игре наступает тогда же, когда у игрока (Docs/Design/09-sky-weather.md).
    /// Солнце — по настоящей формуле для широты мира и дня года.
    /// </summary>
    public sealed class SkyClock
    {
        static readonly float[] Presets = { 5.8f, 7.2f, 12.5f, 17.6f, 19.0f, 23.5f };

        public float Hour { get; private set; }
        public int DayOfYear { get; private set; }
        /// <summary>Высота солнца над горизонтом, °.</summary>
        public float Elevation { get; private set; }
        /// <summary>Азимут солнца, ° от севера по часовой.</summary>
        public float Azimuth { get; private set; }
        /// <summary>Направление НА солнце.</summary>
        public Vector3 SunDirection { get; private set; }

        float? _runtimeHour;
        int _preset = -1;

        public void CyclePreset()
        {
            _preset = (_preset + 1) % (Presets.Length + 1);
            _runtimeHour = _preset < Presets.Length ? Presets[_preset] : (float?)null; // последний — снова настоящие часы
        }

        public void Tick(FieldConfig config)
        {
            var now = DateTime.Now;
            Hour = config.overrideTime ? config.overrideHour
                 : _runtimeHour ?? (float)now.TimeOfDay.TotalHours;
            DayOfYear = config.dayOfYearOverride > 0 ? config.dayOfYearOverride : now.DayOfYear;

            double lat = config.latitude * Math.PI / 180.0;
            double decl = -23.44 * Math.PI / 180.0 * Math.Cos(2.0 * Math.PI * (DayOfYear + 10) / 365.0);
            double hourAngle = (Hour - 12.0) * 15.0 * Math.PI / 180.0;

            double sinEl = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(hourAngle);
            double el = Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinEl)));

            double cosAz = (Math.Sin(decl) - Math.Sin(el) * Math.Sin(lat)) / Math.Max(1e-6, Math.Cos(el) * Math.Cos(lat));
            double az = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosAz)));
            if (hourAngle > 0) az = 2.0 * Math.PI - az; // после полудня солнце на западе

            Elevation = (float)(el * 180.0 / Math.PI);
            Azimuth = (float)(az * 180.0 / Math.PI);

            float ce = Mathf.Cos((float)el);
            SunDirection = new Vector3(Mathf.Sin((float)az) * ce, Mathf.Sin((float)el), Mathf.Cos((float)az) * ce);
        }
    }
}
