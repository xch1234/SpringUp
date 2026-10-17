using System;

namespace Tideflesh.Systems
{
    public static class EconomySwing
    {
        public const double PurchaseHalfPercent = 10;

        // unit01 为 [0, 1]。返回值是百分比前的数，例如 -2.6，不收成整数。
        public static double Percent(double unit01, double halfRangePercent)
        {
            if (double.IsNaN(unit01) || double.IsInfinity(unit01))
                unit01 = 0.5;
            if (unit01 < 0) unit01 = 0;
            else if (unit01 > 1) unit01 = 1;
            return (unit01 * 2.0 - 1.0) * halfRangePercent;
        }

        public static int Floor(double amount, double unit01, double halfRangePercent)
        {
            double percent = Percent(unit01, halfRangePercent);
            return (int)Math.Floor(amount * (1.0 + percent / 100.0));
        }

        public static int RoundAwayFromZero(int amount, double unit01, double halfRangePercent)
        {
            double percent = Percent(unit01, halfRangePercent);
            double value = amount * (1.0 + percent / 100.0);
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
