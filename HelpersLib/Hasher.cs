using System;
using System.Text;
using System.Security.Cryptography;

namespace HelpersLib
{
    public class Hasher
    {
        protected SHA256 Sha;

        public Hasher()
        {
            Sha = SHA256Managed.Create();
        }

        public byte[] GetSha256Hash64(string input)
        {
            int halfLenght = input.Length / 2;
            var half1 = input.Substring(0, halfLenght);
            var half2 = input.Substring(halfLenght, input.Length - halfLenght);

            var hash1 = Sha.ComputeHash(Encoding.UTF8.GetBytes(half1));
            var hash2 = Sha.ComputeHash(Encoding.UTF8.GetBytes(half2));

            byte[] result = new byte[hash1.Length + hash2.Length];
            Buffer.BlockCopy(hash1, 0, result, 0, hash1.Length);
            Buffer.BlockCopy(hash2, 0, result, hash1.Length, hash2.Length);

            return result;
        }

        string GetSha256HexString(string input)
        {
            // Convert the input string to a byte array and compute the hash.
            byte[] data = Sha.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Create a new Stringbuilder to collect the bytes
            // and create a string.
            StringBuilder sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data 
            // and format each one as a hexadecimal string.
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }

            // Return the hexadecimal string.
            return sBuilder.ToString();
        }
    }
}
