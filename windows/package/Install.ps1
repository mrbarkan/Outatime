<#
  Installs a test build of Outatime by hand: trusts the test certificate that signed it (Windows asks for
  admin once), then installs the bundle for the current user and opens the app. Store builds don't need this.
  Keep it next to Outatime_<version>.msixbundle and Outatime-test.cer; Install.cmd runs it with a double-click.
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$cer = Join-Path $here 'Outatime-test.cer'
$bundle = Get-ChildItem $here -Filter '*.msixbundle' | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not (Test-Path $cer) -or -not $bundle) { throw "Put Install.ps1 next to Outatime-test.cer and the .msixbundle." }
Get-ChildItem $here -File | Unblock-File

# Windows only installs a package whose signature it can trace to a certificate it trusts (else 0x800B010A).
$thumbprint = [Security.Cryptography.X509Certificates.X509Certificate2]::new($cer).Thumbprint
$trusted = "Cert:\LocalMachine\TrustedPeople\$thumbprint"
if (-not (Test-Path $trusted)) {
    Write-Host 'Trusting the Outatime test certificate (Windows asks for permission)...'
    $import = "Import-Certificate -FilePath '$($cer.Replace("'", "''"))' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    $admin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($admin) { Invoke-Expression $import }
    else { Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $import }
    if (-not (Test-Path $trusted)) { throw 'The certificate was not trusted, so Windows will refuse the package.' }
}

Write-Host "Installing $($bundle.Name)..."
Add-AppxPackage -Path $bundle.FullName -ForceUpdateFromAnyVersion
$package = Get-AppxPackage -Name 'DavidBarkan.Outatime'
if (-not $package) { throw 'Outatime did not install.' }
Write-Host "Installed Outatime $($package.Version). It lives in the notification area, next to the clock."
if (-not $env:OUTATIME_NO_LAUNCH) { Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!Outatime" }
