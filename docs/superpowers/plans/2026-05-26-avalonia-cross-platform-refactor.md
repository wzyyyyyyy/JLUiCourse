# Avalonia Cross-Platform Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the current WPF-only `iCourse` desktop app as an Avalonia app that builds and publishes for Windows, Linux, and macOS.

**Architecture:** Keep the existing solution and project name, but convert `iCourse` from WPF to Avalonia. Move all UI-host responsibilities behind small services so business code no longer references `System.Windows`, WPF dispatcher, WPF image types, or HandyControl behaviors.

**Tech Stack:** .NET 8, Avalonia 12.0.3, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, Newtonsoft.Json, Polly, Serilog, xUnit, Avalonia.Headless.

---

## Current State

- `iCourse/iCourse.csproj` targets `net8.0-windows`, enables `<UseWPF>true</UseWPF>`, and references `HandyControl`.
- `iCourse/App.xaml` uses WPF application namespaces and HandyControl theme dictionaries.
- `iCourse/Views/*.xaml` are WPF windows/user controls.
- `iCourse/ViewModels/*.cs`, `iCourse/Helpers/JLUiCourseApi.cs`, `iCourse/Helpers/Logger.cs`, and `iCourse/Helpers/AutoScrollBehavior.cs` reference WPF APIs directly.
- `iSetup/iSetup.vdproj` is Windows-only packaging and cannot be part of cross-platform delivery.
- `.github/workflows/*.yml` currently build on `windows-latest` only.

## File Structure

- Modify `iCourse/iCourse.csproj`: convert to Avalonia desktop packages and remove WPF/HandyControl/Costura.
- Create `iCourse/Program.cs`: Avalonia desktop entry point.
- Modify `iCourse/App.xaml` and `iCourse/App.xaml.cs`: Avalonia startup, service registration, theme, exception logging.
- Create `iCourse/Services/IAppLifetime.cs`, `iCourse/Services/AppLifetime.cs`: cross-platform shutdown/restart abstraction.
- Create `iCourse/Services/IUiDispatcher.cs`, `iCourse/Services/AvaloniaUiDispatcher.cs`: UI-thread dispatch abstraction.
- Create `iCourse/Services/IDialogService.cs`, `iCourse/Services/DialogService.cs`: show dialogs and notifications from view models/services without WPF.
- Create `iCourse/Services/IAppPaths.cs`, `iCourse/Services/AppPaths.cs`: cross-platform config/log path handling.
- Create `iCourse/Services/IImageDecoder.cs`, `iCourse/Services/AvaloniaImageDecoder.cs`: base64 captcha image conversion.
- Create `iCourse/Services/DesignTimeServices.cs`: safe design-time service provider for XAML previews.
- Modify `iCourse/Helpers/Logger.cs`: use `IUiDispatcher` and `IAppPaths`.
- Modify `iCourse/Helpers/JLUiCourseApi.cs`: remove MessageBox/Application references and use `IDialogService`/`IAppLifetime`.
- Modify `iCourse/Models/UserCredentials.cs`: store credentials in app data instead of the working directory.
- Modify all view models: replace `Visibility` with booleans, remove direct `App.ServiceProvider` usage where practical, and use services for dialog/lifetime/image logic.
- Delete `iCourse/Helpers/AutoScrollBehavior.cs`: replace HandyControl behavior with Avalonia attached behavior.
- Create `iCourse/Behaviors/AutoScrollToEndBehavior.cs`: Avalonia auto-scroll behavior for logs.
- Modify all `iCourse/Views/*.xaml` and `*.xaml.cs`: Avalonia syntax and constructors.
- Modify `iCourse.sln`: include new test project.
- Create `iCourse.Tests/iCourse.Tests.csproj`: unit/headless test project.
- Create `iCourse.Tests/Services/*Tests.cs`: tests for paths, credentials, logger, and image decoding.
- Create `iCourse.Tests/ViewModels/*Tests.cs`: tests for UI-state transitions.
- Modify `.github/workflows/dotnet.yml`: build and publish on `windows-latest`, `ubuntu-latest`, and `macos-latest`.
- Modify `.github/workflows/build.yml`: either remove duplicate WPF workflow or make it call the same cross-platform commands.
- Modify `README.md`: replace mojibake text with UTF-8 Chinese instructions and cross-platform run/publish commands.

## External References Checked

- Avalonia official docs currently recommend installing templates with `dotnet new install Avalonia.Templates` and creating MVVM apps with `dotnet new avalonia.mvvm`.
- NuGet currently lists `Avalonia` and `Avalonia.Desktop` version `12.0.3`.

### Task 1: Convert Project File To Avalonia

**Files:**
- Modify: `iCourse/iCourse.csproj`
- Create: `iCourse/Program.cs`
- Modify: `iCourse.sln`

- [x] **Step 1: Add a test project before changing UI framework**

Run:

```powershell
dotnet new xunit -n iCourse.Tests
dotnet sln iCourse.sln add iCourse.Tests/iCourse.Tests.csproj
dotnet add iCourse.Tests/iCourse.Tests.csproj reference iCourse/iCourse.csproj
```

Expected: `iCourse.Tests` is added to `iCourse.sln`.

- [x] **Step 2: Replace the application project file**

Write `iCourse/iCourse.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.0.3" />
    <PackageReference Include="Avalonia.Desktop" Version="12.0.3" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.0.3" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Polly" Version="8.4.1" />
    <PackageReference Include="Serilog" Version="4.0.1" />
    <PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
  </ItemGroup>
</Project>
```

- [x] **Step 3: Add the Avalonia entry point**

Write `iCourse/Program.cs`:

```csharp
using Avalonia;
using System;

namespace iCourse;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
```

- [x] **Step 4: Run restore and confirm WPF references are gone**

Run:

```powershell
dotnet restore iCourse.sln
rg "System.Windows|UseWPF|HandyControl" iCourse
```

Expected: restore succeeds; `rg` still finds source references that later tasks remove, but no `UseWPF` or `HandyControl` package remains in `iCourse/iCourse.csproj`.

- [x] **Step 5: Commit**

```powershell
git add iCourse/iCourse.csproj iCourse/Program.cs iCourse.sln iCourse.Tests/iCourse.Tests.csproj
git commit -m "chore: convert project shell to Avalonia"
```

### Task 2: Add Cross-Platform Host Services

**Files:**
- Create: `iCourse/Services/IAppPaths.cs`
- Create: `iCourse/Services/AppPaths.cs`
- Create: `iCourse/Services/IUiDispatcher.cs`
- Create: `iCourse/Services/AvaloniaUiDispatcher.cs`
- Create: `iCourse/Services/IAppLifetime.cs`
- Create: `iCourse/Services/AppLifetime.cs`
- Create: `iCourse/Services/IImageDecoder.cs`
- Create: `iCourse/Services/AvaloniaImageDecoder.cs`
- Create: `iCourse/Services/IDialogService.cs`
- Create: `iCourse/Services/DialogService.cs`
- Create: `iCourse/Services/DesignTimeServices.cs`

- [x] **Step 1: Add app path service**

Write `iCourse/Services/IAppPaths.cs`:

```csharp
namespace iCourse.Services;

public interface IAppPaths
{
    string DataDirectory { get; }
    string LogDirectory { get; }
    string CredentialsFilePath { get; }
    string NoShowDisclaimerFilePath { get; }
}
```

Write `iCourse/Services/AppPaths.cs`:

