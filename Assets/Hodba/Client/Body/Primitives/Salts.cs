namespace Hodba.Client.Body
{
    /// <summary>
    /// Соли хэшей тела — все в одном месте, чтобы два модуля случайно не зашумели одинаково.
    /// Новая соль — новая строка; старые не менять, иначе у человека сменится походка.
    /// </summary>
    public static class Salts
    {
        public const uint Signature = 0x51_00;
        public const uint GaitRhythm = 0x51_10;
        public const uint GaitDrift = 0x51_11;
        public const uint GaitEvents = 0x51_12;
        public const uint Breath = 0x51_20;
        public const uint BreathDrift = 0x51_21;
        public const uint Posture = 0x51_30;
        public const uint Exertion = 0x51_40;
        public const uint Eyelids = 0x51_50;
        public const uint EyeWander = 0x51_60;
        public const uint Attention = 0x51_70;
    }
}
