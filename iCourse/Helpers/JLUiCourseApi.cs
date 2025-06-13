﻿using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.Models;
using iCourse.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Windows;

namespace iCourse.Helpers
{
    public class JLUiCourseApi
    {
        private Http client;
        private Logger Logger => App.ServiceProvider.GetService<Logger>();

        private string username;
        private string password;
        private string uuid;
        private string token;
        private BatchInfo batch;

        public JLUiCourseApi()
        {
            WeakReferenceMessenger.Default.Register<AttemptLoginMessage>(this, AttemptLoginAsync);
        }

        ~JLUiCourseApi()
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }

        private async Task<string> FetchCaptchaAsync()
        {
            client.SetOrigin("https://icourses.jlu.edu.cn");
            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");

            const string captchaEndpoint = "xsxk/auth/captcha";

            var result = await client.HttpPostAsync(captchaEndpoint, null);
            var json = JObject.Parse(result);
            uuid = json["data"]["uuid"].ToString();
            var captchaImage = json["data"]["captcha"].ToString();
            return captchaImage[(captchaImage.IndexOf(',') + 1)..];
        }

        public async Task LoginAsync(string username_, string password_)
        {
            client = new Http(TimeSpan.FromSeconds(5));
            username = username_;
            password = password_;

            var captchaImage = await FetchCaptchaAsync();

            CaptchaWindowViewModel.ShowWindow(captchaImage);
        }