```csharp
using System;
using System.IO;

namespace iCourse.Services;

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DataDirectory = Path.Combine(appData, "JLUiCourse");
        LogDirectory = Path.Combine(DataDirectory, "Logs");
        CredentialsFilePath = Path.Combine(DataDirectory, "credentials.json");
        NoShowDisclaimerFilePath = Path.Combine(DataDirectory, ".noshow");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string DataDirectory { get; }
    public string LogDirectory { get; }
    public string CredentialsFilePath { get; }
    public string NoShowDisclaimerFilePath { get; }
}
```

- [x] **Step 2: Add UI dispatcher service**

Write `iCourse/Services/IUiDispatcher.cs`:

```csharp
using System;

namespace iCourse.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}
```

Write `iCourse/Services/AvaloniaUiDispatcher.cs`:

```csharp
using Avalonia.Threading;
using System;

namespace iCourse.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}
```

- [x] **Step 3: Add lifetime service**

Write `iCourse/Services/IAppLifetime.cs`:

```csharp
namespace iCourse.Services;

public interface IAppLifetime
{
    void Shutdown();
    void Restart();
}
```

Write `iCourse/Services/AppLifetime.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.Diagnostics;

namespace iCourse.Services;

public sealed class AppLifetime : IAppLifetime
{
    public void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    public void Restart()
    {
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }

        Shutdown();
    }
}
```

- [x] **Step 4: Add captcha image decoder**

Write `iCourse/Services/IImageDecoder.cs`:

```csharp
using Avalonia.Media.Imaging;

namespace iCourse.Services;

public interface IImageDecoder
{
    Bitmap DecodeBase64Bitmap(string base64Image);
}
```

Write `iCourse/Services/AvaloniaImageDecoder.cs`:

```csharp
using Avalonia.Media.Imaging;
using System;
using System.IO;

namespace iCourse.Services;

public sealed class AvaloniaImageDecoder : IImageDecoder
{
    public Bitmap DecodeBase64Bitmap(string base64Image)
    {
        var imageBytes = Convert.FromBase64String(base64Image);
        return new Bitmap(new MemoryStream(imageBytes));
    }
}
```

- [x] **Step 5: Add dialog service**

Write `iCourse/Services/IDialogService.cs`:

```csharp
using iCourse.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace iCourse.Services;

public interface IDialogService
{
    Task ShowMessageAsync(string title, string message);
    Task<string?> ShowCaptchaAsync(string base64Image);
    Task<BatchInfo?> SelectBatchAsync(IReadOnlyList<BatchInfo> batches);
    Task ShowQueryCoursesAsync();
}
```

Write `iCourse/Services/DialogService.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using iCourse.Models;
using iCourse.ViewModels;
using iCourse.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace iCourse.Services;

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    private Window? Owner
    {
        get
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }

            return null;
        }
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var window = new MessageWindow
        {
            DataContext = new MessageWindowViewModel(title, message)
        };

        if (Owner is null)
        {
            window.Show();
            return;
        }

        await window.ShowDialog(Owner);
    }

    public async Task<string?> ShowCaptchaAsync(string base64Image)
    {
        var viewModel = ActivatorUtilities.CreateInstance<CaptchaWindowViewModel>(services, base64Image);
        var window = new CaptchaWindow { DataContext = viewModel };
        return Owner is null ? null : await window.ShowDialog<string?>(Owner);
    }

    public async Task<BatchInfo?> SelectBatchAsync(IReadOnlyList<BatchInfo> batches)
    {
        var viewModel = ActivatorUtilities.CreateInstance<SelectBatchViewModel>(services, batches);
        var window = new SelectBatchWindow { DataContext = viewModel };
        return Owner is null ? null : await window.ShowDialog<BatchInfo?>(Owner);
    }

    public async Task ShowQueryCoursesAsync()
    {
        var viewModel = ActivatorUtilities.CreateInstance<QueryCourseWindowViewModel>(services);
        var window = new QueryCourseWindow { DataContext = viewModel };
        if (Owner is not null)
        {
            await window.ShowDialog(Owner);
        }
    }
}
```

- [x] **Step 6: Add design-time service provider**

Write `iCourse/Services/DesignTimeServices.cs`:

```csharp
using iCourse.Helpers;
using iCourse.Models;
using Microsoft.Extensions.DependencyInjection;

namespace iCourse.Services;

public static class DesignTimeServices
{
    public static IServiceProvider Create()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IAppLifetime, AppLifetime>();
        services.AddSingleton<IImageDecoder, AvaloniaImageDecoder>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<Logger>();
        services.AddSingleton<UserCredentials>();
        services.AddSingleton<JLUiCourseApi>();
        return services.BuildServiceProvider();
    }
}
```

- [x] **Step 7: Commit**

```powershell
git add iCourse/Services
git commit -m "feat: add Avalonia platform services"
```

### Task 3: Convert App Startup And Dependency Injection

**Files:**
- Modify: `iCourse/App.xaml`
- Modify: `iCourse/App.xaml.cs`
- Create: `iCourse/Views/MessageWindow.axaml`
- Create: `iCourse/Views/MessageWindow.axaml.cs`
- Create: `iCourse/ViewModels/MessageWindowViewModel.cs`

- [x] **Step 1: Replace WPF application XAML**

Write `iCourse/App.xaml`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="iCourse.App"
             RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

- [x] **Step 2: Replace app startup code**

Write `iCourse/App.xaml.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using iCourse.Helpers;
using iCourse.Models;
using iCourse.Services;
using iCourse.ViewModels;
using iCourse.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace iCourse;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = Services.DesignTimeServices.Create();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

        var credentials = ServiceProvider.GetRequiredService<UserCredentials>();
        credentials.Load();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.DataContext = ServiceProvider.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => ShutdownServices();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IAppLifetime, AppLifetime>();
        services.AddSingleton<IImageDecoder, AvaloniaImageDecoder>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<Logger>();
        services.AddSingleton<UserCredentials>();
        services.AddSingleton<JLUiCourseApi>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<QueryCourseWindowViewModel>();
        services.AddTransient<MainWindow>();
    }

    private static void ShutdownServices()
    {
        AppDomain.CurrentDomain.UnhandledException -= UnhandledExceptionHandler;
        ServiceProvider.GetService<Logger>()?.Dispose();
    }

    private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogException(exception);
        }
    }

    private static void LogException(Exception exception)
    {
        var paths = ServiceProvider.GetRequiredService<IAppPaths>();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var logFilePath = Path.Combine(paths.LogDirectory, $"error_{timestamp}.log");
        File.AppendAllText(logFilePath, $"[{DateTime.Now}] {exception}\n");
    }
}
```

- [x] **Step 3: Add reusable message window**

Write `iCourse/ViewModels/MessageWindowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace iCourse.ViewModels;

public partial class MessageWindowViewModel(string title, string message) : ObservableObject
{
    public string Title { get; } = title;
    public string Message { get; } = message;

    [RelayCommand]
    private void Close(Avalonia.Controls.Window window)
    {
        window.Close();
    }
}
```

Write `iCourse/Views/MessageWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="iCourse.Views.MessageWindow"
        Width="360"
        Height="180"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Title="{Binding Title}">
  <Grid RowDefinitions="*,Auto" Margin="20" Background="#1F1F1F">
    <TextBlock Text="{Binding Message}"
               Foreground="White"
               TextWrapping="Wrap" />
    <Button Grid.Row="1"
            Content="确定"
            Width="100"
            HorizontalAlignment="Center"
            Command="{Binding CloseCommand}"
            CommandParameter="{Binding $parent[Window]}" />
  </Grid>
</Window>
```

Write `iCourse/Views/MessageWindow.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace iCourse.Views;

public partial class MessageWindow : Window
{
    public MessageWindow()
    {
        InitializeComponent();
    }
}
```

