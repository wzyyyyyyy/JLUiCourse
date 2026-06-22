param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = $env:PACKAGE_VERSION,
    [string]$OutputRoot = "artifacts/windows"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "1.0.0"
}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$PublishDir = Join-Path $RepoRoot "artifacts/publish/$Runtime"
$OutputDir = Join-Path $RepoRoot $OutputRoot
$InstallerDir = Join-Path $RepoRoot "artifacts/installer/windows"
$InstallerScript = Join-Path $InstallerDir "iCourse.iss"
$InstallerName = "iCourse-$Version-$Runtime-setup"

New-Item -ItemType Directory -Force -Path $PublishDir, $OutputDir, $InstallerDir | Out-Null

dotnet publish (Join-Path $RepoRoot "iCourse/iCourse.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    --output $PublishDir

$InnoSetup = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $InnoSetup) {
    $Candidate = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (Test-Path $Candidate) {
        $InnoSetup = Get-Item $Candidate
    }
}

if (-not $InnoSetup) {
    throw "Inno Setup compiler was not found. Install it with: choco install innosetup"
}

$AppPublisher = "wzyyyyyyy"
$ExePath = Join-Path $PublishDir "iCourse.exe"
if (-not (Test-Path $ExePath)) {
    throw "Published executable was not found at $ExePath"
}

@"
#define MyAppName "JLUiCourse"
#define MyAppVersion "$Version"
#define MyAppPublisher "$AppPublisher"
#define MyAppExeName "iCourse.exe"

[Setup]
AppId={{0EC1F2E1-8621-4C89-9F49-7F3D6E0472B7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=$OutputDir
OutputBaseFilename=$InstallerName
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "$PublishDir\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
"@ | Set-Content -Path $InstallerScript -Encoding UTF8

& $InnoSetup.Source $InstallerScript

$InstallerPath = Join-Path $OutputDir "$InstallerName.exe"
if (-not (Test-Path $InstallerPath)) {
    throw "Expected installer was not created at $InstallerPath"
}

Write-Host "Windows installer created: $InstallerPath"
