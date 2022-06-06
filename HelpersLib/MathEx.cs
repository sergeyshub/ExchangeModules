using System;
using System.Globalization;
using System.Threading;

namespace HelpersLib
{
    public static class MathEx
    {
        public static decimal RoundDown(decimal number, int decimalPlaces)
        {
            var power = Convert.ToDecimal(Math.Pow(10, decimalPlaces));
            return Math.Floor(number * power) / power;
        }

        public static int GetDecimalCount(decimal number)
        {
            var text = number.ToString().TrimEnd('0');
            var position = text.IndexOf('.');
            if (position == -1) return 0;
            return text.Length - position - 1;
        }

        public static string ToString(decimal number)
        {
            return number.ToString(CultureInfo.CreateSpecificCulture("en-GB"));
        }

        // Formats: "N2" - 1,111.11 "#.00" - 1111.11
        public static string ToString(decimal number, string format)
        {
            return number.ToString(format, CultureInfo.CreateSpecificCulture("en-GB"));
        }

        public static decimal ParseDecimal(string s)
        {
            return decimal.Parse(s.Replace(".", Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator), NumberStyles.Float);
        }
    }
}
