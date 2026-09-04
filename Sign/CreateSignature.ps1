<#
.SYNOPSIS
    Ensures the Profital code signing certificate exists, and wires its thumbprint into SignExe.ps1.

.DESCRIPTION
    This certificate is shared across all solutions built for this customer, so that a single
    thumbprint has to be whitelisted in their antivirus / trusted publishers.

    Re-running this script is safe: if a matching, still-valid certificate is already in
    Cert:\CurrentUser\My it is REUSED, and only Thumbprint.txt and SignExe.ps1 are refreshed.
    A new certificate is minted only when none exists (or -Force is given), because a new
    certificate means a new thumbprint that the customer would have to trust all over again.
#>

[CmdletBinding()]
param(
    # Mint a new certificate even if a usable one already exists. This CHANGES the thumbprint
    # and invalidates any existing antivirus / trusted publisher whitelisting.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$subject = 'CN=Thomas Jensen, O=Profital'
$codeSigningOid = '1.3.6.1.5.5.7.3.3'

$signDir = $PSScriptRoot
if (-not $signDir) { $signDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

# Look for an existing code signing certificate for this subject that is still valid and has a
# private key. Prefer the one that expires last.
$existing = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq $subject -and
        $_.HasPrivateKey -and
        $_.NotAfter -gt (Get-Date) -and
        ($_.EnhancedKeyUsageList.ObjectId -contains $codeSigningOid)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($existing -and -not $Force) {
    $cert = $existing
    Write-Host "Reusing existing certificate (no new certificate created)."
}
else {
    if ($existing -and $Force) {
        Write-Warning "-Force given: minting a NEW certificate. The thumbprint will change and must be re-trusted by the customer."
    }

    $cert = New-SelfSignedCertificate -CertStoreLocation cert:\currentuser\my `
    -Subject $subject `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -Provider "Microsoft Enhanced RSA and AES Cryptographic Provider" `
    -KeyExportPolicy NonExportable `
    -KeyUsage DigitalSignature `
    -Type CodeSigningCert `
    -NotAfter (get-date).AddYears(10)

    Write-Host "Created new certificate."
}

$thumbprint = $cert.Thumbprint

# Save the thumbprint next to the scripts.
Set-Content -Path (Join-Path $signDir 'Thumbprint.txt') -Value $thumbprint -Encoding ascii

# Export the public key so it can be imported into trust stores (and handed to the customer).
$cerPath = Join-Path $signDir 'EDIRouterCodeSigning.cer'
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null

# Patch the thumbprint into SignExe.ps1 so signing picks the right certificate.
$signExe = Join-Path $signDir 'SignExe.ps1'
if (Test-Path $signExe) {
    $lines = Get-Content -Path $signExe | ForEach-Object {
        if ($_ -match "^\s*\`$Thumbprint\s*=\s*'") { "`$Thumbprint = '$thumbprint'" } else { $_ }
    }
    Set-Content -Path $signExe -Value $lines -Encoding utf8
}

Write-Host "  Subject   : $($cert.Subject)"
Write-Host "  Thumbprint: $thumbprint"
Write-Host "  Expires   : $($cert.NotAfter)"
Write-Host "  Public key: $cerPath"
Write-Host ""
Write-Host "Trust the certificate locally (only needed once per machine/user):"
Write-Host "  certutil -user -addstore -f Root `"$cerPath`""
Write-Host "  Import-Certificate -FilePath `"$cerPath`" -CertStoreLocation Cert:\CurrentUser\TrustedPublisher"
