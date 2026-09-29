using System;
using System.Globalization;
using Hodba.Core;
using Hodba.Sim.Walk;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Простейший «доверенный путь» без сервера: убрал телефон на ходу — человек шёл дальше по курсу.
    /// Грубая проверка ощущения; настоящий фон считает сервер (Docs/Design/01-background-walk.md).
    /// </summary>
    public static class WalkerSave
    {
        const string Key = "hodba.walker.v1";

        public struct State
        {
            public WorldPos Position;
            public float Course;
            public bool Walking;
            public DateTime SavedUtc;
        }

        public static void Save(WalkSim sim)
        {
            var s = string.Join(";",
                sim.Position.X.ToString(CultureInfo.InvariantCulture),
                sim.Position.Z.ToString(CultureInfo.InvariantCulture),
                sim.Course.ToString(CultureInfo.InvariantCulture),
                sim.WantsWalk ? "1" : "0",
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.SetString(Key, s);
            PlayerPrefs.Save();
        }

        public static bool TryLoad(out State state)
        {
            state = default;
            var s = PlayerPrefs.GetString(Key, null);
            if (string.IsNullOrEmpty(s)) return false;
            var p = s.Split(';');
            if (p.Length < 5) return false;
            try
            {
                state.Position = new WorldPos(
                    long.Parse(p[0], CultureInfo.InvariantCulture),
                    long.Parse(p[1], CultureInfo.InvariantCulture));
                state.Course = float.Parse(p[2], CultureInfo.InvariantCulture);
                state.Walking = p[3] == "1";
                state.SavedUtc = new DateTime(long.Parse(p[4], CultureInfo.InvariantCulture), DateTimeKind.Utc);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static void Clear() => PlayerPrefs.DeleteKey(Key);

        /// <summary>Сколько прошёл без игрока: прямо по курсу, обычным шагом.</summary>
        public static WorldPos Advance(State state, float speed, float maxHours, out double meters)
        {
            meters = 0;
            if (!state.Walking) return state.Position;
            double seconds = (DateTime.UtcNow - state.SavedUtc).TotalSeconds;
            seconds = Math.Max(0, Math.Min(seconds, maxHours * 3600.0));
            meters = seconds * speed;
            double rad = state.Course * Math.PI / 180.0;
            return state.Position.Offset(
                (long)(Math.Sin(rad) * meters * 1000.0),
                (long)(Math.Cos(rad) * meters * 1000.0));
        }
    }
}
