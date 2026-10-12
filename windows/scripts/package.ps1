<#
  Builds Outatime for Windows: a portable Outatime.exe per architecture, and the MSIX bundle the Microsoft Store takes.
  Needs the .NET 10 SDK and the Windows SDK (makeappx.exe, signtool.exe). Run from windows\:

    .\scripts\package.ps1                 # x64 + arm64 → artifacts\
    .\scripts\package.ps1 -Sign           # also sign with a local test certificate, to install the .msix by hand
#>
param(
    [string[]] $Architectures = @('x64', 'arm64'),
    [switch] $Sign
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item $artifacts -ItemType Directory | Out-Null

# Version: Directory.Build.props, as the MSIX's four parts (the Store wants the last one 0).
$props = [xml](Get-Content (Join-Path $root 'Directory.Build.props'))
$version = "$($props.Project.PropertyGroup.Version).0"

$kits = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $kits) { throw 'makeappx.exe not found: install the Windows 10/11 SDK.' }
$makeappx = $kits.FullName
$signtool = Join-Path $kits.DirectoryName 'signtool.exe'

$packages = @()
foreach ($arch in $Architectures) {
    $rid = "win-$arch"
    $out = Join-Path $artifacts "publish-$arch"
    dotnet publish (Join-Path $root 'src\Outatime') -c Release -r $rid --self-contained -o $out -p:PublishReadyToRun=true
    if ($LASTEXITCODE) { throw "publish $rid failed" }

    # Portable: one self-extracting exe, for testers and anyone who'd rather not use the Store.
    $single = Join-Path $artifacts "single-$arch"
    dotnet publish (Join-Path $root 'src\Outatime') -c Release -r $rid --self-contained -o $single `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none
    if ($LASTEXITCODE) { throw "single-file $rid failed" }
    Compress-Archive -Path (Join-Path $single 'Outatime.exe') -DestinationPath (Join-Path $artifacts "Outatime-$version-$arch-portable.zip")

    # MSIX layout: the published app, the manifest and the tile images.
    Copy-Item (Join-Path $root 'package\Images') (Join-Path $out 'Images') -Recurse
    (Get-Content (Join-Path $root 'package\AppxManifest.xml') -Raw).Replace('$VERSION$', $version).Replace('$ARCH$', $arch) |
        Set-Content (Join-Path $out 'AppxManifest.xml') -Encoding utf8
    $msix = Join-Path $artifacts "Outatime_$($version)_$arch.msix"
    & $makeappx pack /o /d $out /p $msix
    if ($LASTEXITCODE) { throw "makeappx pack $arch failed" }
    $packages += $msix
}

# One bundle for the Store: it serves each PC its architecture.
$bundleDir = Join-Path $artifacts 'bundle'
New-Item $bundleDir -ItemType Directory | Out-Null
$packages | ForEach-Object { Copy-Item $_ $bundleDir }
$bundle = Join-Path $artifacts "Outatime_$version.msixbundle"
& $makeappx bundle /o /d $bundleDir /p $bundle /bv $version
if ($LASTEXITCODE) { throw 'makeappx bundle failed' }
Remove-Item $bundleDir -Recurse

if ($Sign) {
    # A self-signed certificate whose subject matches the manifest's Publisher. Windows installs the bundle only
    # once the certificate is trusted, which Install.ps1 does.
    $publisher = ([xml](Get-Content (Join-Path $root 'package\AppxManifest.xml'))).Package.Identity.Publisher
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq $publisher | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature -FriendlyName 'Outatime test' `
            -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    Export-Certificate -Cert $cert -FilePath (Join-Path $artifacts 'Outatime-test.cer') | Out-Null
    foreach ($p in $packages + $bundle) {
        & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $p
        if ($LASTEXITCODE) { throw "signing $p failed" }
    }
    # Testers double-click Install.cmd: it trusts the certificate, installs the bundle and opens the app.
    Copy-Item (Join-Path $root 'package\Install.ps1'), (Join-Path $root 'package\Install.cmd') $artifacts
}

Get-ChildItem $artifacts -File | Format-Table Name, Length
