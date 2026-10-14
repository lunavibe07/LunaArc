[CmdletBinding()]
param([switch]$Unregister, [switch]$TrustCertificate, [switch]$VerifyOnly, [string]$InstallationDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$packageName = 'LunaArc.ShellMenu'
$directory = [IO.Path]::GetFullPath($InstallationDirectory).TrimEnd('\')
$ownershipKey = 'HKCU:/Software/LunaArc/ModernMenu'
try {
    if ($Unregister) {
        $owner = Get-ItemProperty -LiteralPath $ownershipKey -ErrorAction SilentlyContinue
        if (-not $owner -or $owner.InstallationDirectory -ne $directory) { exit 0 }
        foreach ($package in @(Get-AppxPackage -Name $packageName)) {
            if ($package.PackageFullName -ne $owner.PackageFullName) { continue }
            $manager = [Windows.Management.Deployment.PackageManager,Windows.Management.Deployment,ContentType=WindowsRuntime]::new()
            $installed = $manager.FindPackageForUser('', $package.PackageFullName)
            if ($installed.EffectiveExternalLocation.Path.TrimEnd('\') -ne $directory) { continue }
            Remove-AppxPackage -Package $package.PackageFullName
        }
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
    } finally { $zip.Dispose() }
    $dllSignature = Get-AuthenticodeSignature -LiteralPath (Join-Path $directory 'LunaArc.ContextMenu.dll')
    if ($dllSignature.Status -notin @('Valid','NotTrusted') -or $dllSignature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw 'The native menu DLL signature did not validate.' }
    if ($VerifyOnly) { Write-Output 'PASS: MSIX signer, identity and native DLL signature verified without installing certificate trust or registering Explorer.'; exit 0 }
    if (-not (Test-Path -LiteralPath ('Cert:/CurrentUser/TrustedPeople/' + $certificate.Thumbprint))) {
        if (-not $TrustCertificate) {
            Write-Host "Publisher: $($certificate.Subject)"
            Write-Host "Certificate: $($certificate.Thumbprint)"
            Write-Host 'This menu preview uses a signing certificate that Windows does not already trust.'
            Write-Host 'Enabling it adds the certificate to Trusted People for your Windows account.'
            if ((Read-Host 'Trust this certificate and install the Windows 11 menu? Type YES to continue') -cne 'YES') { exit 2 }
        }
        Import-Certificate -FilePath (Join-Path $directory 'LunaArc.Menu.cer') -CertStoreLocation 'Cert:/CurrentUser/TrustedPeople' | Out-Null
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