- [x] **Step 4: Build to reveal remaining migration errors**

Run:

```powershell
dotnet build iCourse.sln
```

Expected: build fails because WPF views/view models still reference WPF namespaces. Record the errors before Task 4.

- [x] **Step 5: Commit**

```powershell
git add iCourse/App.xaml iCourse/App.xaml.cs iCourse/Views/MessageWindow.axaml iCourse/Views/MessageWindow.axaml.cs iCourse/ViewModels/MessageWindowViewModel.cs
git commit -m "feat: migrate app startup to Avalonia"
```

### Task 4: Migrate View Models Away From WPF

**Files:**
- Modify: `iCourse/ViewModels/MainWindowViewModel.cs`
- Modify: `iCourse/ViewModels/CaptchaWindowViewModel.cs`
- Modify: `iCourse/ViewModels/DisclaimerWindowViewModel.cs`
- Modify: `iCourse/ViewModels/SelectBatchWindowViewModel.cs`
- Modify: `iCourse/ViewModels/QueryCourseWindowViewModel.cs`
- Modify: `iCourse/Models/UserCredentials.cs`
- Modify: `iCourse/Helpers/Logger.cs`
- Modify: `iCourse/Helpers/JLUiCourseApi.cs`

- [x] **Step 1: Replace credentials storage with app paths**

Write `iCourse/Models/UserCredentials.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using iCourse.Services;
using Newtonsoft.Json;
using System.IO;
using System.Text;

namespace iCourse.Models;

public partial class UserCredentials(IAppPaths paths) : ObservableObject
{
    private string username = string.Empty;
    private string password = string.Empty;
    private string lastBatchId = string.Empty;
    private bool autoLogin;
    private bool autoSelectBatch;

    public string Username
    {
        get => username;
        set
        {
            if (SetProperty(ref username, value))
            {
                Save();
            }
        }
    }

    public string Password
    {
        get => password;
        set
        {
            if (SetProperty(ref password, value))
            {
                Save();
            }
        }
    }

    public string LastBatchId
    {
        get => lastBatchId;
        set
        {
            if (SetProperty(ref lastBatchId, value))
            {
                Save();
            }
        }
    }

    public bool AutoLogin
    {
        get => autoLogin;
        set
        {
            if (SetProperty(ref autoLogin, value))
            {
                Save();
            }
        }
    }

    public bool AutoSelectBatch
    {
        get => autoSelectBatch;
        set
        {
            if (SetProperty(ref autoSelectBatch, value))
            {
                Save();
            }
        }
    }

    private void Save()
    {
        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(paths.CredentialsFilePath, json, Encoding.UTF8);
    }

    public void Load()
    {
        if (!File.Exists(paths.CredentialsFilePath))
        {
            Save();
            return;
        }

        var context = File.ReadAllText(paths.CredentialsFilePath, Encoding.UTF8);
        var loaded = JsonConvert.DeserializeObject<UserCredentialsSnapshot>(context);
        if (loaded is null)
        {
            Save();
            return;
        }

        autoLogin = loaded.AutoLogin;
        username = loaded.Username ?? string.Empty;
        password = loaded.Password ?? string.Empty;
        lastBatchId = loaded.LastBatchId ?? string.Empty;
        autoSelectBatch = loaded.AutoSelectBatch;

        OnPropertyChanged(nameof(AutoLogin));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(Password));
        OnPropertyChanged(nameof(LastBatchId));
        OnPropertyChanged(nameof(AutoSelectBatch));
    }

    private sealed class UserCredentialsSnapshot
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? LastBatchId { get; set; }
        public bool AutoLogin { get; set; }
        public bool AutoSelectBatch { get; set; }
    }
}
```

- [x] **Step 2: Replace logger dispatcher**

Write `iCourse/Helpers/Logger.cs`:

```csharp
using iCourse.Services;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace iCourse.Helpers;

public sealed class Logger(IUiDispatcher dispatcher, IAppPaths paths) : IDisposable
{
    public ObservableCollection<string> LogMessages { get; } = new();

    private const int MaxLogEntries = 1145;

    public void Initialize()
    {
        var logPath = Path.Combine(paths.LogDirectory, "log.txt");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Minute)
            .CreateLogger();
    }

    public void WriteLine<T>(T msg)
    {
        var logMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{msg?.ToString()}";
        Log.Information(logMsg);

        dispatcher.Post(() =>
        {
            LogMessages.Add(logMsg);

            if (LogMessages.Count > MaxLogEntries)
            {
                LogMessages.RemoveAt(0);
            }
        });
    }

    public void Dispose()
    {
        Log.CloseAndFlush();
    }
}
```

Then add this line in `App.OnFrameworkInitializationCompleted()` immediately after `ServiceProvider = services.BuildServiceProvider();`:

```csharp
ServiceProvider.GetRequiredService<Logger>().Initialize();
```

- [x] **Step 3: Replace `MainWindowViewModel` visibility properties with booleans**

Write `iCourse/ViewModels/MainWindowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using iCourse.Helpers;
using iCourse.Messages;
using iCourse.Models;
using iCourse.Services;
using System.Collections.ObjectModel;

namespace iCourse.ViewModels;

public partial class MainWindowViewModel :
    ObservableRecipient,
    IRecipient<PropertyChangedMessage<string>>,
    IRecipient<PropertyChangedMessage<bool>>
{
    private readonly JLUiCourseApi api;
    private readonly UserCredentials credentials;
    private readonly IDialogService dialogs;

    [ObservableProperty]
    private bool canLogin = true;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private string username = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private string password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private bool autoLogin;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private bool autoSelectBatch;

    [ObservableProperty]
    private bool areAfterLoginButtonsVisible;

    public MainWindowViewModel(JLUiCourseApi api, UserCredentials credentials, Logger logger, IDialogService dialogs)
    {
        this.api = api;
        this.credentials = credentials;
        this.dialogs = dialogs;

        LogMessages = logger.LogMessages;

        AutoLogin = credentials.AutoLogin;
        AutoSelectBatch = credentials.AutoSelectBatch;

        WeakReferenceMessenger.Default.Register<LoginSuccessMessage>(this, LoginSuccess);
        WeakReferenceMessenger.Default.Register<SelectCourseFinishedMessage>(this, SelectCourseFinished);

        if (credentials.AutoLogin && !string.IsNullOrEmpty(credentials.Username) && !string.IsNullOrEmpty(credentials.Password))
        {
            Username = credentials.Username;
            Password = credentials.Password;
            Login();
        }
    }

    public ObservableCollection<string> LogMessages { get; }

    [RelayCommand]
    private async void Login()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            await dialogs.ShowMessageAsync("提示", "请输入账号或密码!");
            return;
        }

        _ = api.LoginAsync(Username, Password);
    }

    [RelayCommand]
    private void StartSelectCourse()
    {
        _ = api.StartSelectClassAsync();
    }

    [RelayCommand]
    private void QueryCourses()
    {
        _ = dialogs.ShowQueryCoursesAsync();
    }

    private void LoginSuccess(object recipient, LoginSuccessMessage message)
    {
        CanLogin = false;
        AreAfterLoginButtonsVisible = true;
    }

    private void SelectCourseFinished(object recipient, SelectCourseFinishedMessage message)
    {
        IsProgressVisible = true;
        ProgressValue = ((double)message.FinishedNum / message.Total) * 100;
    }

    public void Receive(PropertyChangedMessage<string> message)
    {
        if (message.PropertyName == nameof(Username))
        {
            credentials.Username = Username;
        }

        if (message.PropertyName == nameof(Password))
        {
            credentials.Password = Password;
        }
    }

    public void Receive(PropertyChangedMessage<bool> message)
    {
        if (message.PropertyName == nameof(AutoLogin))
        {
            credentials.AutoLogin = AutoLogin;
        }

        if (message.PropertyName == nameof(AutoSelectBatch))
        {
            credentials.AutoSelectBatch = AutoSelectBatch;
        }
    }
}
```

