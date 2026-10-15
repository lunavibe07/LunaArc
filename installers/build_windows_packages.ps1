[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [string]$DotNet = 'dotnet',
    [string]$Iscc = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $projectRoot 'dist/win-x64'
$packageDir = Join-Path $projectRoot 'dist/packages'
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'build_windows.ps1') -Runtime win-x64 -DotNet $DotNet
}
foreach ($file in @('LunaArc.exe', 'luna-cli.exe', 'LunaArc.7zr.exe', 'SevenZip_LICENSE.txt', 'SevenZip_COPYING.txt', 'SevenZip_NOTICE.txt', 'InstallContextMenu.cmd', 'UninstallContextMenu.cmd', 'README.md', 'EULA_License.txt','LunaArc.ContextMenu.dll','LunaArc.Menu.ico','LunaArc.ShellMenu.msix','LunaArc.Menu.cer','RegisterWindows11Menu.ps1','InstallWindows11Menu.cmd')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $file))) { throw "Missing published file: $file" }
}
[xml]$properties = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw -Encoding UTF8
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Unsupported product version: $version" }
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

if (-not $Iscc) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $Iscc = $command.Source }
    else {
        $compilerPaths = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe'),
            (Join-Path $projectRoot '.tools/inno/ISCC.exe')
        )
        $Iscc = $compilerPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $Iscc) { throw 'Install Inno Setup 6.3+ or pass -Iscc with the compiler path. See https://jrsoftware.org/isdl.php' }
Get-Command $Iscc -ErrorAction Stop | Out-Null

$portable = Join-Path $packageDir "LunaArc-$version-portable-win-x64.zip"
if (Test-Path -LiteralPath $portable) { Remove-Item -LiteralPath $portable -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($portable, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $publishDir -File | Where-Object { $_.Extension -ne '.pdb' }) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $file.FullName, $file.Name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }

& $Iscc "/DAppVersion=$version" "/DSourceDir=$publishDir" "/DOutputDir=$packageDir" (Join-Path $PSScriptRoot 'LunaArc.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed (exit code $LASTEXITCODE)." }
$setup = Join-Path $packageDir "LunaArc-$version-setup-win-x64.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw 'Installer output is missing.' }
$hashLines = @($portable, $setup) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($_))"
}
Set-Content -LiteralPath (Join-Path $packageDir 'SHA256SUMS.txt') -Value $hashLines -Encoding ASCII
Write-Host "Windows setup and portable packages created in $packageDir" -ForegroundColor Green
