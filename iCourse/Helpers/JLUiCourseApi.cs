using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.Models;
using iCourse.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace iCourse.Helpers;

public class JLUiCourseApi(
    Logger logger,
    UserCredentials credentials,
    IDialogService dialogs,
    IAppLifetime lifetime) : IJLUiCourseApi
{
    private Http client = null!;
    private string username = string.Empty;
    private string password = string.Empty;
    private string uuid = string.Empty;
    private string token = string.Empty;
    private BatchInfo batch = null!;
    private CancellationTokenSource cts = new();

    private static readonly Random Random = new();
    private readonly ConcurrentQueue<(Course course, TaskCompletionSource<(bool, string?)> tcs)> taskQueue = new();
    private readonly int workerCount = Environment.ProcessorCount * 2;

    private async Task<string> FetchCaptchaAsync()
    {
        client.SetOrigin("https://icourses.jlu.edu.cn");
        client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");

        const string captchaEndpoint = "xsxk/auth/captcha";

        var result = await client.HttpPostAsync(captchaEndpoint, null);
        var json = JObject.Parse(result);
        uuid = json["data"]?["uuid"]?.ToString() ?? string.Empty;
        var captchaImage = json["data"]?["captcha"]?.ToString() ?? string.Empty;
        return captchaImage[(captchaImage.IndexOf(',') + 1)..];
    }

    public async Task LoginAsync(string username_, string password_)
    {
        client = new Http(TimeSpan.FromSeconds(5), logger);
        username = username_;
        password = password_;

        var captchaImage = await FetchCaptchaAsync();
        var captcha = await dialogs.ShowCaptchaAsync(captchaImage);
        if (!string.IsNullOrWhiteSpace(captcha))
        {
            await AttemptLoginAsync(captcha);
        }
    }

    public async Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize)
    {
        var content = new StringContent(
            $"{{\"teachingClassType\":\"ALLKC\",\"pageNumber\":{index},\"pageSize\":{pageMaxSize},\"orderBy\":\"\",\"teachingClassType\": \"XGKC\"}}",
            Encoding.UTF8, "application/json");
        var response = await client.HttpPostAsync("xsxk/elective/jlu/clazz/list", content);
        return ParseCourseListResponse(response);
    }

    public async Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize, string key)
    {
        var content = new StringContent(
            $"{{\"teachingClassType\":\"ALLKC\",\"pageNumber\":{index},\"pageSize\":{pageMaxSize},\"orderBy\":\"\",\"teachingClassType\": \"XGKC\",\"KEY\":\"{key}\"}}",
            Encoding.UTF8, "application/json");
        var response = await client.HttpPostAsync("xsxk/elective/jlu/clazz/list", content);
        return ParseCourseListResponse(response);
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
            logger.WriteLine("添加失败!");
            return;
        }

        var json = JObject.Parse(response);
        if (json["code"]?.ToObject<int>() == 200)
        {
            logger.WriteLine($"已添加课程{course.Name}到收藏");
        }
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
        if (json["code"]?.ToObject<int>() == 200)
        {
            logger.WriteLine("选课批次设置成功");
            logger.WriteLine("已选批次:" + batch.batchName);
            WeakReferenceMessenger.Default.Send(new SetBatchFinishedMessage(batch));
        }
        else
        {
            logger.WriteLine(json["msg"]?.ToString());
        }

        client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");
        await client.HttpGetAsync("xsxk/elective/grablessons?batchId=" + batch.batchId);
        KeepOnline();
    }

    public async Task StartSelectClassAsync()
    {
        cts = new CancellationTokenSource();
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

        logger.WriteLine("选课完成!");

        foreach (var r in results.Where(r => !r.isSuccess))
        {
            logger.WriteLine($"课程失败: {r.Name}, 原因: {r.msg}");
        }

        logger.WriteLine($"成功数: {results.Count(r => r.isSuccess)}");

        cts.Cancel();
    }

    private List<Course> ParseCourseListResponse(string response)
    {
        if (response.StartsWith('<'))
        {
            logger.WriteLine("查询失败!");
            return [];
        }

        var json = JObject.Parse(response);

        if (json["code"]?.ToObject<int>() != 200)
        {
            logger.WriteLine("查询失败!");
            return [];
        }

        var courses = json["data"]?["rows"]?.ToList() ?? [];
        return courses.Select(course => new Course(course)).ToList();
    }

    private async Task AttemptLoginAsync(string captcha)
    {
        var response = await PostLoginAsync(captcha);
        var json = JObject.Parse(response);

        var code = json["code"]?.ToObject<int>() ?? 0;
        var msg = json["msg"]?.ToString() ?? string.Empty;

        if (msg == "验证码错误")
        {
            logger.WriteLine(msg);
            await LoginAsync(username, password);
            return;
        }

        if (code == 200 && json.ContainsKey("data"))
        {
            logger.WriteLine(msg);

            token = json["data"]?["token"]?.ToString() ?? string.Empty;

            var studentName = json["data"]?["student"]?["XM"]?.ToString() ?? string.Empty;
            var studentId = json["data"]?["student"]?["XH"]?.ToString() ?? string.Empty;
            var collage = json["data"]?["student"]?["YXMC"]?.ToString() ?? string.Empty;

            logger.WriteLine($"姓名：{studentName}");
            logger.WriteLine($"学号：{studentId}");
            logger.WriteLine($"学院：{collage}");

            WeakReferenceMessenger.Default.Send(new LoginSuccessMessage());

            var batchInfos = GetBatchInfo(json);
            if (credentials.AutoSelectBatch && !string.IsNullOrEmpty(credentials.LastBatchId))
            {
                var matchedBatch = batchInfos.FirstOrDefault(batchInfo => batchInfo.batchId == credentials.LastBatchId);
                if (matchedBatch is not null)
                {
                    await SetBatchIdAsync(matchedBatch);
                    return;
                }
            }

            var selectedBatch = await dialogs.SelectBatchAsync(batchInfos);
            if (selectedBatch is not null)
            {
                await SetBatchIdAsync(selectedBatch);
            }

            return;
        }

        logger.WriteLine($"错误:{code}, {msg}");
    }

    private async Task<string> PostLoginAsync(string captcha)
    {
        var encryptedPassword = await new EncryptHelper(client).EncryptWithAesAsync(password);
        return await client.HttpPostAsync("xsxk/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            {"loginname", username},
            {"password", encryptedPassword},
            {"captcha", captcha},
            {"uuid", uuid}
        }));
    }

    private static List<BatchInfo> GetBatchInfo(JObject loginResponse)
    {
        var batchInfos = new List<BatchInfo>();
        loginResponse["data"]?["student"]?["electiveBatchList"]?.ToList().ForEach(batch =>
        {
            var batchInfo = new BatchInfo
            {
                batchId = batch["code"]?.ToString() ?? string.Empty,
                batchName = batch["name"]?.ToString() ?? string.Empty,
                beginTime = batch["beginTime"]?.ToString() ?? string.Empty,
                endTime = batch["endTime"]?.ToString() ?? string.Empty,
                tacticName = batch["tacticName"]?.ToString() ?? string.Empty,
                noSelectReason = batch["noSelectReason"]?.ToString() ?? string.Empty,
                typeName = batch["typeName"]?.ToString() ?? string.Empty,
                canSelect = batch["canSelect"]?.ToString() != "0"
            };
            batchInfos.Add(batchInfo);
        });
        return batchInfos;
    }

    private async Task<List<Course>> GetFavoriteCoursesAsync()
    {
        var coursesList = new List<Course>();

        client.SetReferer("https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId=" + batch.batchId);
        var response = await client.HttpPostAsync("xsxk/sc/clazz/list", null);
        var json = JObject.Parse(response);

        if (json["code"]?.ToObject<int>() == 200)
        {
            var courses = json["data"];
            if (courses is not null)
            {
                foreach (var course in courses)
                {
                    coursesList.Add(new Course(course));
                }
            }
        }
        else
        {
            logger.WriteLine(json["msg"]?.ToString());
        }

        logger.WriteLine("收藏中的课程:\n" + string.Join("\n", coursesList.Select(c => c.Name)));
        return coursesList;
    }

    private string BuildRequestBody(Course course)
    {
        return $"clazzId={Uri.EscapeDataString(course.CourseId)}&secretVal={Uri.EscapeDataString(course.SecretVal)}&clazzType={Uri.EscapeDataString(course.SelectType.ToString())}";
    }

    private void StartWorkerPool()
    {
        for (int i = 0; i < workerCount; i++)
        {
            Task.Factory.StartNew(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    if (taskQueue.TryDequeue(out var workItem))
                    {
                        var (course, tcs) = workItem;
                        var result = await TrySelectCourseWithBackoffAsync(course);
                        tcs.TrySetResult(result);
                    }
                    else
                    {
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

        while (true)
        {
            try
            {
                var requestBody = BuildRequestBody(course);
                using var content = new StringContent(requestBody, Encoding.UTF8, "application/x-www-form-urlencoded");
                var response = await client.PostAsync("xsxk/sc/clazz/addxk", content);
                var respStr = await response.Content.ReadAsStringAsync();

                var json = JObject.Parse(respStr);
                var code = json["code"]?.ToObject<int>() ?? 0;
                var msg = json["msg"]?.ToString() ?? string.Empty;

                if (code == 200)
                {
                    logger.WriteLine($"成功选课: {course.Name}");
                    return (true, null);
                }

                logger.WriteLine($"{course.Name} : {msg}");

                if (msg == "该课程已在选课结果中" || msg == "课容量已满")
                {
                    return msg == "课容量已满" ? (false, msg) : (true, null);
                }

                failCount++;
                int delay = Math.Min(500, 100 + failCount * 100 + Random.Next(0, 50));
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                logger.WriteLine($"{course.Name} : 异常 {ex.Message}");
                failCount++;
                int delay = Math.Min(500, 100 + failCount * 100 + Random.Next(0, 50));
                await Task.Delay(delay);
            }
        }
    }

    private async Task<(bool isSuccess, string? msg)> EnqueueSelectCourseAsync(Course course)
    {
        var tcs = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        taskQueue.Enqueue((course, tcs));
        return await tcs.Task;
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
                    await dialogs.ShowMessageAsync("掉线提醒", "检测到掉线，将在1秒后重启本软件！");
                    await Task.Delay(1000);
                    lifetime.Restart();
                    return;
                }

                await Task.Delay(1500);
            }
        });
    }
}
