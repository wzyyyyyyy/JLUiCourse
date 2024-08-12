using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace iCourse.Helpers
{
    internal class EncryptHelper(Http client)
    {
        private string aeskey;

        public async Task<string> EncryptWithAesAsync(string password)
        {
            await RetrieveAesKeyAsync();

            var keyBytes = Encoding.UTF8.GetBytes(aeskey);

            using var aesAlg = Aes.Create();
            aesAlg.Key = keyBytes;
            aesAlg.Mode = CipherMode.ECB;
            aesAlg.Padding = PaddingMode.PKCS7;

            var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            var plainBytes = Encoding.UTF8.GetBytes(password);

            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return Convert.ToBase64String(encryptedBytes);
        }

        private async Task RetrieveAesKeyAsync()
        {
            var response = await client.HttpGetAsync("");
            var match = Regex.Match(response, "loginVue\\.loginForm\\.aesKey\\s*=\\s*\"([^\"]+)\"");
            if (match.Success)
            {
                aeskey = match.Groups[1].Value;
            }
            else
            {
                throw new InvalidOperationException("AES key not found in content.");
            }
        }
    }
}
