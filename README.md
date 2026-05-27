# JLUiCourse

[![Build](https://github.com/wzyyyyyyy/JLUiCourse/actions/workflows/dotnet.yml/badge.svg)](https://github.com/wzyyyyyyy/JLUiCourse/actions/workflows/dotnet.yml)
![downloads](https://img.shields.io/github/downloads/wzyyyyyyy/JLUiCourse/total.svg)

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
