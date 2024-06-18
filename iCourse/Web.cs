using Newtonsoft.Json.Linq;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

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

        //code,msg
        public async Task<(int, string)> LoginAsync()
        {
            client = new Http(TimeSpan.FromSeconds(5));

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
                MainWindow.Instance.WriteLine(msg);
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

        public List<BatchInfo> GetBatchInfo()
        {
            var batchInfos = new List<BatchInfo>();
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
            return batchInfos;
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

        public async void SetBatchID(BatchInfo batch)
        {
            client.SetOrigin("https://icourses.jlu.edu.cn");
            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");
            client.AddHeader("Authorization", token);
            var response = await client.HttpPostAsync("xsxk/elective/user", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"batchId", batch.batchCode}
            }));
            var json = JObject.Parse(response);
            if (json["code"].ToObject<int>() == 200)
            {
                MainWindow.Instance.WriteLine("选课批次设置成功");
                MainWindow.Instance.WriteLine("已选批次:"+ batch.batchName);
            }
            else
            {
                MainWindow.Instance.WriteLine(json["msg"].ToString());
            }

            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");

            await client.HttpGetAsync("xsxk/elective/grablessons?batchId=" + batch.batchCode);
        }

        public async Task<List<Course>> GetFavoriteCourses(BatchInfo batch)
        {
            List<Course> list = new List<Course>();

            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId="+batch.batchCode);
            var response = await client.HttpPostAsync("xsxk/sc/clazz/list", null);
            var json = JObject.Parse(response);

            var msg = String.Empty;

            if (json["code"].ToObject<int>() == 200)
            {
                var courses = json["data"];
                foreach (var course in courses)
                {
                    var courseInfo = new Course
                    {
                        courseName = course["KCM"].ToString(),
                        courseID = course["JXBID"].ToString(),
                        secretVal = course["secretVal"].ToString(),
                        clazzType = course["teachingClassType"].ToString()
                    };
                    msg+= courseInfo.courseName + "\n";
                    list.Add(courseInfo);
                }
            }
            else
            {
                MainWindow.Instance.WriteLine(json["msg"].ToString());
            }

            MainWindow.Instance.WriteLine("收藏中的课程:\n" + msg);
            return list;
        }

        public async void SelectCourse(BatchInfo batch, Course course)
        {
            var response = await client.HttpPostAsync("xsxk/sc/clazz/addxk", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"clazzId", course.courseID},
                {"secretVal", course.secretVal},
                {"clazzType",course.clazzType }
            }));

            var json = JObject.Parse(response);

            var code = json["code"].ToObject<int>();

            if (code == 200)
            {
                MainWindow.Instance.WriteLine("选课成功");
                MainWindow.Instance.WriteLine("已选课程:" + course.courseName);
            }
            else
            {
                var msg = json["msg"].ToString();
                if (msg == "该课程已在选课结果中" || msg == "课容量已满")
                {
                    MainWindow.Instance.WriteLine(course.courseName+" : "+msg);
                    MainWindow.Instance.WriteLine(course.courseName + " : 已放弃,尝试选下一门课程");
                }
                MainWindow.Instance.WriteLine(course.courseName + " : 选课失败,原因：" + msg);
                MainWindow.Instance.WriteLine(course.courseName + " : 重新尝试...");
                Thread.Sleep(300 + new Random().Next(0, 200));
                SelectCourse(batch, course);
            }
        }
    }
}
