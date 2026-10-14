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
    # Render a small code-native adaptation of branding/app_logo.svg.
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap(256,256)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dark = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#0F172A'))
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Point(40,40)), (New-Object System.Drawing.Point(208,208)), ([System.Drawing.ColorTranslator]::FromHtml('#6366F1')), ([System.Drawing.ColorTranslator]::FromHtml('#EC4899')))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#F8FAFC'))
    $blue = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#38BDF8'))
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.FillRectangle($dark,0,0,256,256)
        $graphics.FillEllipse($gradient,40,40,168,168)
        $graphics.FillEllipse($dark,88,40,136,136)
        $graphics.FillRectangle($white,112,88,24,16)
        $graphics.FillRectangle($white,112,114,24,16)
        $graphics.FillRectangle($blue,112,140,24,28)
        $bitmap.Save((Join-Path $assets 'Logo.png'),[System.Drawing.Imaging.ImageFormat]::Png)
        $png = [IO.File]::ReadAllBytes((Join-Path $assets 'Logo.png'))
        $iconStream = [IO.File]::Create((Join-Path $OutputDirectory 'LunaArc.Menu.ico'))
        $writer = New-Object IO.BinaryWriter($iconStream)
        try {
            $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22)
            $writer.Write($png)
        } finally { $writer.Dispose(); $iconStream.Dispose() }
    } finally { $graphics.Dispose(); $bitmap.Dispose(); $dark.Dispose(); $gradient.Dispose(); $white.Dispose(); $blue.Dispose() }
    $package = Join-Path $OutputDirectory 'LunaArc.ShellMenu.msix'
    & $makeappx pack /o /d $staging /nv /p $package
    if ($LASTEXITCODE -ne 0) { throw 'Sparse menu package build failed.' }
    foreach ($file in @($dll,$package)) {
        & $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint $file
        if ($LASTEXITCODE -ne 0) { throw 'Menu signing failed.' }
    }
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
