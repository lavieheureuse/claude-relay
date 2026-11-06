# 编译 ClaudeSwitch.exe（单文件 WPF，无依赖，使用系统自带的 .NET Framework 4.8 编译器）
# 用法：powershell -ExecutionPolicy Bypass -File build.ps1
param([string]$OutName = 'ClaudeRelay.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $fw 'WPF'
$csc = Join-Path $fw 'csc.exe'

if (-not (Test-Path $csc)) { throw "找不到 .NET Framework 编译器：$csc" }
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist -Force | Out-Null }

# ---- 图标 ----
$ico = Join-Path $root 'app.ico'
if (-not (Test-Path $ico)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\make-icon.ps1')
}

# ---- 引用 ----
$refs = @(
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $fw 'System.Web.Extensions.dll'),
    (Join-Path $fw 'System.Xml.dll'),
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'WindowsBase.dll'),
    (Join-Path $fw 'System.Xaml.dll')
)
$missing = $refs | Where-Object { -not (Test-Path $_) }
if ($missing) { throw ("缺少引用程序集：" + ($missing -join ', ')) }

$sources = Get-ChildItem (Join-Path $src '*.cs') | ForEach-Object { $_.FullName }
$out = Join-Path $dist $OutName
# 图标同时作为内嵌资源，程序里按需取 16 像素（托盘）等小尺寸，避免从 32 像素缩小发糊
$cscArgs = @('/nologo', '/target:winexe', '/platform:anycpu', "/out:`"$out`"", "/win32icon:`"$ico`"", "/resource:`"$ico`",ClaudeRelay.app.ico")
$cscArgs += ($refs | ForEach-Object { "/r:`"$_`"" })
$cscArgs += $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，exit=$LASTEXITCODE" }
if (-not (Test-Path $out)) { throw "没有生成 exe" }
$info = Get-Item $out
Write-Host ("编译成功: {0}  ({1:N0} 字节)" -f $info.FullName, $info.Length)
