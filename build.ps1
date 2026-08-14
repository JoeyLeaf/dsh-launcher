# 用 Windows 自带的 .NET Framework csc.exe 编译 DSH 启动器（无需安装任何 SDK）
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "未找到 csc.exe: $csc" }

$src = @('Theme.cs', 'SettingsForm.cs', 'DshService.cs', 'DshLauncher.cs') | ForEach-Object { Join-Path $here $_ }
$out = Join-Path $here 'DshLauncher.exe'

& $csc /nologo /target:winexe /optimize+ /codepage:65001 `
    /out:$out `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $src

if ($LASTEXITCODE -ne 0) { throw "编译失败，exit code: $LASTEXITCODE" }
Write-Host "编译成功 -> $out"
