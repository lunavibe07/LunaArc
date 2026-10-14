[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Runtime = 'win-x64',
    [string]$DotNet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $projectRoot "dist/$Runtime"
if ($DotNet -eq 'dotnet' -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $localSdk = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
    if (Test-Path -LiteralPath $localSdk) { $DotNet = $localSdk }
}
Get-Command $DotNet -ErrorAction Stop | Out-Null

Push-Location $projectRoot
try {
    Write-Host "Publishing LunaArc for $Runtime..." -ForegroundColor Cyan
    foreach ($project in @('LunaArc.Cli', 'LunaArc.Studio')) {
        & $DotNet publish "src/$project/$project.csproj" -c Release -r $Runtime --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed (exit code $LASTEXITCODE)." }
    }
    $suffix = if ($Runtime.StartsWith('win-')) { '.exe' } else { '' }
    foreach ($name in @('LunaArc', 'luna-cli')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDir "$name$suffix"))) {
            throw "Missing published executable: $name$suffix"
        }
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'branding/EULA_License.txt') -Destination $publishDir
    Copy-Item -LiteralPath (Join-Path $projectRoot 'branding/Fluent_Icons_LICENSE.txt') -Destination $publishDir
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishDir
    if ($Runtime.StartsWith('win-')) {
        foreach ($script in @('InstallContextMenu.cmd', 'UninstallContextMenu.cmd')) {
            Copy-Item -LiteralPath (Join-Path $projectRoot "installers/$script") -Destination $publishDir
        }
    }
    Write-Host "Publish succeeded: $publishDir" -ForegroundColor Green
} finally {
    Pop-Location
}
