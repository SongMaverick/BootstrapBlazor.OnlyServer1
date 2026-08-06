<#
.SYNOPSIS
  Create or update an IIS site + AppPool for BootstrapBlazor.OnlyServer1.

.DESCRIPTION
  (2026-08-03 IIS 迁移)替代旧 install-windows-service.ps1。
  在 Windows 服务器上一次性完成:AppPool / Site / Bindings / DataProtection 权限。

  幂等:重复运行结果一致(AppPool 已存在则 update,Site 已存在则保留)。

  不创建防火墙规则 — IIS 通过 http.sys 内核驱动监听,Windows 已默认放行 HTTP/HTTPS;
  自定义端口(如 8373)走 IIS binding 即可,无需 New-NetFirewallRule。

.PARAMETER InstallDir
  IIS 站点物理路径(IIS 读取 .dll / web.config / appsettings.json 的目录)。
  默认 D:\inetpub\wwwroot\DrawingNumber(脚本不会自动建目录,需先 publish 后再跑)。

.PARAMETER AppPoolName
  默认 DrawingNumberAppPool

.PARAMETER SiteName
  默认 DrawingNumber

.PARAMETER HttpPort
  默认 8373(用户明确 IIS 默认端口已被占用,沿用现有 8373 习惯)

.PARAMETER HttpsPort
  默认 8373(同 HttpPort;若想分离,显式传参)

.PARAMETER CertThumbprint
  HTTPS 证书 thumbprint(40 字符,无空格)。从 IIS 计算机证书或 LocalMachine\My 取:
    Get-ChildItem Cert:\LocalMachine\My | Format-List Subject, Thumbprint
  不传则只创建 HTTP binding。

.PARAMETER CopyKeysFrom
  从旧服务器迁移 DataProtection 密钥目录。指向旧 keys/ 路径(包含 key-<guid>.xml)即可。
  保留登录态:跨服务器搬站时必传,否则全员重登。

.EXAMPLE
  # 全新部署
  .\create-iis-site.ps1 -InstallDir 'D:\inetpub\wwwroot\DrawingNumber' -CertThumbprint 'ABCDEF1234567890ABCDEF1234567890ABCDEF12'

