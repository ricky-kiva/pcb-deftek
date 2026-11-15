[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$FilePath,
    [string]$PfxPath,
    [AllowEmptyString()][string]$PfxPassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$SignToolPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-SignTool {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath)) {
            throw "signtool.exe was not found at '$ExplicitPath'."
        }

        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    if (-not [string]::IsNullOrWhiteSpace($env:SIGNTOOL_PATH) -and (Test-Path -LiteralPath $env:SIGNTOOL_PATH)) {
        return (Resolve-Path -LiteralPath $env:SIGNTOOL_PATH).Path
    }

    $command = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitsRoot) {
        $candidates = Get-ChildItem -LiteralPath $kitsRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                $candidate = Join-Path $_.FullName 'x64\signtool.exe'
                if (Test-Path -LiteralPath $candidate) {
                    [pscustomobject]@{ Version = $_.Name; Path = $candidate }
                }
            } |
            Sort-Object -Property Version -Descending

        if ($candidates) {
            return $candidates[0].Path
        }
    }

    throw 'signtool.exe was not found. Install the Windows SDK or pass -SignToolPath.'
}

$timestampUri = $null
if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
    $timestampUri.Scheme -notin @('http', 'https')) {
    throw "Timestamp URL must be an absolute http(s) URL: '$TimestampUrl'."
}

$temporaryPfx = $null
try {
    if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
        if (-not (Test-Path -LiteralPath $PfxPath)) {
            throw "PFX file was not found at '$PfxPath'."
        }

        $resolvedPfx = (Resolve-Path -LiteralPath $PfxPath).Path
        $resolvedPassword = if ($PSBoundParameters.ContainsKey('PfxPassword')) { $PfxPassword } else { $env:SIGNING_PFX_PASSWORD }
    }
    else {
        if ([string]::IsNullOrWhiteSpace($env:SIGNING_PFX_BASE64)) {
            throw 'No certificate supplied. Set SIGNING_PFX_BASE64 (with SIGNING_PFX_PASSWORD) or pass -PfxPath.'
        }

        $resolvedPassword = if ($PSBoundParameters.ContainsKey('PfxPassword')) { $PfxPassword } else { $env:SIGNING_PFX_PASSWORD }
        if ([string]::IsNullOrWhiteSpace($resolvedPassword)) {
            throw 'SIGNING_PFX_PASSWORD is not set; the certificate cannot be opened.'
        }

        $base64 = $env:SIGNING_PFX_BASE64 -replace '\s', ''
        try {
            $pfxBytes = [Convert]::FromBase64String($base64)
        }
        catch {
            throw 'SIGNING_PFX_BASE64 is not valid Base64. Store the PFX as a single Base64 string.'
        }

        $tempRoot = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
        $temporaryPfx = Join-Path $tempRoot ("pcb-deftek-sign-{0}.pfx" -f [Guid]::NewGuid().ToString('N'))
        [IO.File]::WriteAllBytes($temporaryPfx, $pfxBytes)

        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        & icacls $temporaryPfx /inheritance:r /grant:r "$identity`:(M)" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to restrict permissions on the decoded certificate file.'
        }

        $resolvedPfx = $temporaryPfx
    }

    if ([string]::IsNullOrWhiteSpace($resolvedPassword)) {
        throw 'No certificate password was supplied. Set SIGNING_PFX_PASSWORD or pass -PfxPassword.'
    }

    $signTool = Resolve-SignTool -ExplicitPath $SignToolPath

    try {
        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $resolvedPfx,
            $resolvedPassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    }
    catch [System.Security.Cryptography.CryptographicException] {
        throw 'The certificate could not be opened. Check that SIGNING_PFX_PASSWORD matches the PFX and that the PFX is valid.'
    }

    try {
        if (-not $certificate.HasPrivateKey) {
            throw 'The certificate has no private key and cannot sign.'
        }

        $now = [DateTime]::UtcNow
        if ($now -lt $certificate.NotBefore.ToUniversalTime() -or $now -gt $certificate.NotAfter.ToUniversalTime()) {
            throw "The certificate is not valid now (valid $($certificate.NotBefore.ToString('u')) to $($certificate.NotAfter.ToString('u')))."
        }

        $codeSigningOid = '1.3.6.1.5.5.7.3.3'
        $ekuExtension = $certificate.Extensions |
            Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
            Select-Object -First 1
        $usages = @()
        if ($ekuExtension) {
            $eku = [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($ekuExtension, $true)
            foreach ($oid in $eku.EnhancedKeyUsages) {
                $usages += $oid.Value
            }
        }
        if ($usages.Count -gt 0 -and $usages -notcontains $codeSigningOid) {
            throw "The certificate is not valid for code signing (EKU: $($usages -join ', '))."
        }

        Write-Host ("Certificate: {0} | valid until {1:u} | thumbprint {2}" -f
            $certificate.Subject, $certificate.NotAfter.ToUniversalTime(), $certificate.Thumbprint)
    }
    finally {
        $certificate.Dispose()
    }

    foreach ($file in $FilePath) {
        if (-not (Test-Path -LiteralPath $file)) {
            throw "File to sign was not found: '$file'."
        }

        $resolvedFile = (Resolve-Path -LiteralPath $file).Path

        & $signTool sign /fd SHA256 /f $resolvedPfx /p $resolvedPassword /tr $TimestampUrl /td SHA256 /v $resolvedFile
        if ($LASTEXITCODE -ne 0) {
            throw "Signing failed for '$resolvedFile' (signtool exit code $LASTEXITCODE). Artifacts must not be published unsigned."
        }

        & $signTool verify /pa /all $resolvedFile
        if ($LASTEXITCODE -ne 0) {
            throw "Signature verification failed for '$resolvedFile' (signtool exit code $LASTEXITCODE)."
        }

        $signature = Get-AuthenticodeSignature -LiteralPath $resolvedFile
        if ($signature.Status -ne 'Valid') {
            throw "Signature status for '$resolvedFile' is '$($signature.Status)', expected 'Valid'."
        }

        if ($null -eq $signature.TimeStamperCertificate) {
            throw "No RFC 3161 timestamp was attached to '$resolvedFile'."
        }

        Write-Host ("Signed: {0} | publisher: {1} | timestamp: {2:u}" -f
            $resolvedFile, $signature.SignerCertificate.Subject, $signature.TimeStamperCertificate.NotAfter.ToUniversalTime())
    }
}
finally {
    if ($temporaryPfx -and (Test-Path -LiteralPath $temporaryPfx)) {
        Remove-Item -LiteralPath $temporaryPfx -Force -ErrorAction SilentlyContinue
    }
}
