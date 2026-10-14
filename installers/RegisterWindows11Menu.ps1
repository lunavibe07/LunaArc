[CmdletBinding()]
param([switch]$Unregister, [switch]$TrustCertificate, [switch]$VerifyOnly, [switch]$VerifyOwnership, [switch]$ImportCertificateOnly, [string]$InstallationDirectory = '')
$ErrorActionPreference = 'Stop'
# Start-Process from PowerShell 7 can inherit incompatible PowerShell 7 modules.
# Resolve only the built-in Windows PowerShell modules for certificate/Appx operations.
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    $env:PSModulePath = @((Join-Path $PSHOME 'Modules'), (Join-Path $env:ProgramFiles 'WindowsPowerShell/Modules')) -join ';'
}
$packageName = 'LunaArc.ShellMenu'
if ([string]::IsNullOrWhiteSpace($InstallationDirectory)) { $InstallationDirectory = $PSScriptRoot }
function Get-NormalizedDirectory([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    if ($full.Length -eq $root.Length) { return $root }
    return $full.TrimEnd('\')
}
$directory = Get-NormalizedDirectory $InstallationDirectory
$ownershipKey = 'HKCU:/Software/LunaArc/ModernMenu'
try {
    if ($Unregister -or $VerifyOwnership) {
        $owner = Get-ItemProperty -LiteralPath $ownershipKey -ErrorAction SilentlyContinue
        if (-not $owner -or $owner.InstallationDirectory -ne $directory) {
            if ($VerifyOwnership) { throw 'The modern menu is not owned by this directory.' }
            exit 0
        }
        foreach ($package in @(Get-AppxPackage -Name $packageName)) {
            if ($package.PackageFullName -ne $owner.PackageFullName) { continue }
            $manager = [Windows.Management.Deployment.PackageManager,Windows.Management.Deployment,ContentType=WindowsRuntime]::new()
            $installed = $manager.FindPackageForUser('', $package.PackageFullName)
            if ((Get-NormalizedDirectory $installed.EffectiveExternalLocation.Path) -ne $directory) { continue }
            if ($VerifyOwnership) { Write-Output 'PASS: modern menu owner, package identity and effective external location match.'; exit 0 }
            Remove-AppxPackage -Package $package.PackageFullName
        }
        if ($VerifyOwnership) { throw 'The registered package does not match its ownership record.' }
        Remove-Item -LiteralPath $ownershipKey -Force
        Write-Output 'Removed the modern menu owned by this folder. Certificate trust is left unchanged.'
        exit 0
    }
    if (-not $VerifyOnly -and [Environment]::OSVersion.Version.Build -lt 22000) { throw 'The modern menu requires Windows 11. Use the classic LunaArc submenu instead.' }
    foreach ($file in @('LunaArc.exe','LunaArc.ContextMenu.dll','LunaArc.ShellMenu.msix','LunaArc.Menu.cer')) {
        if (-not (Test-Path -LiteralPath (Join-Path $directory $file))) { throw "Missing $file. Extract the entire portable package." }
    }
    $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2((Join-Path $directory 'LunaArc.Menu.cer'))
    # Check the package signer before trusting it. Windows validates its payload when registering.
    Add-Type -AssemblyName System.Security
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $directory 'LunaArc.ShellMenu.msix'))
    try {
        $entry = $zip.GetEntry('AppxSignature.p7x')
        if (-not $entry) { throw 'The menu package is unsigned.' }
        if ($entry.Length -lt 5 -or $entry.Length -gt 1048576) { throw 'Invalid menu signature size.' }
        $input = $entry.Open(); $memory = New-Object IO.MemoryStream
        try { $input.CopyTo($memory); $signature = $memory.ToArray() }
        finally { $input.Dispose(); $memory.Dispose() }
        if ([Text.Encoding]::ASCII.GetString($signature,0,4) -ne 'PKCX') { throw 'Invalid menu signature header.' }
        $cms = New-Object Security.Cryptography.Pkcs.SignedCms
        $cms.Decode([byte[]]$signature[4..($signature.Length-1)])
        $cms.CheckSignature($true)
        if ($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $certificate.Thumbprint) { throw 'Menu signer does not match the supplied certificate.' }
        $manifestStream = $zip.GetEntry('AppxManifest.xml').Open()
        $reader = New-Object IO.StreamReader($manifestStream)
        try { [xml]$manifest = $reader.ReadToEnd() }
        finally { $reader.Dispose(); $manifestStream.Dispose() }
        if ($manifest.Package.Identity.Name -ne $packageName -or $manifest.Package.Identity.Publisher -ne $certificate.Subject) { throw 'Unexpected menu package identity.' }
        $hashEntry = $zip.GetEntry('Assets/MenuDll.sha256')
        if (-not $hashEntry -or $hashEntry.Length -gt 256) { throw 'Missing native DLL hash pin.' }
        $hashStream = $hashEntry.Open()
        $hashReader = New-Object IO.StreamReader($hashStream)
        try { $expectedDllHash = $hashReader.ReadToEnd().Trim() }
        finally { $hashReader.Dispose(); $hashStream.Dispose() }
        $dllStream = [IO.File]::OpenRead((Join-Path $directory 'LunaArc.ContextMenu.dll'))
        $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
        try { $actualDllHash = [BitConverter]::ToString($hashAlgorithm.ComputeHash($dllStream)).Replace('-','') }
        finally { $hashAlgorithm.Dispose(); $dllStream.Dispose() }
        if ($expectedDllHash -notmatch '^[0-9a-fA-F]{64}$' -or $actualDllHash -ne $expectedDllHash) {
            throw 'The native menu DLL does not match the hash in its signed package.'
        }
    } finally { $zip.Dispose() }
    # Chain trust is a separate user decision: pin the signer and full signed-DLL bytes.
    # Add-AppxPackage must authenticate the MSIX payload (including the hash pin) before activation.
    $dllCertificate = [Security.Cryptography.X509Certificates.X509Certificate]::CreateFromSignedFile((Join-Path $directory 'LunaArc.ContextMenu.dll'))
    try {
        if ($dllCertificate.GetCertHashString() -ne $certificate.Thumbprint) { throw 'Native DLL and package signers differ.' }
    } finally { $dllCertificate.Dispose() }
    if ($VerifyOnly) { Write-Output 'PASS: package signer/identity and native DLL hash/signer pins checked without installing trust or registering Explorer.'; exit 0 }
    $trustPath = 'Cert:/LocalMachine/TrustedPeople/' + $certificate.Thumbprint
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($ImportCertificateOnly) {
        if (-not $TrustCertificate -or -not $isAdministrator) { throw 'Certificate import requires explicit consent and administrator permission.' }
        Import-Certificate -FilePath (Join-Path $directory 'LunaArc.Menu.cer') -CertStoreLocation 'Cert:/LocalMachine/TrustedPeople' | Out-Null
        Write-Output 'Imported the approved menu certificate into Windows Trusted People. No package was registered by the elevated helper.'
        exit 0
    }
    $chain = New-Object Security.Cryptography.X509Certificates.X509Chain
    try { $alreadyTrusted = $chain.Build($certificate) }
    finally { $chain.Dispose() }
    if (-not $alreadyTrusted -and -not (Test-Path -LiteralPath $trustPath)) {
        if (-not $TrustCertificate) {
            Write-Host "Publisher: $($certificate.Subject)"
            Write-Host "Certificate: $($certificate.Thumbprint)"
            Write-Host 'This menu preview uses a signing certificate that Windows does not already trust.'
            Write-Host 'Enabling it adds this certificate to Windows Trusted People (all users).'
            Write-Host 'Windows may ask for administrator permission for this certificate step only.'
            if ((Read-Host 'Trust this certificate and install the Windows 11 menu? Type YES to continue') -cne 'YES') { exit 2 }
        }
        if ($isAdministrator) {
            Import-Certificate -FilePath (Join-Path $directory 'LunaArc.Menu.cer') -CertStoreLocation 'Cert:/LocalMachine/TrustedPeople' | Out-Null
        } else {
            $helperArguments = @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
                ('"' + $PSCommandPath + '"'),'-ImportCertificateOnly','-TrustCertificate','-InstallationDirectory',('"' + $directory + '"'))
            $helper = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $helperArguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
            if ($helper.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $trustPath)) { throw 'Certificate permission was declined or the import failed.' }
        }
    }
    $previous = Get-AppxPackage -Name $packageName | Select-Object -First 1
    $backup = $null
    try {
        # Windows requires removing an equal-version sparse registration when changing its external folder.
        if ($previous -and [string]$previous.Version -eq [string]$manifest.Package.Identity.Version) {
            $manager = [Windows.Management.Deployment.PackageManager,Windows.Management.Deployment,ContentType=WindowsRuntime]::new()
            $previousDirectory = $manager.FindPackageForUser('', $previous.PackageFullName).EffectiveExternalLocation.Path
            $previousSource = Join-Path $previousDirectory 'LunaArc.ShellMenu.msix'
            if (-not (Test-Path -LiteralPath $previousSource)) { throw 'The active menu folder was moved. Unregister it before enabling a different copy.' }
            $backup = Join-Path ([IO.Path]::GetTempPath()) ('LunaArc-menu-backup-' + [guid]::NewGuid().ToString('N') + '.msix')
            Copy-Item -LiteralPath $previousSource -Destination $backup
            Remove-AppxPackage -Package $previous.PackageFullName
        }
        try {
            Add-AppxPackage -Path (Join-Path $directory 'LunaArc.ShellMenu.msix') -ExternalLocation $directory
        } catch {
            if ($backup) {
                try { Add-AppxPackage -Path $backup -ExternalLocation $previousDirectory }
                catch { Write-Warning 'The previous modern menu could not be restored. Run its InstallWindows11Menu.cmd again.' }
            }
            throw
        }
    } finally { if ($backup -and (Test-Path -LiteralPath $backup)) { Remove-Item -LiteralPath $backup -Force } }
    $package = Get-AppxPackage -Name $packageName
    if (-not $package) { throw 'Windows did not register the menu package.' }
    New-Item -Path $ownershipKey -Force | Out-Null
    Set-ItemProperty -LiteralPath $ownershipKey -Name InstallationDirectory -Value $directory
    Set-ItemProperty -LiteralPath $ownershipKey -Name PackageFullName -Value $package.PackageFullName
    Set-ItemProperty -LiteralPath $ownershipKey -Name CertificateThumbprint -Value $certificate.Thumbprint
    Write-Output 'LunaArc is registered for the Windows 11 menu. Reopen Explorer windows or sign out if Windows has cached the old menu.'
} catch {
    Write-Error $_
    exit 1
}