- [x] **Step 4: Replace captcha view model image type**

Write `iCourse/ViewModels/CaptchaWindowViewModel.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iCourse.Services;

namespace iCourse.ViewModels;

public partial class CaptchaWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string captcha = string.Empty;

    [ObservableProperty]
    private Bitmap imageSource;

    public CaptchaWindowViewModel(IImageDecoder imageDecoder, string base64Image)
    {
        ImageSource = imageDecoder.DecodeBase64Bitmap(base64Image);
    }

    [RelayCommand]
    private void CloseWindow(Window window)
    {
        window.Close(Captcha);
    }
}
```

- [x] **Step 5: Replace disclaimer view model lifetime and paths**

Write `iCourse/ViewModels/DisclaimerWindowViewModel.cs`:

```csharp
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iCourse.Services;
using System.IO;

namespace iCourse.ViewModels;

public partial class DisclaimerViewModel(IAppPaths paths, IAppLifetime lifetime) : ObservableObject
{
    [ObservableProperty]
    private bool isAgreed;

    [ObservableProperty]
    private bool noShowNextTime;

    [RelayCommand]
    private void Agree(Window window)
    {
        if (NoShowNextTime)
        {
            File.WriteAllText(paths.NoShowDisclaimerFilePath, "1");
        }

        window.Close(true);
    }

    [RelayCommand]
    private void Decline()
    {
        lifetime.Shutdown();
    }
}
```

- [x] **Step 6: Replace select batch view model dialog close**

Write `iCourse/ViewModels/SelectBatchWindowViewModel.cs`:

```csharp
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iCourse.Helpers;
using iCourse.Models;
using iCourse.Services;
using System.Collections.Generic;
using System.Linq;

namespace iCourse.ViewModels;

public partial class SelectBatchViewModel(UserCredentials credentials, JLUiCourseApi api, IDialogService dialogs, IReadOnlyList<BatchInfo> batchList) : ObservableObject
{
    [ObservableProperty]
    private IReadOnlyList<BatchInfo> batchList = batchList;

    [ObservableProperty]
    private BatchInfo? selectedBatch;

    [RelayCommand]
    private async void ConfirmSelection(Window window)
    {
        if (SelectedBatch is null)
        {
            await dialogs.ShowMessageAsync("提示", "请选择一个批次");
            return;
        }

        credentials.LastBatchId = SelectedBatch.batchId;
        _ = api.SetBatchIdAsync(SelectedBatch);
        window.Close(SelectedBatch);
    }
}
```

- [x] **Step 7: Convert query course view model to injected API**

Write `iCourse/ViewModels/QueryCourseWindowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iCourse.Helpers;
using iCourse.Models;
using System.Collections.Generic;

namespace iCourse.ViewModels;

public partial class QueryCourseWindowViewModel(JLUiCourseApi api) : ObservableObject
{
    [ObservableProperty]
    private List<Course> courses = [];

    [ObservableProperty]
    private string queryText = string.Empty;

    [ObservableProperty]
    private bool buttonEnabled;

    [ObservableProperty]
    private int currentPage = 1;

    public async Task InitializeAsync()
    {
        Courses = await api.QueryCoursesAsync(CurrentPage, 15);
        ButtonEnabled = true;
    }

    [RelayCommand]
    private async Task PreviousPage()
    {
        if (CurrentPage == 1)
        {
            return;
        }

        CurrentPage--;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task NextPage()
    {
        CurrentPage++;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task Query()
    {
        CurrentPage = 1;
        await LoadPageAsync();
    }

    [RelayCommand]
    private void AddToFavorites(Course course)
    {
        _ = api.AddToFavoritesAsync(course);
    }

    private async Task LoadPageAsync()
    {
        ButtonEnabled = false;
        Courses = string.IsNullOrWhiteSpace(QueryText)
            ? await api.QueryCoursesAsync(CurrentPage, 15)
            : await api.QueryCoursesAsync(CurrentPage, 15, QueryText);
        ButtonEnabled = true;
    }
}
```

Update `DialogService.ShowQueryCoursesAsync()` before `ShowDialog`:

```csharp
await viewModel.InitializeAsync();
```

- [x] **Step 8: Replace UI responsibilities in API**

In `iCourse/Helpers/JLUiCourseApi.cs`, remove `using System.Windows;`, add `using iCourse.Services;`, and change the class constructor and fields:

```csharp
public class JLUiCourseApi(Logger logger, UserCredentials credentials, IDialogService dialogs, IAppLifetime lifetime)
{
    private Http client = null!;
    private string username = string.Empty;
    private string password = string.Empty;
    private string uuid = string.Empty;
    private string token = string.Empty;
    private BatchInfo batch = null!;
```

Replace `LoginAsync` captcha flow:

```csharp
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
```

Replace the old messenger-based `AttemptLoginAsync` method with:

```csharp
private async Task AttemptLoginAsync(string captcha)
{
    var response = await PostLoginAsync(captcha);
    var json = JObject.Parse(response);

    var code = json["code"]!.ToObject<int>();
    var msg = json["msg"]!.ToString();

    if (msg == "验证码错误")
    {
        logger.WriteLine(msg);
        await LoginAsync(username, password);
        return;
    }

    if (code == 200 && json.ContainsKey("data"))
    {
        logger.WriteLine(msg);

        token = json["data"]!["token"]!.ToString();

        var studentName = json["data"]!["student"]!["XM"]!.ToString();
        var studentId = json["data"]!["student"]!["XH"]!.ToString();
        var collage = json["data"]!["student"]!["YXMC"]!.ToString();

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
```

Replace reconnect failure in `KeepOnline()`:

```csharp
await dialogs.ShowMessageAsync("掉线提醒", "检测到掉线，将在1秒后重启本软件！");
await Task.Delay(1000);
lifetime.Restart();
```

Replace every `Logger.WriteLine` with `logger.WriteLine`, every `App.ServiceProvider.GetService<UserCredentials>()` with `credentials`, and every `new Http(TimeSpan.FromSeconds(5))` with `new Http(TimeSpan.FromSeconds(5), logger)`.

- [x] **Step 9: Update HTTP helper constructor**

Modify `iCourse/Helpers/Http.cs`:

```csharp
class Http : HttpClient
{
    private readonly AsyncRetryPolicy retryPolicy;
    private readonly Logger logger;

    public Http(TimeSpan timeout, Logger logger)
        : base(new HttpClientHandler
        {
            UseCookies = true,
            ServerCertificateCustomValidationCallback = (HttpRequestMessage request, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true
        })
    {
        this.logger = logger;
```

Then replace `Logger.WriteLine(...)` with `logger.WriteLine(...)` in this file.

- [x] **Step 10: Build and commit**

Run:

```powershell
dotnet build iCourse.sln
```

Expected: build still fails because XAML views are not converted, but model/view model files no longer reference `System.Windows`.

Commit:

```powershell
git add iCourse/ViewModels iCourse/Models/UserCredentials.cs iCourse/Helpers/Logger.cs iCourse/Helpers/JLUiCourseApi.cs iCourse/Helpers/Http.cs iCourse/App.xaml.cs
git commit -m "refactor: remove WPF dependencies from view models"
```

### Task 5: Convert Views To Avalonia XAML

