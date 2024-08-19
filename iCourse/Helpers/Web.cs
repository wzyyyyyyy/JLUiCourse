using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.Models;
using iCourse.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;

namespace iCourse.Helpers
{
    public class Web
    {
        private Http client;
        private Logger Logger => App.ServiceProvider.GetService<Logger>();

        private string username;
        private string password;
        private string uuid;
        private string token;
        private JObject loginResponse;
        private BatchInfo batch;

        private async Task<string> FetchCaptchaAsync()
        {
            client.SetOrigin("https://icourses.jlu.edu.cn");
            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");

            const string captchaEndpoint = "xsxk/auth/captcha";

            var result = await client.HttpPostAsync(captchaEndpoint, null);
            var json = JObject.Parse(result);
            uuid = json["data"]["uuid"].ToString();
            var captchaImage = json["data"]["captcha"].ToString();
            return captchaImage.Substring(captchaImage.IndexOf(",", StringComparison.Ordinal) + 1);
        }

        private static void ShowCaptchaWindow(string base64Image)
        {
            WeakReferenceMessenger.Default.Send<ShowWindowMessage>(new ShowWindowMessage(typeof(CaptchaWindowViewModel), base64Image));
        }

        public async Task LoginAsync(string username_, string password_)
        {
            client = new Http(TimeSpan.FromSeconds(5));
            username = username_;
            password = password_;

            var captchaImage = await FetchCaptchaAsync();

            WeakReferenceMessenger.Default.Register<AttemptLoginMessage>(this, AttemptLoginAsync);

            ShowCaptchaWindow(captchaImage);
        }

