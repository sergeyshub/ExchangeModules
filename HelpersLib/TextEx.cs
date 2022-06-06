using System;
using System.Globalization;

namespace HelpersLib
{
    public static class TextEx
    {
        public static string[] ParsePairString(string pairString)
        {
            char[] delimiterChars = { '-' };
            string[] words = pairString.Split(delimiterChars);
            if (words.Length < 2) return null;
            return words;
        }
    }
}