**Files:**
- Rename/modify: `iCourse/Views/MainWindow.xaml` to `iCourse/Views/MainWindow.axaml`
- Rename/modify: `iCourse/Views/LoginControl.xaml` to `iCourse/Views/LoginControl.axaml`
- Rename/modify: `iCourse/Views/CaptchaWindow.xaml` to `iCourse/Views/CaptchaWindow.axaml`
- Rename/modify: `iCourse/Views/DisclaimerWindow.xaml` to `iCourse/Views/DisclaimerWindow.axaml`
- Rename/modify: `iCourse/Views/SelectBatchWindow.xaml` to `iCourse/Views/SelectBatchWindow.axaml`
- Rename/modify: `iCourse/Views/QueryCourseWindow.xaml` to `iCourse/Views/QueryCourseWindow.axaml`
- Modify: matching `.xaml.cs` files to match Avalonia namespaces
- Delete: old `.xaml` files after `.axaml` replacements compile

- [x] **Step 1: Rename XAML files**

Run:

```powershell
Rename-Item iCourse/Views/MainWindow.xaml MainWindow.axaml
Rename-Item iCourse/Views/LoginControl.xaml LoginControl.axaml
Rename-Item iCourse/Views/CaptchaWindow.xaml CaptchaWindow.axaml
Rename-Item iCourse/Views/DisclaimerWindow.xaml DisclaimerWindow.axaml
Rename-Item iCourse/Views/SelectBatchWindow.xaml SelectBatchWindow.axaml
Rename-Item iCourse/Views/QueryCourseWindow.xaml QueryCourseWindow.axaml
```

- [x] **Step 2: Replace MainWindow**

Write `iCourse/Views/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:iCourse.Views"
        xmlns:behaviors="clr-namespace:iCourse.Behaviors"
        x:Class="iCourse.Views.MainWindow"
        Title="iCourse"
        Width="680"
        Height="420"
        MinWidth="620"
        MinHeight="380"
        WindowStartupLocation="CenterScreen"
        Background="#1F1F1F">
  <Grid ColumnDefinitions="220,*" RowDefinitions="*,Auto" Margin="16">
    <views:LoginControl Grid.RowSpan="2" />

    <Border Grid.Column="1"
            Background="#2E2E2E"
            Padding="10"
            Margin="16,0,0,12">
      <ScrollViewer behaviors:AutoScrollToEndBehavior.ItemsSource="{Binding LogMessages}">
        <ItemsControl ItemsSource="{Binding LogMessages}">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <TextBlock Text="{Binding}"
                         Foreground="White"
                         FontSize="14"
                         TextWrapping="Wrap"
                         Margin="0,0,0,6" />
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
    </Border>

    <ProgressBar Grid.Column="1"
                 Grid.Row="1"
                 Margin="16,0,0,0"
                 IsVisible="{Binding IsProgressVisible}"
                 Value="{Binding ProgressValue}" />
  </Grid>
</Window>
```

Write `iCourse/Views/MainWindow.xaml.cs`:

```csharp
using Avalonia.Controls;
using iCourse.Services;
using iCourse.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace iCourse.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await ShowDisclaimerWindowAsync();
    }

    private async Task ShowDisclaimerWindowAsync()
    {
        var paths = App.ServiceProvider.GetRequiredService<IAppPaths>();
        if (File.Exists(paths.NoShowDisclaimerFilePath))
        {
            return;
        }

        var viewModel = ActivatorUtilities.CreateInstance<DisclaimerViewModel>(App.ServiceProvider);
        var window = new DisclaimerWindow { DataContext = viewModel };
        var agreed = await window.ShowDialog<bool>(this);
        if (!agreed)
        {
            App.ServiceProvider.GetRequiredService<IAppLifetime>().Shutdown();
        }
    }
}
```

- [x] **Step 3: Replace LoginControl**

Write `iCourse/Views/LoginControl.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="iCourse.Views.LoginControl">
  <StackPanel Spacing="12" Width="180">
    <TextBlock Text="账号:" Foreground="White" FontSize="14" />
    <TextBox Text="{Binding Username, Mode=TwoWay}" />

    <TextBlock Text="密码:" Foreground="White" FontSize="14" />
    <TextBox Text="{Binding Password, Mode=TwoWay}" PasswordChar="*" />

    <CheckBox Content="自动选择批次"
              Foreground="White"
              IsChecked="{Binding AutoSelectBatch, Mode=TwoWay}" />
    <CheckBox Content="自动登录"
              Foreground="White"
              IsChecked="{Binding AutoLogin, Mode=TwoWay}" />

    <Button Content="登录"
            Height="32"
            IsVisible="{Binding CanLogin}"
            Command="{Binding LoginCommand}" />
    <Button Content="开始选课"
            Height="32"
            IsVisible="{Binding AreAfterLoginButtonsVisible}"
            Command="{Binding StartSelectCourseCommand}" />
    <Button Content="查询课程"
            Height="32"
            IsVisible="{Binding AreAfterLoginButtonsVisible}"
            Command="{Binding QueryCoursesCommand}" />
  </StackPanel>
</UserControl>
```

Write `iCourse/Views/LoginControl.xaml.cs`:

```csharp
using Avalonia.Controls;

namespace iCourse.Views;

public partial class LoginControl : UserControl
{
    public LoginControl()
    {
        InitializeComponent();
    }
}
```

- [x] **Step 4: Replace CaptchaWindow**

Write `iCourse/Views/CaptchaWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="iCourse.Views.CaptchaWindow"
        Title="验证码"
        Height="250"
        Width="260"
        WindowStartupLocation="CenterOwner"
        Background="#1F1F1F"
        Topmost="True">
  <Grid RowDefinitions="Auto,Auto,Auto" ColumnDefinitions="*,Auto" Margin="16">
    <TextBlock Text="请输入验证码："
               FontSize="14"
               Foreground="White"
               Grid.ColumnSpan="2" />
    <Image Grid.Row="1"
           Grid.ColumnSpan="2"
           Height="80"
           Margin="0,12"
           Source="{Binding ImageSource}" />
    <TextBox Grid.Row="2"
             Text="{Binding Captcha}"
             Width="145">
      <TextBox.KeyBindings>
        <KeyBinding Gesture="Enter"
                    Command="{Binding CloseWindowCommand}"
                    CommandParameter="{Binding $parent[Window]}" />
      </TextBox.KeyBindings>
    </TextBox>
    <Button Grid.Row="2"
            Grid.Column="1"
            Content="确定"
            Width="64"
            Margin="10,0,0,0"
            Command="{Binding CloseWindowCommand}"
            CommandParameter="{Binding $parent[Window]}" />
  </Grid>
</Window>
```

Write `iCourse/Views/CaptchaWindow.xaml.cs`:

```csharp
using Avalonia.Controls;

namespace iCourse.Views;

public partial class CaptchaWindow : Window
{
    public CaptchaWindow()
    {
        InitializeComponent();
    }
}
```

- [x] **Step 5: Replace DisclaimerWindow**

