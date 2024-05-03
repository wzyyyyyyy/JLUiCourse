using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace iCourse
{
    internal class Web
    {
        private HttpClient client;

        private string AESKey;
        private string username;
        private string password;
        private string uuid;
        private string captcha;
        private string login_response;

        public Web(string username, string password)
        {
            this.username = username;
            this.password = password;
        }

        private byte[] EncryptWithAES(string plainText, string aesKey)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(aesKey);

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = keyBytes;
                aesAlg.Mode = CipherMode.ECB;
                aesAlg.Padding = PaddingMode.PKCS7;

                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

                // 加密
                byte[] encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                return encryptedBytes;
            }
        }

        private async Task<string> getAESKeyAsync()
        {
            var response = client.GetAsync("").Result;
            var content = response.Content.ReadAsStringAsync().Result;

            var match = Regex.Match(content, "loginVue\\.loginForm\\.aesKey\\s*=\\s*\"([^\"]+)\"");
            if (match.Success)
            {
                AESKey = match.Groups[1].Value;
                return AESKey;
            }
            else
            {
                throw new InvalidOperationException("aesKey not found in content.");
            }
        }

        //code msg
        public (int,string,string) Login()
        {
            client = new HttpClient();

            client.BaseAddress = new Uri("https://icourses.jlu.edu.cn/");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");
            client.DefaultRequestHeaders.Add("Host", "icourses.jlu.edu.cn");
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/103.0.5060.114 Safari/537.36 Edg/103.0.1264.62");

            getAESKeyAsync().Wait();
            byte[] encryptedBytes = EncryptWithAES(password, AESKey);
            password = Convert.ToBase64String(encryptedBytes);

            client.DefaultRequestHeaders.Add("Origin", "https://icourses.jlu.edu.cn");
            client.DefaultRequestHeaders.Add("Referer", "https://icourses.jlu.edu.cn/xsxk/profile/index.html");

            var result = client.PostAsync("xsxk/auth/captcha", null).Result.Content.ReadAsStringAsync().Result;

            var json = JObject.Parse(result);
            uuid = json["data"]["uuid"].ToString();
            var captchaImage = json["data"]["captcha"].ToString();
            captchaImage = captchaImage.Substring(captchaImage.IndexOf(",") + 1);

            getCaptcha(captchaImage);

            var response = postLogin();
            json = JObject.Parse(response);

            int code = json["code"].ToObject<int>();
            string msg = json["msg"].ToString();

            if (json["msg"].ToString() == "验证码错误")
            {
                (code, msg, response) = Login();
            }

            login_response = response;

            return (code, msg,response);
        }

        private string postLogin()
        {
            var response = client.PostAsync("xsxk/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"loginname", username},
                {"password", password},
                {"captcha", captcha},
                {"uuid", uuid}
            })).Result.Content.ReadAsStringAsync().Result;
            return response;
        }

        private string getCaptcha(string base64Image)
        {
            CaptchaWindow captchaWindow = new CaptchaWindow(base64Image);
            captchaWindow.ShowDialog();
            captcha = CaptchaWindow.Captcha;
            return CaptchaWindow.Captcha;
        }

        public List<>
    }
}