        public async Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize)
        {
            var content = new StringContent(
                $"{{\"teachingClassType\":\"ALLKC\",\"pageNumber\":{index},\"pageSize\":{pageMaxSize},\"orderBy\":\"\",\"teachingClassType\": \"XGKC\"}}",
                Encoding.UTF8, "application/json");
            var response = await client.HttpPostAsync("xsxk/elective/jlu/clazz/list", content);
            if (response.StartsWith('<'))
            {
                Logger.WriteLine("查询失败!");
                return [];
            }

            var json = JObject.Parse(response);

            if (json["code"].ToObject<int>() != 200)
            {
                Logger.WriteLine("查询失败!");
                return [];
            }

            var courses = json["data"]["rows"].ToList();
            return courses.Select(course => new Course(course)).ToList();
        }

        public async Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize, string key)
        {
            var content = new StringContent(
                $"{{\"teachingClassType\":\"ALLKC\",\"pageNumber\":{index},\"pageSize\":{pageMaxSize},\"orderBy\":\"\",\"teachingClassType\": \"XGKC\",\"KEY\":\"{key}\"}}",
                Encoding.UTF8, "application/json");
            var response = await client.HttpPostAsync("xsxk/elective/jlu/clazz/list", content);
            if (response.StartsWith('<'))
            {
                Logger.WriteLine("查询失败!");
                return [];
            }

            var json = JObject.Parse(response);

            if (json["code"].ToObject<int>() != 200)
            {
                Logger.WriteLine("查询失败!");
                return [];
            }

            var courses = json["data"]["rows"].ToList();
            return courses.Select(course => new Course(course)).ToList();
        }

        public async Task AddToFavoritesAsync(Course course)
        {
            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId=" + batch.batchId);
            client.SetOrigin("https://icourses.jlu.edu.cn");

            var response = await client.HttpPostAsync("xsxk/sc/clazz/add", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"clazzId", course.CourseId},
                {"secretVal", course.SecretVal},
                {"clazzType", "XGKC"}
            }));

            if (response.StartsWith('<'))
            {
                Logger.WriteLine("添加失败!");
                return;
            }

            var json = JObject.Parse(response);
            if (json["code"].ToObject<int>() == 200)
            {
                Logger.WriteLine($"已添加课程{course.Name}到收藏");
                return;
            }
        }

        private async void AttemptLoginAsync(object recipient, AttemptLoginMessage message)
        {

            var response = await PostLoginAsync(message.Captcha);
            var json = JObject.Parse(response);

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

                var batchInfos = GetBatchInfo(json);

                var credentials = App.ServiceProvider.GetService<UserCredentials>();


                if (credentials.AutoSelectBatch && !string.IsNullOrEmpty(credentials.LastBatchId))
                {
                    foreach (var batchInfo in batchInfos.Where(batchInfo => batchInfo.batchId == credentials.LastBatchId))
                    {
                        await SetBatchIdAsync(batchInfo);
                        return;
                    }
                }
                SelectBatchViewModel.ShowWindow(batchInfos);
                return;
            }

            Logger.WriteLine($"错误:{code}, {msg}");
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

        private List<BatchInfo> GetBatchInfo(JObject loginResponse)
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
                WeakReferenceMessenger.Default.Send<SetBatchFinishedMessage>(new SetBatchFinishedMessage(batch));
            }
            else
            {
                Logger.WriteLine(json["msg"].ToString());
            }

            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");

            await client.HttpGetAsync("xsxk/elective/grablessons?batchId=" + batch.batchId);

            KeepOnline();
        }

        private async Task<List<Course>> GetFavoriteCoursesAsync()
        {
            var coursesList = new List<Course>();

            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId=" + batch.batchId);
            var response = await client.HttpPostAsync("xsxk/sc/clazz/list", null);
            var json = JObject.Parse(response);

            if (json["code"].ToObject<int>() == 200)
            {
                var courses = json["data"];
                foreach (var course in courses)
                {
                    var courseInfo = new Course(course);
                    coursesList.Add(courseInfo);
                }
            }
            else
            {
                Logger.WriteLine(json["msg"].ToString());
            }

            Logger.WriteLine("收藏中的课程:\n" + string.Join("\n", coursesList.Select(c => c.Name)));
            return coursesList;
        }

        private async Task<(bool isSuccess, string? msg)> SelectCourseAsync(Course courseInfo)
        {
            client.SetReferer("https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId=" + batch.batchId);
            while (true)
            {
                var response = await client.HttpPostAsync("xsxk/sc/clazz/addxk", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"clazzId", courseInfo.CourseId},
                    {"secretVal", courseInfo.SecretVal},
                    {"clazzType", courseInfo.SelectType.ToString() }
                }));

                var json = JObject.Parse(response);

                var code = json["code"].ToObject<int>();

                if (code == 200)
                {
                    MessageBox.Show(json["msg"].ToString());
                    Logger.WriteLine("已选课程:" + courseInfo.Name);
                    return (true, null);
                }

                var msg = json["msg"].ToString();
                if (msg == "该课程已在选课结果中")
                {
                    Logger.WriteLine(courseInfo.Name + " : " + msg);
                    Logger.WriteLine(courseInfo.Name + " : 已放弃,尝试选下一门课程");
                    return (true, null);
                }

                if (msg == "课容量已满")
                {
                    Logger.WriteLine(courseInfo.Name + " : " + msg);
                    Logger.WriteLine(courseInfo.Name + " : 已放弃,尝试选下一门课程");
                    return (false, msg);
                }

                Logger.WriteLine(courseInfo.Name + " : 选课失败,原因：" + msg);
                Logger.WriteLine(courseInfo.Name + " : 重新尝试...");
                await Task.Delay(200 + new Random().Next(0, 200));
            }
        }

        public async void StartSelectClassAsync()
        {
            var list = await GetFavoriteCoursesAsync();

            int totalTasks = list.Count;
            int completedTasks = 0;

            var tasks = list.Select(async course =>
            {
                var (isSuccess, msg) = await SelectCourseAsync(course);


                int currentCompleted = Interlocked.Increment(ref completedTasks);
                WeakReferenceMessenger.Default.Send<SelectCourseFinishedMessage>(new SelectCourseFinishedMessage(currentCompleted, totalTasks));

                return new { course.Name, isSuccess, msg };
            }).ToList();

            var results = await Task.WhenAll(tasks);

            Logger.WriteLine("选课完成!");

            var failedCourses = results.Where(result => !result.isSuccess).ToList();
            var successfulCount = results.Count(result => result.isSuccess);

            foreach (var result in failedCourses)
            {
                Logger.WriteLine($"课程选择失败: {result.Name}, 原因: {result.msg}");
            }

            Logger.WriteLine($"选择成功课程的数目: {successfulCount}");
        }

        private void KeepOnline()
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
