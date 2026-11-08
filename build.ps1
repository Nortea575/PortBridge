param(
    [string]$IsccPath,
    [switch]$PreserveStaging
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$projectDir = Join-Path $repoRoot 'src\PortBridge'
$projectPath = Join-Path $projectDir 'PortBridge.csproj'
$distDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
$stageDir = [IO.Path]::GetFullPath((Join-Path $distDir 'setup-staging'))
$publishDir = Join-Path $stageDir 'win-x64'
$setupScript = Join-Path $repoRoot 'installer\PortBridge.iss'

$version = (Get-Content -LiteralPath (Join-Path $repoRoot 'release-version.txt') -Raw).Trim()
[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
$projectVersion = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $version -ne $projectVersion) {
    throw 'release-version.txt 必须与 PortBridge.csproj 的版本一致。'
}

if (!(Test-Path -LiteralPath $setupScript)) { throw "未找到安装脚本：$setupScript" }

# Keep all staging operations inside the repository's fixed dist directory.
$distPrefix = $distDir.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!$stageDir.StartsWith($distPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝访问仓库 dist 目录之外的暂存路径：$stageDir"
}
if (!$PreserveStaging -and (Test-Path -LiteralPath $stageDir)) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}
try {
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'PortBridge Windows x64 发布失败。' }

    $appExe = Join-Path $publishDir 'PortBridge.exe'
    if (!(Test-Path -LiteralPath $appExe)) { throw '发布输出中未找到 PortBridge.exe。' }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $publishDir
    Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishDir

    $licenseOutput = Join-Path $publishDir 'licenses'
    New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'licenses') -File | Copy-Item -Destination $licenseOutput

# Copy license texts shipped in the resolved NuGet packages without including
# README files, examples, tests, or package metadata in the installed program.
    $assetsPath = Join-Path $projectDir 'obj\project.assets.json'
    if (!(Test-Path -LiteralPath $assetsPath)) { throw '发布后未找到 NuGet 项目依赖清单。' }
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        foreach ($cache in $assets.packageFolders.PSObject.Properties.Name) {
            $packageDir = Join-Path $cache $library.Value.path
            if (!(Test-Path -LiteralPath $packageDir)) { continue }
            $packageFiles = Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY)' }
            if ($packageFiles.Count -gt 0) {
                $packageName = $library.Name -replace '[/\\]', '-'
                $packageLicenseDir = Join-Path $licenseOutput $packageName
                New-Item -ItemType Directory -Path $packageLicenseDir -Force | Out-Null
                $packageFiles | Copy-Item -Destination $packageLicenseDir
                if ($library.Name -like 'SSH.NET/*') {
                    Copy-Item -LiteralPath (Join-Path $repoRoot 'licenses\SSH.NET-LICENSE') -Destination $packageLicenseDir -Force
                }
            }
            break
        }
    }

    if (!$IsccPath) { $IsccPath = $env:ISCC_PATH }
    $compilerCandidates = @()
    if ($IsccPath) { $compilerCandidates += $IsccPath }
    $localCompiler = Join-Path $repoRoot '..\工具\Inno Setup 7.1.1\ISCC.exe'
    if (Test-Path -LiteralPath $localCompiler) { $compilerCandidates += $localCompiler }
    if ($env:ProgramFiles) {
        $programFilesCompiler = Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'
        if (Test-Path -LiteralPath $programFilesCompiler) { $compilerCandidates += $programFilesCompiler }
    }
    $pathCompiler = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($pathCompiler) { $compilerCandidates += $pathCompiler.Source }
    $compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$compiler) { throw '未找到 Inno Setup 7.1.1 编译器。请安装该版本，或通过 -IsccPath / ISCC_PATH 指定 ISCC.exe。' }

    $compilerVersion = (& $compiler --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $compilerVersion -ne '7.1.1') {
        throw "安装包固定使用 Inno Setup 7.1.1；当前编译器报告版本：$compilerVersion"
    }

    $versionDefine = "--define=AppVersion=$version"
    $outputName = "PortBridge-$version-Setup-x64"
    Push-Location $repoRoot
    try {
        & $compiler $versionDefine "--output-dir=$distDir" "--output-filename=$outputName" $setupScript
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 安装包编译失败。' }
    }
    finally {
        Pop-Location
    }

    $setupOutput = Join-Path $distDir "$outputName.exe"
    if (!(Test-Path -LiteralPath $setupOutput)) { throw 'Inno Setup 未生成预期的 Setup 文件。' }
    $setupHash = Get-FileHash -LiteralPath $setupOutput -Algorithm SHA256
    Write-Host "已生成：$setupOutput"
    Write-Host "SHA-256：$($setupHash.Hash)"
}
finally {
    if (!$PreserveStaging -and (Test-Path -LiteralPath $stageDir)) {
        Remove-Item -LiteralPath $stageDir -Recurse -Force
    }
}
