# Authenticode-sign a published GuildSyncCompanion.exe.
# Refuses a self-signed certificate. Windows will not treat a self-signed
# file as a known publisher, and this script will not invent one.
#
# Requires a code signing certificate bought in the publisher's legal name
# (OV or EV) from a CA that Microsoft trusts, and Windows signtool.exe
# from the Windows SDK. Set SIGN_PFX_PASSWORD in the environment.

param(
    [Parameter(Mandatory = $true)][string]$Pfx,
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Timestamp = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Pfx)) {
    throw "Certificate file not found: $Pfx"
}
if (-not (Test-Path $Exe)) {
    throw "Executable not found: $Exe"
}
if (-not $env:SIGN_PFX_PASSWORD) {
    throw "Set SIGN_PFX_PASSWORD to the certificate password."
}

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Pfx, $env:SIGN_PFX_PASSWORD)
if ($cert.Subject -eq $cert.Issuer) {
    throw "This certificate is self-signed. Buy an OV or EV code signing certificate issued to the guild's legal name. A self-signed file is not a trusted Windows publisher."
}
if ($cert.NotAfter -lt (Get-Date)) {
    throw "Certificate expired on $($cert.NotAfter)."
}

$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if (-not $signtool) {
    throw "signtool.exe from the Windows SDK is not on PATH. The jar signtool on Linux cannot Authenticode-sign an exe."
}

& $signtool.Source sign /fd SHA256 /tr $Timestamp /td SHA256 /f $Pfx /p $env:SIGN_PFX_PASSWORD $Exe
if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit $LASTEXITCODE" }
& $signtool.Source verify /pa $Exe
if ($LASTEXITCODE -ne 0) { throw "Signature did not verify against a trusted publisher." }
Write-Host "Signed $Exe as $($cert.Subject)"
