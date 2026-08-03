<#
.SYNOPSIS
    Register DrawingNumber as a Windows Service (auto-start, no console window,
    self-recovering on crash).

.DESCRIPTION
    - Runs the EXE as a service under LocalSystem
    - Opens firewall port 8373
    - Sets ASPNETCORE_ENVIRONMENT=Production (so appsettings.Production.json is loaded)
    - Sets DataProtection keys dir
    - Sets service recovery: restart on crash with backoff
    - Start Mode = auto (auto-start on boot)

.PARAMETER InstallDir
    Directory containing BootstrapBlazor.OnlyServer1.exe + appsettings*.json + certs\.
    Default: D:\Applications\MXB\DrawingNumber\web_publish
    (项目约定的标准部署根目录。IT 若使用带日期戳的发布目录(如 web_publish_20260709),
     请用 -InstallDir 显式覆盖,或在解压后将目录重命名/链接到 web_publish。)

.PARAMETER Port
    HTTPS port (must match Kestrel:Endpoints:Https:Url in appsettings.Production.json).
    Default: 8373

.EXAMPLE
    PS> .\install-windows-service.ps1
    PS> .\install-windows-service.ps1 -InstallDir 'D:\Applications\MXB\DrawingNumber\web_publish_20260801' -Port 8443

.NOTES
    Must run as Administrator. Set-ExecutionPolicy may need RemoteSigned.
#>

[CmdletBinding()]
param(
    [string]$InstallDir = 'D:\Applications\MXB\DrawingNumber\web_publish',
    [int]   $Port       = 8373
)

$ErrorActionPreference = 'Stop'

# === Configuration (locked-in) =====================================================
$ServiceName  = 'DrawingNumber'
$DisplayName  = 'DrawingNumber Internal System'
$ExeName      = 'BootstrapBlazor.OnlyServer1.exe'
$ExePath      = Join-Path $InstallDir $ExeName
# (2026-07-23 P0-3)DataProtection 密钥 (keys/) 与 HTTPS PFX (certs/) 物理隔离:
#   - keys/  = DP 密钥:服务只读,管理员只读 (运行时不可篡改)
#   - certs/ = HTTPS PFX:服务只读,管理员可改 (证书轮换)
$KeysDir      = Join-Path $InstallDir 'keys'
$CertsDir     = Join-Path $InstallDir 'certs'
$LogsDir      = Join-Path $InstallDir 'logs'
# ============================================================================

if (-not (Test-Path $ExePath)) {
    Write-Error "EXE not found: $ExePath. Run publish-server.ps1 first and copy to $InstallDir."
    exit 1
}

# Validate prerequisites
Write-Host ">>> Pre-flight checks" -ForegroundColor Cyan
@($InstallDir, $KeysDir, $CertsDir, $LogsDir) | ForEach-Object {
    if (Test-Path $_) {
        Write-Host "  + $_ exists"
    } else {
        New-Item -ItemType Directory -Path $_ -Force | Out-Null
        Write-Host "  + $_ created"
    }
}

if (-not (Test-Path (Join-Path $CertsDir 'server.pfx'))) {
    Write-Warning "PFX not found at $(Join-Path $CertsDir 'server.pfx')"
    Write-Warning "Run generate-cert.ps1 first, or copy PFX manually to $CertsDir."
}

# (2026-07-23 P0-3)ACL 收紧:keys/ 和 certs/ 分别设置,不允许运行时篡改
# - keys/  DP 密钥:服务+管理员都只读,杜绝运行时篡改 (丢/改 = 全员被踢)
# - certs/ HTTPS PFX:服务只读,管理员可改 (证书轮换需要)
icacls $KeysDir  /inheritance:r /grant:r "SYSTEM:(OI)(CI)R"           "Administrators:(OI)(CI)R"           | Out-Null
Write-Host "  + ACLs set on $KeysDir (SYSTEM/Admins read-only)"
icacls $CertsDir /inheritance:r /grant:r "SYSTEM:(OI)(CI)R"           "Administrators:(OI)(CI)M"           | Out-Null
Write-Host "  + ACLs set on $CertsDir (SYSTEM read, Admins modify)"
# logs/ 仍需完全控制 (NLog 写日志)
icacls $LogsDir  /inheritance:r /grant:r "SYSTEM:(OI)(CI)F"           "Administrators:(OI)(CI)F"           | Out-Null
Write-Host "  + ACLs set on $LogsDir (SYSTEM/Admins full)"

# Remove existing service if present (idempotent re-runs)
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host ""
    Write-Host "  ! Service '$ServiceName' already exists. Stopping & removing..." -ForegroundColor Yellow
    sc.exe stop    $ServiceName | Out-Null
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# sc.exe create
Write-Host ""
Write-Host ">>> Registering Windows Service '$ServiceName'" -ForegroundColor Cyan

# IMPORTANT: spaces in sc.exe arguments need explicit escaping. Use cmd-style `binPath= "..."`.
# ref: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-create
$binPathArg = 'binPath= "' + $ExePath + '"'

sc.exe create $ServiceName $binPathArg DisplayName= "$DisplayName" start= auto obj= "LocalSystem" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "sc create failed (exit $LASTEXITCODE)"; exit 1 }
Write-Host "  + Service registered (LocalSystem, start=auto)"

# Failure recovery: restart on crash, 0/30/60s backoff
sc.exe failure $ServiceName reset= 60 actions= restart/0/restart/30000/restart/60000 | Out-Null
Write-Host "  + Failure recovery: restart on crash (0s / 30s / 60s backoff)"

# Per-service env vars: ASPNETCORE_ENVIRONMENT=Production -> appsettings.Production.json is loaded
# (HKLM\SYSTEM\CurrentControlSet\Services\<name>\Parameters)
Write-Host "  + Setting ASPNETCORE_ENVIRONMENT=Production"
[Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production", "Machine")
# Per-service override using reg add (more reliable than Machine env when service runs in session 0)
reg add "HKLM\SYSTEM\CurrentControlSet\Services\$ServiceName" /v Environment /t REG_MULTI_SZ /d "ASPNETCORE_ENVIRONMENT=Production`0" /f | Out-Null

# Firewall rule
Write-Host ""
Write-Host ">>> Opening firewall port $Port" -ForegroundColor Cyan
$ruleName = "DrawingNumber-HTTPS-$Port"
$existingFw = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existingFw) {
    Write-Host "  ! Firewall rule '$ruleName' already exists, removing and re-adding"
    Remove-NetFirewallRule -DisplayName $ruleName
}
New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
Write-Host "  + Inbound TCP $Port allowed"

# Start the service
Write-Host ""
Write-Host ">>> Starting service" -ForegroundColor Cyan
sc.exe start $ServiceName | Out-Null
Start-Sleep -Seconds 3
$svc = Get-Service -Name $ServiceName
Write-Host "  Status : $($svc.Status)"
Write-Host "  CanStop: $($svc.CanStop)"

if ($svc.Status -ne 'Running') {
    Write-Warning "Service did not reach Running state."
    Write-Warning "Check Windows Event Viewer -> Application for ASP.NET Core / DrawingNumber errors."
    Write-Warning "Or run the EXE manually as a console app to see startup output:"
    Write-Warning "  $ExePath"
    exit 2
}

Write-Host ""
Write-Host "DONE. Service '$ServiceName' is running." -ForegroundColor Green
Write-Host ""
Write-Host "Test from a browser:" -ForegroundColor Cyan
Write-Host "  https://140.231.237.180:$Port"
Write-Host ""
Write-Host "First login: WorkerNumber=1 / Admin@123 -- CHANGE IMMEDIATELY."
