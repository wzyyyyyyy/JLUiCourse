using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.Models;
using iCourse.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace iCourse.Helpers;

public class JLUiCourseApi(
    Logger logger,
    UserCredentials credentials,
    IDialogService dialogs,
    IAppLifetime lifetime,
    CourseSelectionEngine selectionEngine,
    IMessenger messenger) : IJLUiCourseApi
{
    private Http client = null!;
    private string username = string.Empty;
    private string password = string.Empty;
    private string uuid = string.Empty;
    private string token = string.Empty;
    private BatchInfo batch = null!;
    private readonly CourseSelectionRunGuard runGuard = new();

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
            messenger.Send(new SetBatchFinishedMessage(batch));
        }
        else
        {
            var message = json["msg"]?.ToString() ?? "服务器未返回原因";
            logger.WriteLine(message);
            messenger.Send(new SystemBannerMessage(
                $"选课批次设置失败：{message}",
                SystemBannerSeverity.Error));
        }

        client.SetReferer("https://icourses.jlu.edu.cn/xsxk/profile/index.html");
        await client.HttpGetAsync("xsxk/elective/grablessons?batchId=" + batch.batchId);
        KeepOnline();
    }

    public async Task StartSelectClassAsync()
    {
        if (!runGuard.TryBegin(out var runToken))
        {
            messenger.Send(new SystemBannerMessage(
                "选课任务正在进行中",
                SystemBannerSeverity.Warning));
            return;
        }

        var wasCancelled = false;
        try
        {
            var courses = await GetFavoriteCoursesAsync(runToken);
            runToken.ThrowIfCancellationRequested();

            if (courses.Count == 0)
            {
                messenger.Send(new SystemBannerMessage(
                    "收藏中没有可选课程",
                    SystemBannerSeverity.Warning));
                return;
            }

            messenger.Send(new CourseSelectionRunStartedMessage(
                courses.Select(CourseSelectionSnapshot.Waiting).ToList()));

            var transport = new CourseSelectionHttpTransport(client, batch.batchId);
            var results = await selectionEngine.RunAsync(
                courses,
                transport,
                snapshot => messenger.Send(
                    new CourseSelectionStatusChangedMessage(snapshot)),
                runToken);

            wasCancelled = runToken.IsCancellationRequested;
            LogSelectionResults(results, wasCancelled);
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            wasCancelled = true;
            logger.WriteLine("选课任务已取消");
        }
        catch (FavoriteCoursesException exception)
        {
            logger.WriteLine($"读取收藏失败: {exception}");
            messenger.Send(new SystemBannerMessage(
                $"读取收藏失败：{exception.Message}",
                SystemBannerSeverity.Error));
        }
        catch (Exception exception)
        {
            logger.WriteLine($"选课任务异常: {exception}");
            messenger.Send(new SystemBannerMessage(
                "选课任务发生异常，请查看日志",
                SystemBannerSeverity.Error));
        }
        finally
        {
            wasCancelled |= runToken.IsCancellationRequested;
            runGuard.Complete();
            messenger.Send(new CourseSelectionRunCompletedMessage(wasCancelled));
        }
    }

    public void StopSelectClass() => runGuard.Cancel();

    private void LogSelectionResults(
        IReadOnlyList<CourseSelectionSnapshot> results,
        bool wasCancelled)
    {
        foreach (var result in results)
        {
            logger.WriteLine(
                $"课程结果: {result.CourseName}, {result.LatestResult}, 尝试 {result.AttemptCount} 次");
        }

        logger.WriteLine(
            $"选课{(wasCancelled ? "已取消" : "完成")}：" +
            $"成功 {results.Count(result => result.State == CourseSelectionState.Succeeded)}，" +
            $"失败 {results.Count(result => result.State == CourseSelectionState.Failed)}，" +
            $"取消 {results.Count(result => result.State == CourseSelectionState.Cancelled)}");
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
            messenger.Send(new SystemBannerMessage(
                "验证码错误，请重新输入",
                SystemBannerSeverity.Warning));
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

            messenger.Send(new LoginSuccessMessage());

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
        messenger.Send(new SystemBannerMessage(
            string.IsNullOrWhiteSpace(msg) ? "登录失败" : $"登录失败：{msg}",
            SystemBannerSeverity.Error));
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

    private async Task<List<Course>> GetFavoriteCoursesAsync(CancellationToken token)
    {
        var coursesList = new List<Course>();

        client.SetReferer("https://icourses.jlu.edu.cn/xsxk/elective/grablessons?batchId=" + batch.batchId);
        var response = await client.HttpPostAsync("xsxk/sc/clazz/list", null, token);
        var json = JObject.Parse(response);

        if (json["code"]?.ToObject<int>() != 200)
        {
            var message = json["msg"]?.ToString() ?? "服务器未返回原因";
            throw new FavoriteCoursesException(message);
        }

        var courses = json["data"];
        if (courses is not null)
        {
            foreach (var course in courses)
            {
                coursesList.Add(new Course(course));
            }
        }

        logger.WriteLine("收藏中的课程:\n" + string.Join("\n", coursesList.Select(c => c.Name)));
        return coursesList;
    }

    private sealed class FavoriteCoursesException(string message) : Exception(message);

    private void KeepOnline()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var response = await client.HttpPostAsync("xsxk/sc/clazz/list", null);
                if (response.StartsWith('<'))
                {
                    messenger.Send(new SystemBannerMessage(
                        "登录已失效，应用即将重启",
                        SystemBannerSeverity.Warning));
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
