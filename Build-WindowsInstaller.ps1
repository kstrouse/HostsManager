param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "0.1.1",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "src\HostsManager.Desktop\HostsManager.Desktop.csproj"
$publishRoot = Join-Path $PSScriptRoot "artifacts\publish"
$outputPath = Join-Path $publishRoot $RuntimeIdentifier
$installerRoot = Join-Path $PSScriptRoot "artifacts\installer"
$installerScript = Join-Path $PSScriptRoot "installer\windows\HostsManager.iss"
$defaultIsccPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"

if (-not $SkipPublish) {
    Write-Host "Publishing $RuntimeIdentifier to $outputPath" -ForegroundColor Cyan

    dotnet publish $projectPath `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:UseAppHost=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $outputPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for runtime '$RuntimeIdentifier'."
    }
}

if (-not (Test-Path $outputPath)) {
    throw "Publish output not found: $outputPath"
}

if (-not (Test-Path $installerScript)) {
    throw "Installer script not found: $installerScript"
}

if (-not (Test-Path $defaultIsccPath)) {
    Write-Warning "Inno Setup compiler not found at '$defaultIsccPath'."
    Write-Warning "Publish output is ready at '$outputPath'. Install Inno Setup 6 to build the installer."
    return
}

New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null

Write-Host "Building installer $Version" -ForegroundColor Cyan

& $defaultIsccPath `
    "/DAppVersion=$Version" `
    "/DPublishDir=$outputPath" `
    "/DInstallerOutputDir=$installerRoot" `
    $installerScript

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed."
}

Write-Host ""
Write-Host "Installer complete. Output is under $installerRoot" -ForegroundColor Green
