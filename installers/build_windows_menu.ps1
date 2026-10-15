[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $projectRoot 'native_libs/WindowsContextMenu'
$buildDir = Join-Path $projectRoot '.tools/context-menu-build'
$staging = Join-Path $buildDir 'identity'
Get-Command cmake.exe -ErrorAction Stop | Out-Null
& cmake.exe -S $nativeRoot -B $buildDir -A x64
if ($LASTEXITCODE -ne 0) { throw 'Configure the native menu with Visual Studio C++ tools and Windows SDK installed.' }
& cmake.exe --build $buildDir --config Release
if ($LASTEXITCODE -ne 0) { throw 'Native menu build failed.' }
& ctest.exe --test-dir $buildDir -C Release --verbose --output-on-failure
if ($LASTEXITCODE -ne 0) { throw 'Native menu contract checks failed.' }
New-Item -ItemType Directory -Path $OutputDirectory,$staging -Force | Out-Null
$dll = Join-Path $OutputDirectory 'LunaArc.ContextMenu.dll'
Copy-Item -LiteralPath (Join-Path $buildDir 'Release/LunaArc.ContextMenu.dll') -Destination $dll
$sdkBin = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'x64/makeappx.exe') } |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdkBin) { throw 'Windows SDK MakeAppx and SignTool are required for the Windows 11 menu.' }
$makeappx = Join-Path $sdkBin.FullName 'x64/makeappx.exe'
$signtool = Join-Path $sdkBin.FullName 'x64/signtool.exe'
$certificate = $null
$temporaryCertificate = $false
try {
    if ($env:LUNAARC_MENU_SIGNING_THUMBPRINT) {
        if ($env:LUNAARC_MENU_SIGNING_THUMBPRINT -notmatch '^[0-9a-fA-F]{40}$') { throw 'Invalid signing-certificate thumbprint.' }
        $certificate = Get-Item -LiteralPath ('Cert:/CurrentUser/My/' + $env:LUNAARC_MENU_SIGNING_THUMBPRINT)
    } elseif ($env:GITHUB_ACTIONS -eq 'true') {
        # Public preview builds get a disposable test signer. Never export its private key.
        $certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=LunaArc Shell Menu Preview' -KeyUsage DigitalSignature -CertStoreLocation 'Cert:/CurrentUser/My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(1)
        $temporaryCertificate = $true
    } else {
        throw 'Set LUNAARC_MENU_SIGNING_THUMBPRINT to your signing certificate, or use -SkipExplorerExtension for a managed-only local publish. CI produces test-signed menu previews.'
    }
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $nativeRoot 'AppxManifest.xml') -Raw
    [xml]$properties = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
    $manifest.Package.Identity.Version = ([version][string]$properties.Project.PropertyGroup.Version).ToString(3) + '.0'
    $manifest.Package.Identity.Publisher = $certificate.Subject
    $manifest.Save((Join-Path $staging 'AppxManifest.xml'))
    $assets = Join-Path $staging 'Assets'
    New-Item -ItemType Directory -Path $assets -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'branding/app_logo_256.png') -Destination (Join-Path $assets 'Logo.png')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'branding/app_logo.ico') -Destination (Join-Path $OutputDirectory 'LunaArc.Menu.ico')
    & $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint $dll
    if ($LASTEXITCODE -ne 0) { throw 'Native menu signing failed.' }
    Set-Content -LiteralPath (Join-Path $assets 'MenuDll.sha256') -Value (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -Encoding ASCII
    $package = Join-Path $OutputDirectory 'LunaArc.ShellMenu.msix'
    & $makeappx pack /o /d $staging /nv /p $package
    if ($LASTEXITCODE -ne 0) { throw 'Sparse menu package build failed.' }
    & $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint $package
    if ($LASTEXITCODE -ne 0) { throw 'Menu package signing failed.' }
    Export-Certificate -Cert $certificate -FilePath (Join-Path $OutputDirectory 'LunaArc.Menu.cer') | Out-Null
    foreach ($file in @('RegisterWindows11Menu.ps1','InstallWindows11Menu.cmd')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $OutputDirectory
    }
    & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $OutputDirectory 'RegisterWindows11Menu.ps1') -VerifyOnly
    if ($LASTEXITCODE -ne 0) { throw 'Signed menu payload verification failed.' }
    Write-Output 'Native checks passed; Windows 11 menu preview packaged. No certificate trust or Explorer registration was changed.'
} finally {
    if ($temporaryCertificate -and $certificate) { Remove-Item -LiteralPath ('Cert:/CurrentUser/My/' + $certificate.Thumbprint) -Force }
}
