# Runs only on the disposable Windows GitHub Actions runner: installs and removes test packages.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Installer lifecycle tests run only on a disposable Windows GitHub Actions runner.'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$properties = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw -Encoding UTF8
$version = [string]$properties.Project.PropertyGroup.Version
$packageDir = Join-Path $projectRoot 'dist/packages'
$setup = Join-Path $packageDir "LunaArc-$version-setup-win-x64.exe"
$portable = Join-Path $packageDir "LunaArc-$version-portable-win-x64.zip"
$testBase = [System.IO.Path]::GetFullPath($env:RUNNER_TEMP)
$testRoot = Join-Path $testBase ('LunaArc-Packages-' + [guid]::NewGuid().ToString('N'))
$installDir = Join-Path $testRoot 'installed app'
$portableDir = Join-Path $testRoot 'portable app'
$menuKey = 'HKCU:\Software\Classes\SystemFileAssociations\.zip\shell\LunaArc_ExtractHere\command'

function Invoke-HiddenProgram([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "$File failed with exit code $($process.ExitCode)." }
}
function Assert-MenuOwner([string]$Directory) {
    $expected = '"' + (Join-Path $Directory 'luna-cli.exe') + '" extract-here "%1"'
    $actual = (Get-Item -LiteralPath $menuKey).GetValue('')
    if ($actual -ne $expected) { throw "Unexpected context-menu command: $actual" }
}

New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
try {
    Expand-Archive -LiteralPath $portable -DestinationPath $portableDir
    foreach ($file in @('LunaArc.exe', 'luna-cli.exe', 'InstallContextMenu.cmd', 'UninstallContextMenu.cmd')) {
        if (-not (Test-Path -LiteralPath (Join-Path $portableDir $file))) { throw "Portable is missing $file" }
    }
    Invoke-HiddenProgram $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TASKS=contextmenu', "/DIR=`"$installDir`"")
    Assert-MenuOwner $installDir
    foreach ($exe in @('LunaArc.exe', 'luna-cli.exe')) {
        $installedHash = (Get-FileHash -LiteralPath (Join-Path $installDir $exe)).Hash
        $portableHash = (Get-FileHash -LiteralPath (Join-Path $portableDir $exe)).Hash
        if ($installedHash -ne $portableHash) { throw "Installed and portable $exe differ." }
    }
    Invoke-HiddenProgram (Join-Path $installDir 'luna-cli.exe') @('help')
    Invoke-HiddenProgram (Join-Path $installDir 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    if (Test-Path -LiteralPath $menuKey) { throw 'Uninstall left its context menu behind.' }
    if (Test-Path -LiteralPath (Join-Path $installDir 'LunaArc.exe')) { throw 'Uninstall left the application executable behind.' }

    # Installation without Explorer integration must leave a clean menu state.
    Invoke-HiddenProgram $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TASKS=', "/DIR=`"$installDir`"")
    if (Test-Path -LiteralPath $menuKey) { throw 'Unchecked Explorer task still installed context menus.' }
    # The portable app exposes the same commands. Its registration must survive removal of another installation.
    Invoke-HiddenProgram (Join-Path $portableDir 'luna-cli.exe') @('register-shell')
    Assert-MenuOwner $portableDir
    Invoke-HiddenProgram (Join-Path $installDir 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    Assert-MenuOwner $portableDir
    Invoke-HiddenProgram (Join-Path $portableDir 'luna-cli.exe') @('unregister-shell')
    if (Test-Path -LiteralPath $menuKey) { throw 'Portable unregister left its context menu behind.' }
    Write-Output 'PASS: setup install/uninstall, optional menus, identical portable binaries and coexistence cleanup.'
} finally {
    if (Test-Path -LiteralPath (Join-Path $portableDir 'luna-cli.exe')) {
        $cleanup = Start-Process -FilePath (Join-Path $portableDir 'luna-cli.exe') -ArgumentList @('unregister-shell', '--if-path', "`"$portableDir`"") -WindowStyle Hidden -Wait -PassThru
    }
    if (Test-Path -LiteralPath (Join-Path $installDir 'unins000.exe')) {
        $cleanup = Start-Process -FilePath (Join-Path $installDir 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -WindowStyle Hidden -Wait -PassThru
    }
    $resolvedRoot = [System.IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedRoot.StartsWith($testBase.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the runner temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
