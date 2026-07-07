# 右键菜单管家 构建脚本
# 用法: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"

$msbuild = "C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
    # 尝试用 vswhere 定位任意版本的 MSBuild
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    }
}
if (-not $msbuild -or -not (Test-Path $msbuild)) { throw "未找到 MSBuild，请安装 Visual Studio 或 Build Tools。" }

# 统一源码为 UTF-8 with BOM，避免编译器按本地代码页误读中文
$utf8bom = New-Object System.Text.UTF8Encoding($true)
Get-ChildItem $src -Recurse -Include *.cs, *.xaml, *.manifest, *.csproj | ForEach-Object {
    $text = [System.IO.File]::ReadAllText($_.FullName)
    [System.IO.File]::WriteAllText($_.FullName, $text, $utf8bom)
}

& $msbuild (Join-Path $src "ContextMenuManager.csproj") /p:Configuration=Release /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw "构建失败（MSBuild 退出码 $LASTEXITCODE）" }

$exe = Join-Path $src "bin\Release\ContextMenuManager.exe"
if (-not (Test-Path $exe)) { throw "未找到输出文件 $exe" }

$dist = Join-Path $root "发布"
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item $exe (Join-Path $dist "右键菜单管家.exe") -Force
Write-Host ""
Write-Host "构建成功 → $dist\右键菜单管家.exe"
