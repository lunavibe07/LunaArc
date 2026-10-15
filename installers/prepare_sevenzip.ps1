[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory, [ValidateSet('win-x64','linux-x64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$cache = Join-Path (Split-Path -Parent $PSScriptRoot) '.tools/sevenzip-26.04'
New-Item -ItemType Directory -Force -Path $cache, $OutputDirectory | Out-Null
if ($Runtime -eq 'linux-x64') {
    $archive = Join-Path $cache '7z2604-linux-x64.tar.xz'
    $expectedLinux = 'fc0327ba27e89bd086cf426dff17d77de582953cdbbc10a6576540a06853ffcd'
    if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedLinux) {
        Invoke-WebRequest 'https://github.com/ip7z/7zip/releases/download/26.04/7z2604-linux-x64.tar.xz' -OutFile $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedLinux) { throw '7-Zip Linux checksum mismatch.' }
    & tar -xJf $archive -C $cache 7zz
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the Linux encoder.' }
    Copy-Item -LiteralPath (Join-Path $cache '7zz') -Destination (Join-Path $OutputDirectory 'LunaArc.7zz')
    if ($PSVersionTable.Platform -eq 'Unix') { & chmod +x (Join-Path $OutputDirectory 'LunaArc.7zz') }
} else {
$encoder = Join-Path $cache '7zr.exe'
$expected = '256feca8e274e5da655e2a284fabafd9f554365eb164862089dacd4e8276d282'
if (-not (Test-Path -LiteralPath $encoder) -or (Get-FileHash -LiteralPath $encoder -Algorithm SHA256).Hash -ne $expected) {
    Invoke-WebRequest 'https://github.com/ip7z/7zip/releases/download/26.04/7zr.exe' -OutFile $encoder
}
if ((Get-FileHash -LiteralPath $encoder -Algorithm SHA256).Hash -ne $expected) { throw '7-Zip encoder checksum mismatch.' }
Copy-Item -LiteralPath $encoder -Destination (Join-Path $OutputDirectory 'LunaArc.7zr.exe')
}
foreach ($notice in @('SevenZip_LICENSE.txt','SevenZip_COPYING.txt','SevenZip_NOTICE.txt')) {
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) "branding/$notice") -Destination $OutputDirectory
}
