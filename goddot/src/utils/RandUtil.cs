using System;

namespace BattleKing.Utils
{
    public static class RandUtil
    {
        private static readonly Random _random = new Random();

        public static int Roll100()
        {
            return _random.Next(0, 100);
        }

        public static int Roll(int maxValue)
        {
            return _random.Next(0, maxValue);
        }

        public static int Roll(int minValue, int maxValue)
        {
            return _random.Next(minValue, maxValue);
        }

        public static bool RollPercent(int percent)
        {
            return _random.Next(0, 100) < percent;
        }
    }
}