Write `iCourse/Views/DisclaimerWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="iCourse.Views.DisclaimerWindow"
        Title="免责协议"
        Height="335"
        Width="380"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Background="#1F1F1F">
  <Grid RowDefinitions="*,Auto,Auto,Auto" Margin="20">
    <TextBlock TextWrapping="Wrap"
               Foreground="White"
               Text="本软件完全免费，仅供学习和研究使用。请勿将其用于任何违反学校或相关法律法规的行为。用户需自行承担使用本软件所产生的后果，开发者不对因使用本软件造成的任何直接或间接损失负责。在使用本软件过程中，用户应遵守所在机构及国家的相关法律法规。如因使用本软件违反相关规定，责任由用户自行承担。本软件未经吉林大学官方授权，与吉林大学无任何直接或间接关联。" />
    <CheckBox Grid.Row="1"
              Content="我已阅读并同意"
              Foreground="White"
              IsChecked="{Binding IsAgreed}" />
    <CheckBox Grid.Row="2"
              Content="下次启动时不再显示"
              Foreground="White"
              IsChecked="{Binding NoShowNextTime}" />
    <StackPanel Grid.Row="3"
                Orientation="Horizontal"
                HorizontalAlignment="Center"
                Spacing="10">
      <Button Content="同意"
              Width="100"
              IsEnabled="{Binding IsAgreed}"
              Command="{Binding AgreeCommand}"
              CommandParameter="{Binding $parent[Window]}" />
      <Button Content="拒绝"
              Width="100"
              Command="{Binding DeclineCommand}" />
    </StackPanel>
  </Grid>
</Window>
```

Write `iCourse/Views/DisclaimerWindow.xaml.cs`:

```csharp
using Avalonia.Controls;

namespace iCourse.Views;

public partial class DisclaimerWindow : Window
{
    public DisclaimerWindow()
    {
        InitializeComponent();
    }
}
```

- [x] **Step 6: Replace SelectBatchWindow**

Write `iCourse/Views/SelectBatchWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="iCourse.Views.SelectBatchWindow"
        Title="选课批次"
        Height="465"
        Width="600"
        WindowStartupLocation="CenterOwner"
        Background="#2E2E2E">
  <Grid ColumnDefinitions="*,2*" Margin="10">
    <ListBox ItemsSource="{Binding BatchList}"
             SelectedItem="{Binding SelectedBatch}"
             Background="#454545">
      <ListBox.ItemTemplate>
        <DataTemplate>
          <TextBlock Text="{Binding batchName}" Foreground="White" />
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
    <StackPanel Grid.Column="1" Margin="20,10,0,10" Spacing="6">
      <TextBlock Text="轮次码:" FontWeight="Bold" Foreground="White"/>
      <TextBlock Text="{Binding SelectedBatch.batchId}" Foreground="White"/>
      <TextBlock Text="轮次名:" FontWeight="Bold" Foreground="White"/>
      <TextBlock Text="{Binding SelectedBatch.batchName}" Foreground="White"/>
      <TextBlock Text="开始时间:" FontWeight="Bold" Foreground="White"/>
      <TextBlock Text="{Binding SelectedBatch.beginTime}" Foreground="White"/>
      <TextBlock Text="结束时间:" FontWeight="Bold" Foreground="White"/>
      <TextBlock Text="{Binding SelectedBatch.endTime}" Foreground="White"/>
      <TextBlock Text="选课策略:" FontWeight="Bold" Foreground="White"/>
      <TextBlock Text="{Binding SelectedBatch.tacticName}" Foreground="White"/>
      <TextBlock Text="不可选原因:" FontWeight="Bold" Foreground="White"/>
      <TextBlock Text="{Binding SelectedBatch.noSelectReason}" Foreground="White"/>
      <TextBlock Text="选课类型:" FontWeight="Bold" Foreground="White"/>
      <TextBlock Text="{Binding SelectedBatch.typeName}" Foreground="White"/>
      <TextBlock Text="是否可选:" FontWeight="Bold" Foreground="White"/>
      <TextBlock Text="{Binding SelectedBatch.canSelect}" Foreground="White"/>
      <Button Content="确定"
              Width="100"
              HorizontalAlignment="Center"
              Command="{Binding ConfirmSelectionCommand}"
              CommandParameter="{Binding $parent[Window]}" />
    </StackPanel>
  </Grid>
</Window>
```

Write `iCourse/Views/SelectBatchWindow.xaml.cs`:

```csharp
using Avalonia.Controls;

namespace iCourse.Views;

public partial class SelectBatchWindow : Window
{
    public SelectBatchWindow()
    {
        InitializeComponent();
    }
}
```

- [x] **Step 7: Replace QueryCourseWindow**

Write `iCourse/Views/QueryCourseWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="iCourse.Views.QueryCourseWindow"
        Title="查询课程"
        Height="600"
        Width="835"
        WindowStartupLocation="CenterOwner"
        Background="#1F1F1F">
  <Grid RowDefinitions="Auto,*,Auto" ColumnDefinitions="Auto,*,Auto" Margin="10">
    <TextBlock Text="课程名:"
               VerticalAlignment="Center"
               Margin="0,0,10,0"
               Foreground="White" />
    <TextBox Grid.Column="1"
             Text="{Binding QueryText}"
             Foreground="White"
             Background="#252526" />
    <Button Grid.Column="2"
            Content="查询"
            Margin="10,0,0,0"
            Width="89"
            Command="{Binding QueryCommand}"
            IsEnabled="{Binding ButtonEnabled}" />

    <DataGrid Grid.Row="1"
              Grid.ColumnSpan="3"
              Margin="0,10"
              AutoGenerateColumns="False"
              ItemsSource="{Binding Courses}">
      <DataGrid.Columns>
        <DataGridTextColumn Header="课程名称" Binding="{Binding Name}" Width="150" IsReadOnly="True"/>
        <DataGridTextColumn Header="校区" Binding="{Binding Campus}" Width="100" IsReadOnly="True"/>
        <DataGridTextColumn Header="上课时间地点" Binding="{Binding ClassLocation}" Width="220" IsReadOnly="True"/>
        <DataGridTextColumn Header="课程性质" Binding="{Binding SelectType}" Width="100" IsReadOnly="True"/>
        <DataGridTextColumn Header="上课教师" Binding="{Binding TeacherName}" Width="150" IsReadOnly="True"/>
        <DataGridTemplateColumn Header="操作" Width="90">
          <DataGridTemplateColumn.CellTemplate>
            <DataTemplate>
              <Button Content="收藏"
                      Width="60"
                      Command="{Binding DataContext.AddToFavoritesCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                      CommandParameter="{Binding}" />
            </DataTemplate>
          </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>
      </DataGrid.Columns>
    </DataGrid>

    <StackPanel Grid.Row="2"
                Grid.ColumnSpan="3"
                Orientation="Horizontal"
                HorizontalAlignment="Center"
                Spacing="10">
      <Button Content="上一页"
              Width="80"
              Command="{Binding PreviousPageCommand}"
              IsEnabled="{Binding ButtonEnabled}" />
      <TextBlock Text="{Binding CurrentPage}"
                 Foreground="White"
                 VerticalAlignment="Center" />
      <Button Content="下一页"
              Width="80"
              Command="{Binding NextPageCommand}"
              IsEnabled="{Binding ButtonEnabled}" />
    </StackPanel>
  </Grid>
</Window>
```

Write `iCourse/Views/QueryCourseWindow.xaml.cs`:

```csharp
using Avalonia.Controls;

namespace iCourse.Views;

public partial class QueryCourseWindow : Window
{
    public QueryCourseWindow()
    {
        InitializeComponent();
    }
}
```

- [x] **Step 8: Build and commit**

Run:

```powershell
dotnet build iCourse.sln
```

Expected: remaining errors are limited to old helper behavior, messages, or XAML binding syntax issues.

Commit:

```powershell
git add iCourse/Views
git commit -m "feat: port windows to Avalonia"
```

### Task 6: Replace HandyControl Auto Scroll Behavior

**Files:**
- Delete: `iCourse/Helpers/AutoScrollBehavior.cs`
- Create: `iCourse/Behaviors/AutoScrollToEndBehavior.cs`

- [x] **Step 1: Delete WPF/HandyControl behavior**

Run:

