param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\PortBridge\PortBridge.csproj'
$output = Join-Path $PSScriptRoot "dist\$Runtime"
dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $output
if ($LASTEXITCODE -ne 0) { throw '构建失败' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination $output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'examples') -Destination $output -Recurse -Force
$licenseOutput = Join-Path $output 'licenses'
New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
# NuGet records the resolved package directories; license files can be copied
# without depending on the developer's user profile or package-cache location.
$assets = Get-Content (Join-Path $PSScriptRoot 'src\PortBridge\obj\project.assets.json') -Raw | ConvertFrom-Json
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    foreach ($cache in $assets.packageFolders.PSObject.Properties.Name) {
        $packageDir = Join-Path $cache $library.Value.path
        if (!(Test-Path $packageDir)) { continue }
        $packageName = $library.Name.Replace('/', '-')
        $packageLicenseDir = Join-Path $licenseOutput $packageName
        New-Item -ItemType Directory -Path $packageLicenseDir -Force | Out-Null
        Get-ChildItem $packageDir -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY)' } |
            Copy-Item -Destination $packageLicenseDir
        # NuGet's nuspec contains the license expression and copyright owner.
        Get-ChildItem $packageDir -Filter '*.nuspec' -File | Copy-Item -Destination $packageLicenseDir
        if ($library.Name -like 'SSH.NET/*') {
            Copy-Item (Join-Path $PSScriptRoot 'licenses\SSH.NET-LICENSE') $packageLicenseDir
        }
        break
    }
}
Get-ChildItem (Join-Path $PSScriptRoot 'licenses') -File | Copy-Item -Destination $licenseOutput
Compress-Archive -Path "$output\*" -DestinationPath (Join-Path $PSScriptRoot "dist\PortBridge-$Runtime.zip") -Force
Write-Host "已生成：$output\PortBridge.exe"
