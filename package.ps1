param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.2"
)

$ErrorActionPreference = "Stop"

$root = [IO.Path]::GetFullPath($PSScriptRoot)
$publishScript = Join-Path $root "publish.ps1"
$publishDirectory = Join-Path $root "publish"
$packagingDirectory = Join-Path $root "packaging"
$artifactsDirectory = Join-Path $root "artifacts"
$portableDirectory = Join-Path $artifactsDirectory "portable"
$distDirectory = Join-Path $root "dist"
$portableZip = Join-Path $distDirectory "CcdAffinityManager-$Version-portable.zip"
$installerExe = Join-Path $distDirectory "CcdAffinityManager-Setup-$Version.exe"
$installerScript = Join-Path $packagingDirectory "CcdAffinityManager.iss"

function Remove-WorkspaceDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $resolvedRoot = [IO.Path]::GetFullPath($root)
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $rootPrefix = $resolvedRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    if (-not $resolvedPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a directory outside the project: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

function Resolve-InnoCompiler {
    $command = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    return $candidates |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}

& $publishScript -Configuration $Configuration -Runtime $Runtime

Remove-WorkspaceDirectory -Path $portableDirectory
New-Item -ItemType Directory -Path $portableDirectory -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $publishDirectory "CcdAffinityManager.exe") `
    -Destination $portableDirectory
Copy-Item -LiteralPath (Join-Path $packagingDirectory "README.txt") `
    -Destination $portableDirectory

New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null

if (Test-Path -LiteralPath $portableZip) {
    Remove-Item -LiteralPath $portableZip -Force
}

Compress-Archive `
    -Path (Join-Path $portableDirectory "*") `
    -DestinationPath $portableZip `
    -CompressionLevel Optimal

$innoCompiler = Resolve-InnoCompiler
if (-not $innoCompiler) {
    throw "Inno Setup 6 was not found. Install it with: winget install --id JRSoftware.InnoSetup --exact"
}

if (Test-Path -LiteralPath $installerExe) {
    Remove-Item -LiteralPath $installerExe -Force
}

& $innoCompiler "/DAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$outputs = @($portableZip, $installerExe)
foreach ($output in $outputs) {
    if (-not (Test-Path -LiteralPath $output)) {
        throw "Expected package was not created: $output"
    }

    $hash = Get-FileHash -LiteralPath $output -Algorithm SHA256
    "$($hash.Hash)  $([IO.Path]::GetFileName($output))" |
        Set-Content -LiteralPath "$output.sha256" -Encoding ascii
}

Write-Host ""
Write-Host "Packages created:"
foreach ($output in $outputs) {
    Write-Host "  $output"
}
