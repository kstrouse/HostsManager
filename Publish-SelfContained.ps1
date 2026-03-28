param(
    [string[]]$RuntimeIdentifiers = @("win-x64", "osx-x64", "osx-arm64"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "src\HostsManager\HostsManager.csproj"
$publishRoot = Join-Path $PSScriptRoot "artifacts\publish"

foreach ($rid in $RuntimeIdentifiers) {
    $outputPath = Join-Path $publishRoot $rid

    Write-Host "Publishing $rid to $outputPath" -ForegroundColor Cyan

    dotnet publish $projectPath `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:UseAppHost=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $outputPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for runtime '$rid'."
    }
}

Write-Host ""
Write-Host "Publish complete. App folders are under $publishRoot" -ForegroundColor Green