```powershell
Remove-Item iCourse/Helpers/AutoScrollBehavior.cs
```

- [x] **Step 2: Add Avalonia attached behavior**

Write `iCourse/Behaviors/AutoScrollToEndBehavior.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using System.Collections.Specialized;

namespace iCourse.Behaviors;

public static class AutoScrollToEndBehavior
{
    public static readonly AttachedProperty<INotifyCollectionChanged?> ItemsSourceProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, INotifyCollectionChanged?>(
            "ItemsSource",
            typeof(AutoScrollToEndBehavior));

    static AutoScrollToEndBehavior()
    {
        ItemsSourceProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            if (args.OldValue.Value is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= OnCollectionChanged;
            }

            if (args.NewValue.Value is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += OnCollectionChanged;
            }

            void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            {
                scrollViewer.ScrollToEnd();
            }
        });
    }

    public static void SetItemsSource(AvaloniaObject element, INotifyCollectionChanged? value)
    {
        element.SetValue(ItemsSourceProperty, value);
    }

    public static INotifyCollectionChanged? GetItemsSource(AvaloniaObject element)
    {
        return element.GetValue(ItemsSourceProperty);
    }
}
```

- [x] **Step 3: Verify no HandyControl/WPF behavior references remain**

Run:

```powershell
rg "HandyControl|AutoScrollBehavior|System.Windows.Controls|System.Windows.Media" iCourse
```

Expected: no matches.

- [x] **Step 4: Commit**

```powershell
git add iCourse/Behaviors/AutoScrollToEndBehavior.cs iCourse/Helpers/AutoScrollBehavior.cs
git commit -m "refactor: replace WPF scroll behavior"
```

### Task 7: Add Focused Tests

**Files:**
- Modify: `iCourse.Tests/iCourse.Tests.csproj`
- Create: `iCourse.Tests/Fakes/FakeAppPaths.cs`
- Create: `iCourse.Tests/Fakes/ImmediateUiDispatcher.cs`
- Create: `iCourse.Tests/Models/UserCredentialsTests.cs`
- Create: `iCourse.Tests/Helpers/LoggerTests.cs`
- Create: `iCourse.Tests/ViewModels/MainWindowViewModelTests.cs`

- [x] **Step 1: Configure test project**

Write `iCourse.Tests/iCourse.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia.Headless" Version="12.0.3" />
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\iCourse\iCourse.csproj" />
  </ItemGroup>
</Project>
```

- [x] **Step 2: Add test fakes**

Write `iCourse.Tests/Fakes/FakeAppPaths.cs`:

```csharp
using iCourse.Services;

namespace iCourse.Tests.Fakes;

internal sealed class FakeAppPaths(string root) : IAppPaths
{
    public string DataDirectory { get; } = root;
    public string LogDirectory { get; } = Path.Combine(root, "Logs");
    public string CredentialsFilePath { get; } = Path.Combine(root, "credentials.json");
    public string NoShowDisclaimerFilePath { get; } = Path.Combine(root, ".noshow");
}
```

Write `iCourse.Tests/Fakes/ImmediateUiDispatcher.cs`:

```csharp
using iCourse.Services;

namespace iCourse.Tests.Fakes;

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        action();
    }
}
```

- [x] **Step 3: Test credential persistence**

Write `iCourse.Tests/Models/UserCredentialsTests.cs`:

```csharp
using iCourse.Models;
using iCourse.Tests.Fakes;

namespace iCourse.Tests.Models;

public sealed class UserCredentialsTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsAllFields()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var paths = new FakeAppPaths(root);
        var credentials = new UserCredentials(paths)
        {
            Username = "u",
            Password = "p",
            LastBatchId = "b",
            AutoLogin = true,
            AutoSelectBatch = true
        };

        var loaded = new UserCredentials(paths);
        loaded.Load();

        Assert.Equal("u", loaded.Username);
        Assert.Equal("p", loaded.Password);
        Assert.Equal("b", loaded.LastBatchId);
        Assert.True(loaded.AutoLogin);
        Assert.True(loaded.AutoSelectBatch);
    }
}
```

- [x] **Step 4: Test logger collection updates**

Write `iCourse.Tests/Helpers/LoggerTests.cs`:

```csharp
using iCourse.Helpers;
using iCourse.Tests.Fakes;

namespace iCourse.Tests.Helpers;

public sealed class LoggerTests
{
    [Fact]
    public void WriteLine_AddsTimestampedMessage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var logger = new Logger(new ImmediateUiDispatcher(), new FakeAppPaths(root));
        logger.Initialize();

        logger.WriteLine("hello");

        Assert.Single(logger.LogMessages);
        Assert.Contains("hello", logger.LogMessages[0]);
        Assert.StartsWith("[", logger.LogMessages[0]);
    }
}
```

- [x] **Step 5: Add view model state test**

Write `iCourse.Tests/ViewModels/MainWindowViewModelTests.cs`:

```csharp
namespace iCourse.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void InitialProgressState_IsHidden()
    {
        Assert.True(true);
    }
}
```

After `JLUiCourseApi` is split enough for easy faking, replace this smoke test with a constructor-level test using fake API/dialog services. Keep this temporary smoke test only until Task 8 completes.

- [x] **Step 6: Run tests**

Run:

```powershell
dotnet test iCourse.sln
```

Expected: tests pass.

- [x] **Step 7: Commit**

```powershell
git add iCourse.Tests
git commit -m "test: add cross-platform service tests"
```

### Task 8: Split API Side Effects For Testability

**Files:**
- Create: `iCourse/Services/IJLUiCourseApi.cs`
- Modify: `iCourse/Helpers/JLUiCourseApi.cs`
- Modify: `iCourse/ViewModels/MainWindowViewModel.cs`
- Modify: `iCourse/ViewModels/QueryCourseWindowViewModel.cs`
- Modify: `iCourse.Tests/ViewModels/MainWindowViewModelTests.cs`

- [x] **Step 1: Add API interface**

Write `iCourse/Services/IJLUiCourseApi.cs`:

```csharp
using iCourse.Models;

namespace iCourse.Services;

public interface IJLUiCourseApi
{
    Task LoginAsync(string username, string password);
    Task StartSelectClassAsync();
    Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize);
    Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize, string key);
    Task AddToFavoritesAsync(Course course);
    Task SetBatchIdAsync(BatchInfo batch);
}
```

- [x] **Step 2: Implement interface and register it**

Change the class declaration:

```csharp
public class JLUiCourseApi(Logger logger, UserCredentials credentials, IDialogService dialogs, IAppLifetime lifetime) : IJLUiCourseApi
```

Change DI registration in `App.xaml.cs`:

```csharp
services.AddSingleton<IJLUiCourseApi, JLUiCourseApi>();
services.AddSingleton<JLUiCourseApi>();
```

- [x] **Step 3: Use interface in view models**

Change constructor parameters:

```csharp
public MainWindowViewModel(IJLUiCourseApi api, UserCredentials credentials, Logger logger, IDialogService dialogs)
```

```csharp
public partial class QueryCourseWindowViewModel(IJLUiCourseApi api) : ObservableObject
```

- [x] **Step 4: Replace smoke test with real test**

Write `iCourse.Tests/ViewModels/MainWindowViewModelTests.cs`:

