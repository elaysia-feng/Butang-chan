# 构建桌宠。
#
# 用 Roslyn（Microsoft.Net.Compilers.Toolset）而不是系统自带的 csc：
# 系统那台是 4.8 时代的编译器，只支持 C# 5，写不了 ?. / 内联 out / 模式匹配。
# 工具链只下一次，缓存在 %LOCALAPPDATA%\ButangPet\csc，项目目录里不留负担。
#
# 用法：pwsh -File build.ps1
$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $root 'src'
$outDir = Join-Path $root 'app'
$cache  = Join-Path $env:LOCALAPPDATA 'ButangPet\csc'
$modern = Join-Path $cache 'csc.exe'
$legacy = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

function Get-Csc {
    if (Test-Path $modern) { return $modern }
    Write-Host '首次构建，正在下载 Roslyn 编译器（约 21MB，只需一次）…' -ForegroundColor Cyan
    $pkg = Join-Path $env:TEMP 'roslyn.nupkg'
    if (-not (Test-Path $pkg)) {
        Invoke-WebRequest -Uri 'https://www.nuget.org/api/v2/package/Microsoft.Net.Compilers.Toolset/4.8.0' `
                         -OutFile $pkg -UseBasicParsing -TimeoutSec 300
    }
    $ex = Join-Path $env:TEMP ('roslyn_' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($pkg, $ex)
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    # 包里编译器在 tasks\net472 下，不在 tools 下
    Copy-Item (Join-Path $ex 'tasks\net472\*') $cache -Recurse -Force
    if (Test-Path $modern) { return $modern }
    throw 'Roslyn 解压后找不到 csc.exe'
}

$csc = Get-Csc
Write-Host "编译器：$csc"

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# 角色用原生透明窗口，定制消息与设置面板使用独立 WebView2。
foreach ($dll in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') {
    Copy-Item (Join-Path $root ("vendor\" + $dll)) $outDir -Force
}
$sources = Get-ChildItem $srcDir -Filter *.cs | ForEach-Object { $_.FullName }
$exePath = Join-Path $outDir 'ButangPet.exe'
$before = if (Test-Path $exePath) { (Get-Item $exePath).LastWriteTimeUtc } else { [datetime]::MinValue }
Write-Host "编译 $($sources.Count) 个源文件 → $exePath"

& $csc `
  /nologo /target:winexe /platform:x64 /optimize+ /utf8output /langversion:9.0 `
  "/out:$exePath" `
  "/win32manifest:$root\app.manifest" `
  "/reference:$outDir\Microsoft.Web.WebView2.Core.dll" `
  "/reference:$outDir\Microsoft.Web.WebView2.WinForms.dll" `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  /reference:System.Web.Extensions.dll `
  /reference:System.Net.Http.dll `
  $sources

if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }

$after = (Get-Item $exePath).LastWriteTimeUtc
if ($after -le $before) {
    # 编译"成功"但产物没更新 —— 一定要说出来，否则会对着旧 exe 调半天
    throw "编译没有产生新的 exe（时间戳没变），源码可能没被改动"
}

# 托盘图标要跟着走：没有图标程序也能跑，但托盘会变成默认图标
if (Test-Path (Join-Path $root 'assets\tray.ico')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $outDir 'assets') | Out-Null
    Copy-Item (Join-Path $root 'assets\tray.ico') (Join-Path $outDir 'assets') -Force
}

$spriteOut = Join-Path $outDir 'assets\sprites'
New-Item -ItemType Directory -Force -Path $spriteOut | Out-Null
foreach ($name in 'idle', 'blink', 'talk', 'wink', 'shy', 'sleepy', 'wave', 'cheer', 'bashful', 'yawn') {
    Copy-Item (Join-Path $root ("assets\sprites\" + $name + '.png')) $spriteOut -Force
}

$motionOut = Join-Path $outDir 'assets\motions'
New-Item -ItemType Directory -Force -Path $motionOut | Out-Null
Copy-Item (Join-Path $root 'assets\motions\*') $motionOut -Recurse -Force

$panelOut = Join-Path $outDir 'web\panel'
New-Item -ItemType Directory -Force -Path $panelOut | Out-Null
Copy-Item (Join-Path $root 'web\panel\*') $panelOut -Recurse -Force

# 打包内置插件，不删除用户添加的插件。
$pluginOut = Join-Path $outDir 'plugins'
New-Item -ItemType Directory -Force -Path $pluginOut | Out-Null
Copy-Item (Join-Path $root 'plugins\*.json') $pluginOut -Force

$exe = Get-Item $exePath
Write-Host "完成：$($exe.FullName)  $([math]::Round($exe.Length/1KB,1)) KB  $($exe.LastWriteTime)" -ForegroundColor Green
