using System.Security.Cryptography;
using System.Text;

namespace HelpersLib
{
    public class KeyGenerator
    {
        public const string ALPHA_CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        public const string CAPITAL_CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        public const string LOWER_CHARS = "abcdefghijklmnopqrstuvwxyz";
        public const string DIGIT_CHARS = "1234567890";
        public const string DIGIT_CHARS_NO_ZERO = "123456789";

        public static string GetUniqueKey(int maxSize, string symbols)
        {
            char[] chars = new char[symbols.Length];
            chars = symbols.ToCharArray();
            byte[] data = new byte[1];
            using (RNGCryptoServiceProvider crypto = new RNGCryptoServiceProvider())
            {
                crypto.GetNonZeroBytes(data);
                data = new byte[maxSize];
                crypto.GetNonZeroBytes(data);
            }
            StringBuilder result = new StringBuilder(maxSize);
            foreach (byte b in data)
            {
                result.Append(chars[b % (chars.Length)]);
            }
            return result.ToString();
        }

        public static string GetDigitNoFirstZeroKey(int maxSize)
        {
            var first = GetUniqueKey(1, DIGIT_CHARS_NO_ZERO);
            var rest = "";
            if (1 < maxSize) rest = GetUniqueKey(maxSize - 1, DIGIT_CHARS);

            return first + rest;
        }

        public static string GetAlphaDigitKey(int maxSize)
        {
            var first = GetUniqueKey(1, ALPHA_CHARS + DIGIT_CHARS_NO_ZERO);
            var rest = "";
            if (1 < maxSize) rest = GetUniqueKey(maxSize - 1, ALPHA_CHARS + DIGIT_CHARS);

            return first + rest;
        }

        public static string GetCapAlphaDigitKey(int maxSize)
        {
            var first = GetUniqueKey(1, CAPITAL_CHARS + DIGIT_CHARS_NO_ZERO);
            var rest = "";
            if (1 < maxSize) rest = GetUniqueKey(maxSize - 1, CAPITAL_CHARS + DIGIT_CHARS);

            return first + rest;
        }

        public static string GetLowAlphaDigitKey(int maxSize)
        {
            var first = GetUniqueKey(1, LOWER_CHARS + DIGIT_CHARS_NO_ZERO);
            var rest = "";
            if (1 < maxSize) rest = GetUniqueKey(maxSize - 1, LOWER_CHARS + DIGIT_CHARS);

            return first + rest;
        }
    }
}