        private async Task<string> PostLoginAsync(string captcha)
        {
            var encryptedPassword = await new EncryptHelper(client).EncryptWithAesAsync(password);
            var response = await client.HttpPostAsync("xsxk/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"loginname", username},
                {"password", encryptedPassword},
                {"captcha", captcha},
                {"uuid", uuid}
            }));
            return response;
        }

        private async void AttemptLoginAsync(object recipient, AttemptLoginMessage message)
        {
            WeakReferenceMessenger.Default.Unregister<AttemptLoginMessage>(this);

            var response = await PostLoginAsync(message.Captcha);
            var json = JObject.Parse(response);
            loginResponse = json;

            var code = json["code"].ToObject<int>();
            var msg = json["msg"].ToString();

            if (msg == "验证码错误")
            {
                Logger.WriteLine(msg);
                _ = LoginAsync(username, password);
                return;
            }

            if (code == 200 && json.ContainsKey("data"))
            {
                Logger.WriteLine(msg);

                token = json["data"]["token"].ToString();

                var studentName = json["data"]["student"]["XM"].ToString();
                var studentId = json["data"]["student"]["XH"].ToString();
                var collage = json["data"]["student"]["YXMC"].ToString();

                Logger.WriteLine($"姓名：{studentName}");
                Logger.WriteLine($"学号：{studentId}");
                Logger.WriteLine($"学院：{collage}");

                WeakReferenceMessenger.Default.Send<LoginSuccessMessage>(new LoginSuccessMessage());
                WeakReferenceMessenger.Default.Register<StartSelectClassMessage>(this, StartSelectClassAsync);

                var batchInfos = GetBatchInfo();

                var credentials = UserCredentials.Load();

                if (credentials.AutoSelectBatch && !string.IsNullOrEmpty(credentials.LastBatchId))
                {
                    var batchInfo = batchInfos.First(batchInfo => batchInfo.batchId == credentials.LastBatchId);
                    if (batchInfo != null)
                    {
                        WeakReferenceMessenger.Default.Send<StartSelectClassMessage>(
                            new StartSelectClassMessage(batchInfo));
                        return;
                    }
                }

                ShowSelectBatchWindow(batchInfos);
                return;
            }

            Logger.WriteLine($"错误:{code}, {msg}");
        }

        private void ShowSelectBatchWindow(List<BatchInfo> batchInfos)
        {
            WeakReferenceMessenger.Default.Send<ShowWindowMessage>(new ShowWindowMessage(typeof(SelectBatchViewModel),
                batchInfos));
        }

        public List<BatchInfo> GetBatchInfo()
        {
            var batchInfos = new List<BatchInfo>();
            loginResponse["data"]["student"]["electiveBatchList"].ToList().ForEach(batch =>
            {
                var batchInfo = new BatchInfo
                {
                    batchId = batch["code"].ToString(),
                    batchName = batch["name"].ToString(),
                    beginTime = batch["beginTime"].ToString(),
                    endTime = batch["endTime"].ToString(),
                    tacticName = batch["tacticName"].ToString(),
                    noSelectReason = batch["noSelectReason"].ToString(),
                    typeName = batch["typeName"].ToString(),
                    canSelect = batch["canSelect"].ToString() != "0"
                };
                batchInfos.Add(batchInfo);
            });
            return batchInfos;
        }

        public async Task SetBatchIdAsync(BatchInfo batch)
        {
            this.batch = batch;
            client.SetOrigin("https://icourses.jlu.edu.cn");
            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");
            client.AddHeader("Authorization", token);
            var response = await client.HttpPostAsync("xsxk/elective/user", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"batchId", batch.batchId}
            }));
            var json = JObject.Parse(response);
            if (json["code"].ToObject<int>() == 200)
            {
                Logger.WriteLine("选课批次设置成功");
                Logger.WriteLine("已选批次:" + batch.batchName);
            }
            else
            {
                Logger.WriteLine(json["msg"].ToString());
            }

            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");

            await client.HttpGetAsync("xsxk/elective/grablessons?batchId=" + batch.batchId);
        }

        public async Task<List<CourseInfo>> GetFavoriteCoursesAsync()
        {
            var coursesList = new List<CourseInfo>();

            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId=" + batch.batchId);
            var response = await client.HttpPostAsync("xsxk/sc/clazz/list", null);
            var json = JObject.Parse(response);

            if (json["code"].ToObject<int>() == 200)
            {
                var courses = json["data"];
                foreach (var course in courses)
                {
                    var courseInfo = new CourseInfo
                    {
                        courseName = course["KCM"].ToString(),
                        courseID = course["JXBID"].ToString(),
                        secretVal = course["secretVal"].ToString(),
                        clazzType = course["teachingClassType"].ToString()
                    };
                    coursesList.Add(courseInfo);
                }
            }
            else
            {
                Logger.WriteLine(json["msg"].ToString());
            }

            Logger.WriteLine("收藏中的课程:\n" + string.Join("\n", coursesList.Select(c => c.courseName)));
            return coursesList;
        }

        public async Task<(bool isSuccess, string? msg)> SelectCourseAsync(CourseInfo courseInfo)
        {
            while (true)
            {
                var response = await client.HttpPostAsync("xsxk/sc/clazz/addxk", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"clazzId", courseInfo.courseID},
                    {"secretVal", courseInfo.secretVal},
                    {"clazzType", courseInfo.clazzType }
                }));

                var json = JObject.Parse(response);

                var code = json["code"].ToObject<int>();

                if (code == 200)
                {
                    Logger.WriteLine("已选课程:" + courseInfo.courseName);
                    return (true, null);
                }

                var msg = json["msg"].ToString();
                if (msg == "该课程已在选课结果中")
                {
                    Logger.WriteLine(courseInfo.courseName + " : " + msg);
                    Logger.WriteLine(courseInfo.courseName + " : 已放弃,尝试选下一门课程");
                    return (true, null);
                }

                if (msg == "课容量已满")
                {
                    Logger.WriteLine(courseInfo.courseName + " : " + msg);
                    Logger.WriteLine(courseInfo.courseName + " : 已放弃,尝试选下一门课程");
                    return (false, msg);
                }

                Logger.WriteLine(courseInfo.courseName + " : 选课失败,原因：" + msg);
                Logger.WriteLine(courseInfo.courseName + " : 重新尝试...");
                await Task.Delay(200 + new Random().Next(0, 200));
            }
        }

        private async void StartSelectClassAsync(object recipient, StartSelectClassMessage msg)
        {
            await SetBatchIdAsync(msg.BatchInfo);
            var list = await GetFavoriteCoursesAsync();
            KeepOnline();

            int totalTasks = list.Count;
            int completedTasks = 0;

            var tasks = list.Select(async course =>
            {
                var (isSuccess, msg) = await SelectCourseAsync(course);

                
                int currentCompleted = Interlocked.Increment(ref completedTasks);
                WeakReferenceMessenger.Default.Send<SelectCourseFinishedMessage>(new SelectCourseFinishedMessage(currentCompleted, totalTasks));

                return new { course.courseName, isSuccess, msg };
            }).ToList();

            var results = await Task.WhenAll(tasks);

            Logger.WriteLine("选课完成!");

            var failedCourses = results.Where(result => !result.isSuccess).ToList();
            var successfulCount = results.Count(result => result.isSuccess);

            foreach (var result in failedCourses)
            {
                Logger.WriteLine($"课程选择失败: {result.courseName}, 原因: {result.msg}");
            }

            Logger.WriteLine($"选择成功课程的数目: {successfulCount}");
        }

        public void KeepOnline()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    var response = await client.HttpPostAsync("xsxk/sc/clazz/list", null);
                    if (response.StartsWith('<'))
                    {
                        _ = Task.Run(() =>
                        {
                            MessageBox.Show("检测到掉线，将在1秒后重启本软件！！！");
                        });

                        var timer = new System.Timers.Timer(1000);
                        timer.Elapsed += (sender, e) =>
                        {
                            timer.Stop();
                            var appPath = Process.GetCurrentProcess().MainModule.FileName;

                            try
                            {
                                Process.Start(appPath);
                                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"发生错误: {ex.Message}");
                            }
                        };

                        timer.Start();

                        return;
                    }
                    await Task.Delay(1500);
                }
            });
        }
    }
}