.EXAMPLE
  # 跨服务器搬站(保留登录态)
  .\create-iis-site.ps1 `
    -InstallDir 'D:\inetpub\wwwroot\DrawingNumber' `
    -CertThumbprint '...' `
    -CopyKeysFrom '\\oldserver\D$\Applications\DrawingNumber\web_publish\keys'

.EXAMPLE
  # 仅 HTTP(暂未配证书)
  .\create-iis-site.ps1 -InstallDir 'D:\inetpub\wwwroot\DrawingNumber' -HttpPort 8373

.NOTES
  后续:跑 .\remove-iis-site.ps1 反向操作(不动 keys/ logs/,需运维手动保留)。
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$InstallDir,

    [string]$AppPoolName    = 'DrawingNumberAppPool',
    [string]$SiteName       = 'DrawingNumber',
    [int]$HttpPort          = 8373,
    [int]$HttpsPort         = 8373,
    [string]$CertThumbprint = $null,
    [string]$CopyKeysFrom   = $null
)

$ErrorActionPreference = 'Stop'

Write-Host ''
Write-Host '>>> DrawingNumber IIS site creator' -ForegroundColor Cyan
Write-Host "  InstallDir    : $InstallDir"
Write-Host "  AppPoolName   : $AppPoolName"
Write-Host "  SiteName      : $SiteName"
Write-Host "  HttpPort      : $HttpPort"
Write-Host "  HttpsPort     : $HttpsPort"
Write-Host "  CertThumbprint: $(if ($CertThumbprint) { $CertThumbprint } else { '(none, HTTP only)' })"
Write-Host "  CopyKeysFrom  : $(if ($CopyKeysFrom) { $CopyKeysFrom } else { '(none)' })"
Write-Host ''

# ============================================================
# 0. 预检
# ============================================================
Write-Host '>>> Pre-flight checks' -ForegroundColor Yellow

# 0.1 必须管理员
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Must run as Administrator (right-click PowerShell → Run as administrator).'
}
Write-Host '  + Running as Administrator'

# 0.2 WebAdministration 模块(IIS PowerShell)
Import-Module WebAdministration -ErrorAction SilentlyContinue
if (-not (Get-Module WebAdministration)) {
    throw 'WebAdministration module not found. Install IIS Management Scripts and Tools role service.'
}
Write-Host '  + WebAdministration module loaded'

# 0.3 .NET 8 Hosting Bundle(>= 8.0.10)
$hostingRelease = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
if ($hostingRelease -lt 528040) {
    Write-Warning ".NET 8 Hosting Bundle not detected (Registry Release=$hostingRelease; need >= 528040)."
    Write-Warning "  Download: https://dotnet.microsoft.com/download/dotnet/8.0"
    if (-not $CertThumbprint) {
        throw 'Hosting Bundle missing. Install it first, or pass -SkipHostingBundleCheck to override.'
    }
} else {
    Write-Host "  + .NET 8 Hosting Bundle detected (Release=$hostingRelease)"
}

# 0.4 WebSocket Protocol 角色(Blazor Server SignalR 必须)
$ws = Get-WindowsFeature -Name Web-WebSocket -ErrorAction SilentlyContinue
if ($ws -and -not $ws.Installed) {
    Write-Warning 'IIS WebSocket Protocol role not installed. Blazor Server SignalR will fall back to long-polling.'
} elseif ($ws) {
    Write-Host '  + IIS WebSocket Protocol installed'
}

# 0.5 ANCM V2 模块
$ancm = Get-WebGlobalModule -Name AspNetCoreModuleV2 -ErrorAction SilentlyContinue
if (-not $ancm) {
    Write-Warning 'ASP.NET Core Module V2 not detected. ANCM is required for in-process hosting.'
} else {
    Write-Host '  + ASP.NET Core Module V2 detected'
}

# 0.6 InstallDir 物理路径
if (-not (Test-Path $InstallDir)) {
    throw "InstallDir not found: $InstallDir. Run publish-iis.ps1 first and copy to this path."
}
$webConfigPath = Join-Path $InstallDir 'web.config'
if (-not (Test-Path $webConfigPath)) {
    throw "web.config missing under $InstallDir. Did you forget to include web.config in publish output?"
}
Write-Host "  + InstallDir exists: $InstallDir"

# 0.7 HTTPS cert thumbprint 校验(若有)
if ($CertThumbprint) {
    $cert = Get-ChildItem "Cert:\LocalMachine\My\$CertThumbprint" -ErrorAction SilentlyContinue
    if (-not $cert) {
        throw "Cert thumbprint not found in Cert:\LocalMachine\My: $CertThumbprint"
    }
    Write-Host "  + Cert found: $($cert.Subject) (thumb=$CertThumbprint)"
}

# 0.8 CopyKeysFrom 校验(若有)
if ($CopyKeysFrom) {
    if (-not (Test-Path $CopyKeysFrom)) {
        throw "CopyKeysFrom path not found: $CopyKeysFrom"
    }
    $keyFiles = Get-ChildItem $CopyKeysFrom -Filter 'key-*.xml' -ErrorAction SilentlyContinue
    if (-not $keyFiles) {
        Write-Warning "No key-*.xml files found under $CopyKeysFrom — login state will NOT survive migration."
    } else {
        Write-Host "  + $(@($keyFiles).Count) DataProtection key file(s) found at CopyKeysFrom"
    }
}

Write-Host ''

# ============================================================
# 1. AppPool
# ============================================================
Write-Host ">>> AppPool: $AppPoolName" -ForegroundColor Yellow

if (Test-Path "IIS:\AppPools\$AppPoolName") {
    Write-Host "  + AppPool exists, updating settings"
} else {
    New-WebAppPool -Name $AppPoolName | Out-Null
    Write-Host "  + AppPool created"
}

Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''                       # No Managed Code
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode            -Value 'AlwaysRunning'           # 防止首请求冷启动
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.idleTimeout -Value '00:00:00'           # 禁空闲超时(Blazor 长连接)
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name recycling.periodicRestart.time -Value '00:00:00'      # 关键:禁 29h 周期回收
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType  -Value 'ApplicationPoolIdentity'
Write-Host '    .NET CLR     = No Managed Code'
Write-Host '    Start Mode   = AlwaysRunning'
Write-Host '    Idle Timeout = 00:00:00 (disabled)'
Write-Host '    PeriodicRestart.Time = 00:00:00 (disabled — avoids 29h SignalR drop)'
Write-Host '    Identity     = ApplicationPoolIdentity'

# 1.1 强制覆盖 ASPNETCORE_ENVIRONMENT(不允许残留值)
$envCollection = Get-ItemProperty "IIS:\AppPools\$AppPoolName" -Name environmentVariables -ErrorAction SilentlyContinue
# 简单做法:删空再加
$poolPath = "IIS:\AppPools\$AppPoolName"
Set-WebConfigurationProperty -Filter "/system.applicationHost/applicationPools/add[@name='$AppPoolName']/environmentVariables" -Name "." -Value $null -ErrorAction SilentlyContinue
Add-WebConfigurationProperty -Filter "/system.applicationHost/applicationPools/add[@name='$AppPoolName']/environmentVariables" -Name "." -Value @{name='ASPNETCORE_ENVIRONMENT'; value='Production'}
Write-Host '    EnvVar       = ASPNETCORE_ENVIRONMENT=Production (forced)'

Write-Host ''

# ============================================================
# 2. Site
# ============================================================
Write-Host ">>> Site: $SiteName" -ForegroundColor Yellow

if (Test-Path "IIS:\Sites\$SiteName") {
    Write-Host "  + Site exists; binding/AppPool will be updated"
    # 更新 AppPool + physical path
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool   -Value $AppPoolName
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath      -Value $InstallDir
} else {
    New-Website -Name $SiteName -PhysicalPath $InstallDir -ApplicationPool $AppPoolName | Out-Null
    Write-Host "  + Site created (physicalPath=$InstallDir, AppPool=$AppPoolName)"
}

# ============================================================
# 3. Bindings
# ============================================================
Write-Host '>>> Bindings' -ForegroundColor Yellow

# 3.1 HTTP binding(幂等:已存在则跳过)
$existingHttp = Get-WebBinding -Name $SiteName -Protocol http -Port $HttpPort -ErrorAction SilentlyContinue
if ($existingHttp) {
    Write-Host "  + HTTP binding *:$HttpPort: already exists"
} else {
    New-WebBinding -Name $SiteName -BindingInformation "*:${HttpPort}:" -Protocol http | Out-Null
    Write-Host "  + HTTP binding *:$HttpPort: created"
}

# 3.2 HTTPS binding(可选)
if ($CertThumbprint) {
    $existingHttps = Get-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -ErrorAction SilentlyContinue
    if ($existingHttps) {
        Write-Host "  + HTTPS binding *:$HttpsPort: already exists; updating cert thumbprint"
        # 更新已有 binding 的证书
        Set-WebBinding -Name $SiteName -BindingInformation "*:${HttpsPort}:" -PropertyName certificateHash        -Value $CertThumbprint | Out-Null
        Set-WebBinding -Name $SiteName -BindingInformation "*:${HttpsPort}:" -PropertyName certificateStoreName   -Value 'MY' | Out-Null
    } else {
        New-WebBinding -Name $SiteName -BindingInformation "*:${HttpsPort}:" -Protocol https -SslFlags 1 | Out-Null
        Set-WebBinding -Name $SiteName -BindingInformation "*:${HttpsPort}:" -PropertyName certificateHash      -Value $CertThumbprint | Out-Null
        Set-WebBinding -Name $SiteName -BindingInformation "*:${HttpsPort}:" -PropertyName certificateStoreName -Value 'MY' | Out-Null
        Write-Host "  + HTTPS binding *:$HttpsPort: created (cert=$CertThumbprint)"
    }
}

Write-Host ''

# ============================================================
# 4. DataProtection keys/ 目录 + 权限
# ============================================================
Write-Host '>>> DataProtection keys/' -ForegroundColor Yellow

$keysDir = Join-Path $InstallDir 'keys'
if (-not (Test-Path $keysDir)) {
    New-Item -ItemType Directory -Path $keysDir -Force | Out-Null
    Write-Host "  + Created $keysDir"
} else {
    Write-Host "  + Exists: $keysDir"
}

# 4.1 跨服务器搬站:导入旧 keys
if ($CopyKeysFrom) {
    Write-Host "  + Copying keys from $CopyKeysFrom"
    Copy-Item -Path "$CopyKeysFrom\*" -Destination $keysDir -Recurse -Force
    $count = (Get-ChildItem $keysDir -Filter 'key-*.xml' -ErrorAction SilentlyContinue | Measure-Object).Count
    Write-Host "    $count key file(s) in $keysDir"
}

# 4.2 icacls 给 IIS_IUSRS 写权限(ApplicationPoolIdentity 是 IIS_IUSRS 组的一员)
# 必须先 mkdir 再 icacls,顺序反了 NTFS 创建的目录权限不全
& icacls $keysDir /grant "IIS_IUSRS:(OI)(CI)M" /T | Out-Null
Write-Host "  + ACL: IIS_IUSRS:(OI)(CI)M on $keysDir"

# ============================================================
# 5. logs/ 目录 + 权限(ANCM stdoutLogFile 写入 + NLog fallback)
# ============================================================
Write-Host '>>> logs/' -ForegroundColor Yellow

$logsDir = Join-Path $InstallDir 'logs'
if (-not (Test-Path $logsDir)) {
    New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
    Write-Host "  + Created $logsDir"
} else {
    Write-Host "  + Exists: $logsDir"
}
& icacls $logsDir /grant "IIS_IUSRS:(OI)(CI)M" /T | Out-Null
Write-Host "  + ACL: IIS_IUSRS:(OI)(CI)M on $logsDir"

Write-Host ''

# ============================================================
# 6. 启动 + 预热
# ============================================================
Write-Host '>>> Starting AppPool + Site' -ForegroundColor Yellow

Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
Start-Website    -Name $SiteName    -ErrorAction SilentlyContinue

Start-Sleep -Seconds 2

$poolState = Get-WebAppPoolState -Name $AppPoolName
$siteState = Get-Website          -Name $SiteName | Select-Object -ExpandProperty State
Write-Host "  + AppPool state : $($poolState.Value)"
Write-Host "  + Site state    : $siteState"

# 6.1 预热首请求(避免用户首次访问 5-15s 冷启动)
Write-Host ''
Write-Host '>>> Warm-up (first request can take 5-15s due to JIT + ANCM)' -ForegroundColor Yellow
try {
    $warmup = Invoke-WebRequest "http://localhost:${HttpPort}/" -UseBasicParsing -TimeoutSec 30 -ErrorAction Stop
    Write-Host "  + HTTP  warmup OK (status=$($warmup.StatusCode))"
} catch {
    Write-Warning "HTTP warmup failed: $($_.Exception.Message)"
    Write-Warning '  Check stdout log under logs\stdout\ and Windows Event Viewer (Application log, IIS AspNetCore Module V2 source).'
}

if ($CertThumbprint) {
    try {
        $warmup = Invoke-WebRequest "https://localhost:${HttpsPort}/" -UseBasicParsing -TimeoutSec 30 -SkipCertificateCheck -ErrorAction Stop
        Write-Host "  + HTTPS warmup OK (status=$($warmup.StatusCode))"
    } catch {
        Write-Warning "HTTPS warmup failed: $($_.Exception.Message)"
    }
}

Write-Host ''
Write-Host 'DONE. Site is ready.' -ForegroundColor Green
Write-Host "  AppPool : $AppPoolName ($($poolState.Value))"
Write-Host "  Site    : $SiteName    ($siteState)"
Write-Host "  URL     : $(if ($CertThumbprint) { "https://localhost:${HttpsPort}/" } else { "http://localhost:${HttpPort}/" })"
Write-Host ''