```csharp
using iCourse.Helpers;
using iCourse.Models;
using iCourse.Services;
using iCourse.Tests.Fakes;
using iCourse.ViewModels;

namespace iCourse.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void InitialProgressState_IsHidden()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var paths = new FakeAppPaths(root);
        var logger = new Logger(new ImmediateUiDispatcher(), paths);
        logger.Initialize();
        var credentials = new UserCredentials(paths);
        var viewModel = new MainWindowViewModel(new FakeApi(), credentials, logger, new FakeDialogService());

        Assert.False(viewModel.IsProgressVisible);
        Assert.True(viewModel.CanLogin);
        Assert.False(viewModel.AreAfterLoginButtonsVisible);
    }

    private sealed class FakeApi : IJLUiCourseApi
    {
        public Task AddToFavoritesAsync(Course course) => Task.CompletedTask;
        public Task LoginAsync(string username, string password) => Task.CompletedTask;
        public Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize) => Task.FromResult(new List<Course>());
        public Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize, string key) => Task.FromResult(new List<Course>());
        public Task SetBatchIdAsync(BatchInfo batch) => Task.CompletedTask;
        public Task StartSelectClassAsync() => Task.CompletedTask;
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task<BatchInfo?> SelectBatchAsync(IReadOnlyList<BatchInfo> batches) => Task.FromResult<BatchInfo?>(null);
        public Task<string?> ShowCaptchaAsync(string base64Image) => Task.FromResult<string?>(null);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task ShowQueryCoursesAsync() => Task.CompletedTask;
    }
}
```

- [x] **Step 5: Run tests and build**

Run:

```powershell
dotnet test iCourse.sln
dotnet build iCourse.sln -c Release
```

Expected: both pass.

- [x] **Step 6: Commit**

```powershell
git add iCourse/Services/IJLUiCourseApi.cs iCourse/Helpers/JLUiCourseApi.cs iCourse/ViewModels iCourse/App.xaml.cs iCourse.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "refactor: introduce testable course api boundary"
```

### Task 9: Cross-Platform CI And Publishing

**Files:**
- Modify: `.github/workflows/dotnet.yml`
- Modify: `.github/workflows/build.yml`
- Modify: `README.md`

- [x] **Step 1: Replace main CI workflow**

Write `.github/workflows/dotnet.yml`:

```yaml
name: Build

on:
  push:
    branches: [ "master" ]
  pull_request:
    branches: [ "master" ]

jobs:
  build:
    name: Build on ${{ matrix.os }}
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest, macos-latest]

    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore dependencies
        run: dotnet restore

      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test
        run: dotnet test --configuration Release --no-build

      - name: Publish
        run: dotnet publish iCourse/iCourse.csproj --configuration Release --output publish/${{ matrix.os }}

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: iCourse-${{ matrix.os }}
          path: publish/${{ matrix.os }}
```

- [x] **Step 2: Remove duplicate WPF workflow**

Write `.github/workflows/build.yml`:

```yaml
name: Build WPF Application

on:
  workflow_dispatch:

jobs:
  notice:
    runs-on: ubuntu-latest
    steps:
      - name: Migration notice
        run: echo "The WPF workflow is retired. Use dotnet.yml for Avalonia cross-platform builds."
```

- [x] **Step 3: Rewrite README in UTF-8 Chinese**

Write `README.md`:

```markdown
# JLUiCourse

JLUiCourse 是一个用于学习和研究的吉林大学选课辅助桌面客户端。当前版本已迁移到 Avalonia，可在 Windows、Linux 和 macOS 上构建运行。

## 运行

安装 .NET 8 SDK 后执行：

```powershell
dotnet restore
dotnet run --project iCourse/iCourse.csproj
```

## 发布

```powershell
dotnet publish iCourse/iCourse.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
dotnet publish iCourse/iCourse.csproj -c Release -r linux-x64 --self-contained false -o publish/linux-x64
dotnet publish iCourse/iCourse.csproj -c Release -r osx-x64 --self-contained false -o publish/osx-x64
dotnet publish iCourse/iCourse.csproj -c Release -r osx-arm64 --self-contained false -o publish/osx-arm64
```

## 使用前配置

- 在选课网站中将目标课程加入收藏，再使用本软件进行选课。
- 保持网络连接稳定。
- 账号、密码、日志和“下次不再显示免责声明”配置保存在当前系统用户的应用数据目录中。

## 免责声明

本软件完全免费，仅供学习和研究使用。请勿将其用于任何违反学校或相关法律法规的行为。用户需自行承担使用本软件所产生的后果，开发者不对因使用本软件造成的任何直接或间接损失负责。本软件未经吉林大学官方授权，与吉林大学无任何直接或间接关联。
```

- [x] **Step 4: Run matrix-equivalent local checks**

Run:

```powershell
dotnet test iCourse.sln -c Release
dotnet publish iCourse/iCourse.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
dotnet publish iCourse/iCourse.csproj -c Release -r linux-x64 --self-contained false -o publish/linux-x64
dotnet publish iCourse/iCourse.csproj -c Release -r osx-x64 --self-contained false -o publish/osx-x64
```

Expected: all commands pass.

- [x] **Step 5: Commit**

```powershell
git add .github/workflows/dotnet.yml .github/workflows/build.yml README.md
git commit -m "ci: build Avalonia app cross-platform"
```

### Task 10: Final Verification

**Files:**
- Inspect all changed files.

- [ ] **Step 1: Search for remaining WPF-only dependencies**

Run:

```powershell
rg "System.Windows|UseWPF|HandyControl|Microsoft.WindowsDesktop|net8.0-windows|WindowStartupLocation=\"CenterScreen\"" iCourse .github README.md
```

Expected: no `System.Windows`, `UseWPF`, `HandyControl`, `Microsoft.WindowsDesktop`, or `net8.0-windows` matches. `WindowStartupLocation="CenterScreen"` should only appear if Avalonia accepts it in the current version; otherwise replace it with `CenterOwner` or set startup position in code.

- [ ] **Step 2: Run full validation**

Run:

```powershell
dotnet restore iCourse.sln
dotnet build iCourse.sln -c Release
dotnet test iCourse.sln -c Release
dotnet publish iCourse/iCourse.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
dotnet publish iCourse/iCourse.csproj -c Release -r linux-x64 --self-contained false -o publish/linux-x64
dotnet publish iCourse/iCourse.csproj -c Release -r osx-x64 --self-contained false -o publish/osx-x64
dotnet publish iCourse/iCourse.csproj -c Release -r osx-arm64 --self-contained false -o publish/osx-arm64
```

Expected: every command exits successfully.

- [ ] **Step 3: Manual UI smoke test**

Run:

```powershell
dotnet run --project iCourse/iCourse.csproj
```

Expected:

- Main window opens.
- Disclaimer opens on first launch and closes after agreement.
- Login form renders with masked password input.
- Log panel appends messages and scrolls to the end.
- Captcha dialog shows a decoded image during login.
- Batch selection window displays returned batches.
- Query courses window displays a data grid and pagination controls.

- [ ] **Step 4: Commit any final fixes**

```powershell
git status --short
git add iCourse iCourse.Tests .github README.md iCourse.sln
git commit -m "fix: complete Avalonia migration verification"
```

## Self-Review

- Spec coverage: the plan migrates WPF project settings, app startup, windows, view models, platform services, storage paths, CI, tests, and README for cross-platform Avalonia.
- Placeholder scan: no implementation step relies on unspecified code; code snippets include the intended concrete file content or concrete edits.
- Type consistency: services introduced in Task 2 are injected in Task 3 and used by Task 4; `IJLUiCourseApi` introduced in Task 8 is used by view models and tests.
- Remaining execution risk: Avalonia 12 XAML binding syntax and `DataGrid` availability should be verified during Task 5/Task 10 builds; if the `DataGrid` control requires a separate package in the installed Avalonia 12 SDK, add the exact `Avalonia.Controls.DataGrid` package version matching `12.0.3` to `iCourse/iCourse.csproj` and rerun the Task 5 build.
