using System;

namespace HelpersLib
{
    public static class DateTimeEx
    {
        public static DateTime NowSeconds()
        {
            return RoundToSeconds(DateTime.Now);
        }

        public static DateTime RoundToSeconds(DateTime dt)
        {
            DateTime dtRounded = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
            if (dt.Millisecond >= 500) dtRounded = dtRounded.AddSeconds(1);

            return dtRounded;
        }
    }
}
