using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.Models;
using iCourse.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
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

        private static readonly Random _random = new();
        private readonly ConcurrentQueue<(Course course, TaskCompletionSource<(bool, string?)> tcs)> _taskQueue = new();
        private readonly int _workerCount = Environment.ProcessorCount * 2;
        private readonly CancellationTokenSource _cts = new();

        private string BuildRequestBody(Course course)
        {
            return $"clazzId={Uri.EscapeDataString(course.CourseId)}&secretVal={Uri.EscapeDataString(course.SecretVal)}&clazzType={Uri.EscapeDataString(course.SelectType.ToString())}";
        }

        public void StartWorkerPool()
        {
            for (int i = 0; i < _workerCount; i++)
            {
                Task.Factory.StartNew(async () =>
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        if (_taskQueue.TryDequeue(out var workItem))
                        {
                            var (course, tcs) = workItem;
                            var result = await TrySelectCourseWithBackoffAsync(course);
                            tcs.TrySetResult(result);
                        }
                        else
                        {
                            // 无任务时等待
                            await Task.Delay(10);
                        }
                    }
                }, TaskCreationOptions.LongRunning);
            }
        }

        private async Task<(bool isSuccess, string? msg)> TrySelectCourseWithBackoffAsync(Course course)
        {
            client.DefaultRequestHeaders.Referrer = new Uri($"https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId={batch.batchId}");

            int failCount = 0;

            string requestBody = BuildRequestBody(course);
            var content = new StringContent(requestBody, Encoding.UTF8, "application/x-www-form-urlencoded");

            while (true)
            {
                try
                {
                    var response = await client.PostAsync("xsxk/sc/clazz/addxk", content);
                    var respStr = await response.Content.ReadAsStringAsync();

                    var json = JObject.Parse(respStr);
                    var code = json["code"].ToObject<int>();
                    var msg = json["msg"].ToString();

                    if (code == 200)
                    {
                        Logger.WriteLine($"成功选课: {course.Name}");
                        return (true, null);
                    }

                    Logger.WriteLine($"{course.Name} : {msg}");

                    if (msg == "该课程已在选课结果中" || msg == "课容量已满")
                    {
                        return (msg == "课容量已满" ? (false, msg) : (true, null));
                    }


                    failCount++;
                    int delay = Math.Min(500, 100 + failCount * 100 + _random.Next(0, 50));
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"{course.Name} : 异常 {ex.Message}");
                    failCount++;
                    int delay = Math.Min(500, 100 + failCount * 100 + _random.Next(0, 50));
                    await Task.Delay(delay);
                }
            }
        }

        public async Task<(bool isSuccess, string? msg)> EnqueueSelectCourseAsync(Course course)
        {
            var tcs = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _taskQueue.Enqueue((course, tcs));
            return await tcs.Task;
        }

        public async Task StartSelectClassAsync()
        {
            var list = await GetFavoriteCoursesAsync();

            StartWorkerPool();

            int total = list.Count;
            int completed = 0;

            var tasks = list.Select(async course =>
            {
                var (isSuccess, msg) = await EnqueueSelectCourseAsync(course);
                Interlocked.Increment(ref completed);
                WeakReferenceMessenger.Default.Send(new SelectCourseFinishedMessage(completed, total));
                return new { course.Name, isSuccess, msg };
            }).ToList();

            var results = await Task.WhenAll(tasks);

            Logger.WriteLine("选课完成!");

            foreach (var r in results.Where(r => !r.isSuccess))
                Logger.WriteLine($"课程失败: {r.Name}, 原因: {r.msg}");

            Logger.WriteLine($"成功数: {results.Count(r => r.isSuccess)}");

            _cts.Cancel();
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
