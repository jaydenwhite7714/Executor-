<#
.SYNOPSIS
    Builds and optionally publishes Driven Test Engine.

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -SelfContained -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [switch]$SelfContained,

    [switch]$NoPublish,

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$solution = Join-Path $root 'DrivenTestEngine.sln'
$appProject = Join-Path $root 'src\DrivenTestEngine.App\DrivenTestEngine.App.csproj'
$publishDir = Join-Path $root 'publish'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK was not found on PATH. Install .NET 8 SDK or newer from https://dot.net.'
}

Write-Host "== .NET SDK ==" -ForegroundColor Cyan
dotnet --version

if ($Clean) {
    Write-Host "== Cleaning ==" -ForegroundColor Cyan
    dotnet clean $solution -c $Configuration | Out-Null
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
}

Write-Host "== Building $Configuration ==" -ForegroundColor Cyan
dotnet build $solution -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

if ($NoPublish) {
    Write-Host '[ok] Build complete.' -ForegroundColor Green
    return
}

Write-Host "== Publishing ($Runtime, self-contained=$($SelfContained.IsPresent)) ==" -ForegroundColor Cyan
$publishArgs = @(
    'publish', $appProject,
    '-c', $Configuration,
    '-r', $Runtime,
    '-o', $publishDir,
    '-v', 'minimal',
    "--self-contained=$($SelfContained.IsPresent.ToString().ToLowerInvariant())"
)
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

Write-Host ''
Write-Host "[ok] Executable: $(Join-Path $publishDir 'DrivenTestEngine.exe')" -ForegroundColor Green
if (-not $SelfContained) {
    Write-Host '     Framework-dependent: install the .NET 8 Desktop Runtime on target machines.' -ForegroundColor DarkGray
}
