<#
.SYNOPSIS
    Generate self-signed certificate for DrawingNumber HTTPS endpoint and
    export to PFX (which Kestrel loads from appsettings.Production.json:Kestrel:Endpoints:Https:Certificate).

.DESCRIPTION
    - SAN includes: IP 140.231.237.180 (the actual address browsers will hit)
                    DNS localhost  (for same-box smoke test)
                    DNS SE-5CG4021HTJ (machine name; local path-only access)
    - Stored to <install-dir>\certs\server.pfx.
    - Removed from LocalMachine\My after export (only PFX on disk).
    - Password comes from $Password parameter (default matches appsettings.Production.json).

.PARAMETER InstallDir
    Application install directory. Default: D:\Applications\MXB\DrawingNumber\web_publish
    Must be the same path used in install-windows-service.ps1 and where publish-server.ps1 deploys.
    (项目约定的标准部署根目录;与 install-windows-service.ps1 保持一致。)

.PARAMETER Password
    PFX password. Must match Certificate:Password in appsettings.Production.json.
    Default: 'Pfx@2026!TCID#K7m9xQpLwRtY'.

.EXAMPLE
    PS> .\generate-cert.ps1
    PS> .\generate-cert.ps1 -InstallDir 'D:\DrawingNumber' -Password 'NewP@ssw0rd!2026'

.NOTES
    Run PowerShell AS ADMIN (LocalMachine\My write).
    After running, the PFX is the only copy — back it up before re-running.
#>

[CmdletBinding()]
param(
    [string]$InstallDir = 'D:\Applications\MXB\DrawingNumber\web_publish',
    [string]$Password   = 'Pfx@2026!TCID#K7m9xQpLwRtY'
)

$ErrorActionPreference = 'Stop'

# === Configuration (locked-in) =====================================================
$CertSubject   = 'CN=140.231.237.180'
$CertSAN_IP    = '140.231.237.180'
$CertSAN_DNS   = @('localhost', 'SE-5CG4021HTJ')
$CertFriendly  = 'DrawingNumber-HTTPS'
$ValidYears    = 5
$KeyLength     = 2048
# (2026-07-23 P0-3)HTTPS PFX 移到独立 certs/ 目录,与 DataProtection 用的 keys/ 隔离:
#   - keys/  = DataProtection 密钥 (ASP.NET Core 自动生成 key-<guid>.xml)
#   - certs/ = HTTPS 服务器证书 PFX (本脚本生成)
$CertsDir      = Join-Path $InstallDir 'certs'
$PfxPath       = Join-Path $CertsDir 'server.pfx'
# ============================================================================

Write-Host ">>> DrawingNumber cert generator" -ForegroundColor Cyan
Write-Host "    InstallDir : $InstallDir"
Write-Host "    PFX target : $PfxPath"
Write-Host "    Subject    : $CertSubject"
Write-Host "    SAN        : IP:$CertSAN_IP, DNS:$($CertSAN_DNS -join ', DNS:')"
Write-Host ""

# Pre-flight
if (-not (Test-Path $CertsDir)) {
    New-Item -ItemType Directory -Path $CertsDir -Force | Out-Null
    Write-Host "  + Created certs folder: $CertsDir"
}

# Refuse to silently overwrite — old PFX may have an old password
if (Test-Path $PfxPath) {
    Write-Host "  ! PFX already exists at $PfxPath" -ForegroundColor Yellow
    $ans = Read-Host "    Overwrite? (yes/no)"
    if ($ans -ne 'yes') {
        Write-Host "    Aborted. Existing PFX kept." -ForegroundColor Yellow
        exit 0
    }
    Remove-Item $PfxPath -Force
}

# Generate the cert
# Note: New-SelfSignedCertificate on Win10+ / Server 2016+ accepts IP literals in
# -DnsName and emits them as IPAddress-form SAN entries. The DNS-form entries
# (localhost / SE-5CG4021HTJ) are also added. Browsers match against the typed
# address bar — for "https://140.231.237.180:8373" they hit the IP SAN directly.
try {
    $DnsAll = @($CertSAN_IP) + $CertSAN_DNS

    $Cert = New-SelfSignedCertificate `
        -Subject         $CertSubject `
        -DnsName         $DnsAll `
        -CertStoreLocation 'Cert:\LocalMachine\My' `
        -NotAfter        (Get-Date).AddYears($ValidYears) `
        -KeyAlgorithm    RSA -KeyLength $KeyLength `
        -KeyUsage        DigitalSignature,KeyEncipherment `
        -FriendlyName    $CertFriendly

    Write-Host "  + Cert created in LocalMachine\My (Thumbprint: $($Cert.Thumbprint))" -ForegroundColor Green
} catch {
    Write-Error "Cert creation failed: $_"
    exit 1
}

# Export to PFX
try {
    $SecurePwd = ConvertTo-SecureString -String $Password -Force -AsPlainText
    Export-PfxCertificate -Cert $Cert -FilePath $PfxPath -Password $SecurePwd -ErrorAction Stop
    Write-Host "  + PFX exported: $PfxPath" -ForegroundColor Green
} catch {
    Write-Error "PFX export failed: $_"
    exit 1
}

# Remove the cert from LocalMachine\My — only PFX on disk remains
try {
    Remove-Item "Cert:\LocalMachine\My\$($Cert.Thumbprint)" -Force -ErrorAction Stop
    Write-Host "  + Removed cert from LocalMachine\My (only PFX remains)" -ForegroundColor Green
} catch {
    Write-Warning "Could not remove cert from LocalMachine\My: $_"
    Write-Warning "Manually run: Remove-Item 'Cert:\LocalMachine\My\$($Cert.Thumbprint)'"
}

# ACL — make sure the EXE's service account (LocalSystem) can read the PFX
icacls $PfxPath /inheritance:r /grant:r "SYSTEM:(R)" "Administrators:(R)" | Out-Null
Write-Host "  + PFX ACL set (SYSTEM / Administrators read-only)" -ForegroundColor Green

Write-Host ""
Write-Host "DONE. Next steps:" -ForegroundColor Cyan
Write-Host "  1) Verify appsettings.Production.json:Kestrel:Endpoints:Https:Certificate:Password matches '$Password'"
Write-Host "  2) Run .\install-windows-service.ps1"
Write-Host "  3) Backup the PFX to a secure location — there is only one copy."
