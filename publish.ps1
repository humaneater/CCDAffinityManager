param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "src\CcdAffinityManager\CcdAffinityManager.csproj"
$publishDirectory = Join-Path $PSScriptRoot "publish"

dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

$executable = Join-Path $publishDirectory "CcdAffinityManager.exe"
Write-Host ""
Write-Host "Published: $executable"
