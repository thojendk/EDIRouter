# Signs an executable with the shared code signing certificate created by CreateSignature.ps1.
# The thumbprint below is written by that script.
#
# Invoked automatically after publish by the SignPublishedExe target in Sign.targets, which
# passes -Path. Run it by hand with an explicit path:
#
#     .\Sign\SignExe.ps1 -Path .\publish\MyApp.exe

param(
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$Thumbprint = 'B66EBE0327FE11FA42AC4F95C121D952CE419E64'

# A PSModulePath inherited from a different PowerShell edition hides this host's own modules, so
# Microsoft.PowerShell.Security never autoloads and both the Cert:\ drive and
# Set-AuthenticodeSignature silently go missing. Reset the path so autoload can find them.
if (-not (Get-Command Set-AuthenticodeSignature -ErrorAction SilentlyContinue)) {
    $env:PSModulePath = Join-Path $PSHOME 'Modules'
}

if (-not $Path) {
    throw "No executable given. Pass -Path <exe>; the SignPublishedExe target supplies it automatically."
}
$Path = (Resolve-Path -Path $Path).Path

# Look the certificate up through the .NET store API rather than the Cert:\ PSDrive, which
# depends on the provider above having loaded.
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store('My', 'CurrentUser')
$store.Open('ReadOnly')
try {
    $cert = $store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1
}
finally {
    $store.Close()
}

if (-not $cert) {
    throw "Code signing certificate $Thumbprint not found in CurrentUser\My. Run CreateSignature.ps1 first."
}

$result = Set-AuthenticodeSignature $Path -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'

Write-Host "Signed : $Path"
Write-Host "Status : $($result.Status) - $($result.StatusMessage)"
if ($result.Status -ne 'Valid') { throw "Signing failed: $($result.Status) - $($result.StatusMessage)" }
