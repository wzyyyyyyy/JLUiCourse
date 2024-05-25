using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace iCourse
{
    public class Web
    {
        private Http client;

        private string AESKey;
        private readonly string username;
        private string password;
        private string uuid;
        private string captcha;
        private string token;
        private JObject login_response;

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

        private async Task<string> GetAESKeyAsync()
        {
            var response = await client.HttpGetAsync("");
            var match = Regex.Match(response, "loginVue\\.loginForm\\.aesKey\\s*=\\s*\"([^\"]+)\"");
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
        public async Task<(int, string)> LoginAsync()
        {
            client = new Http(TimeSpan.FromSeconds(2), 3);

            // 获取AESKey
            await GetAESKeyAsync();
            byte[] encryptedBytes = EncryptWithAES(password, AESKey);
            password = Convert.ToBase64String(encryptedBytes);
            client.SetOrigin("https://icourses.jlu.edu.cn");
            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");

            // 获取验证码
            var result = await client.HttpPostAsync("xsxk/auth/captcha", null);
            var json = JObject.Parse(result);
            uuid = json["data"]["uuid"].ToString();
            var captchaImage = json["data"]["captcha"].ToString();
            captchaImage = captchaImage.Substring(captchaImage.IndexOf(",") + 1);

            await GetCaptchaAsync(captchaImage);

            // 登录
            var response = await PostLoginAsync();
            json = JObject.Parse(response);
            login_response = json;

            int code = json["code"].ToObject<int>();
            string msg = json["msg"].ToString();

            // 验证码错误
            if (msg == "验证码错误")
            {
                (code, msg) = await LoginAsync();
            }

            // 登录失败
            if (code != 200)
            {
                return (code, msg);
            }

            // 登录成功
            if (json.ContainsKey("data"))
            {
                token = json["data"]["token"].ToString();
            }

            return (code, msg);
        }

        private async Task<string> PostLoginAsync()
        {
            var response = await client.HttpPostAsync("xsxk/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"loginname", username},
                {"password", password},
                {"captcha", captcha},
                {"uuid", uuid}
            }));
            return response;
        }

        private async Task<string> GetCaptchaAsync(string base64Image)
        {
            await MainWindow.Instance.Dispatcher.InvokeAsync(() =>
            {
                CaptchaWindow captchaWindow = new CaptchaWindow(base64Image);
                captchaWindow.ShowDialog();
                captcha = CaptchaWindow.Captcha;
            });
            return captcha;
        }

        public JObject GetLoginResponse()
        {
            return login_response;
        }

        public async Task<List<BatchInfo>> GetBatchInfoAsync()
        {
            var batchInfos = new List<BatchInfo>();
            await Task.Run(() =>
            {
                login_response["data"]["student"]["electiveBatchList"].ToList().ForEach(batch =>
                {
                    var batchInfo = new BatchInfo
                    {
                        batchCode = batch["code"].ToString(),
                        batchName = batch["name"].ToString(),
                        beginTime = batch["beginTime"].ToString(),
                        endTime = batch["endTime"].ToString(),
                        tacticName = batch["tacticName"].ToString(),
                        noSelectReason = batch["noSelectReason"].ToString(),
                        typeName = batch["typeName"].ToString(),
                        canSelect = batch["canSelect"].ToString() != "0" //你鸡为什么用字符串，逆天
                    };
                    batchInfos.Add(batchInfo);
                });
            });
            return batchInfos;
        }
    }
}
