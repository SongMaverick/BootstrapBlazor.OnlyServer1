<#
.SYNOPSIS
  Publish BootstrapBlazor.OnlyServer1 for IIS deployment.

.DESCRIPTION
  (2026-08-03 IIS 迁移)替代旧 publish-server.ps1。
  生成 framework-dependent 的 IIS 部署包 — 服务器需装 .NET 8 Hosting Bundle。

  默认输出不含 BootstrapBlazor.OnlyServer1.exe(那是 Windows Service 自托管的产物);
  IIS 路径下 ANCM 通过 processPath="dotnet" arguments=".\BootstrapBlazor.OnlyServer1.dll" 加载。

.PARAMETER ProjectPath
  .csproj 路径。默认 = 脚本同级 ../BootstrapBlazor.OnlyServer1.csproj

.PARAMETER OutputDir
  publish 输出目录。默认 D:\Publish\DrawingNumber\IIS
  (运维可改成 IIS 站点物理路径,例如 D:\inetpub\wwwroot\DrawingNumber)

.PARAMETER Configuration
  Debug | Release。默认 Release(发布到生产)。

.PARAMETER SelfContained
  开关。默认 $false(framework-dependent,服务器要装 Hosting Bundle)。
  加 -SelfContained 时附加 -r win-x64 --self-contained true,产物 ~80MB 不依赖服务器 Runtime。

.PARAMETER NoReadyToRun
  开关。默认开 R2R(ReadyToRun),ANCM 冷启动优化。
  加 -NoReadyToRun 关闭可加快 publish 速度。

.EXAMPLE
  .\publish-iis.ps1
  # 默认 Release + framework-dependent + ReadyToRun → D:\Publish\DrawingNumber\IIS

.EXAMPLE
  .\publish-iis.ps1 -OutputDir 'D:\inetpub\wwwroot\DrawingNumber'
  # 直接 publish 到 IIS 站点物理路径(发布后需手动 Recycle AppPool)

.EXAMPLE
  .\publish-iis.ps1 -SelfContained
  # 自包含模式,服务器无需装 Hosting Bundle

.NOTES
  后续步骤:把 OutputDir 内容拷到 IIS 站点物理路径 → 跑 scripts\create-iis-site.ps1
#>

[CmdletBinding()]
param(
    [string]$ProjectPath   = (Join-Path $PSScriptRoot '..\BootstrapBlazor.OnlyServer1.csproj'),
    [string]$OutputDir     = 'D:\Publish\DrawingNumber\IIS',
    [string]$Configuration = 'Release',
    [switch]$SelfContained = $false,
    [switch]$NoReadyToRun  = $false
)

$ErrorActionPreference = 'Stop'

Write-Host ''
Write-Host '>>> DrawingNumber IIS publish' -ForegroundColor Cyan
Write-Host "  ProjectPath : $ProjectPath"
Write-Host "  OutputDir   : $OutputDir"
Write-Host "  Config      : $Configuration"
Write-Host "  SelfContained: $SelfContained"
Write-Host "  ReadyToRun  : $(-not $NoReadyToRun)"
Write-Host ''

# ----------------------------------------------------------------
# 0. 预检
# ----------------------------------------------------------------
if (-not (Test-Path $ProjectPath)) {
    throw ".csproj not found: $ProjectPath"
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
if (-not $dotnet) {
    throw 'dotnet CLI not in PATH. Install .NET 8 SDK first.'
}

# ----------------------------------------------------------------
# 1. dotnet publish
# ----------------------------------------------------------------
$pubArgs = @(
    'publish', $ProjectPath
    '-c', $Configuration
    '-o', $OutputDir
    '-p:PublishSingleFile=false'
    '-p:PublishTrimmed=false'        # Blazor Server 不支持 trim,必须 false
)

if (-not $NoReadyToRun) {
    $pubArgs += '-p:PublishReadyToRun=true'
}

if ($SelfContained) {
    $pubArgs += @('-r', 'win-x64', '--self-contained', 'true')
}

Write-Host ">>> Running: dotnet $($pubArgs -join ' ')" -ForegroundColor DarkGray
Write-Host ''

& dotnet @pubArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit=$LASTEXITCODE)"
}

Write-Host ''
Write-Host ">>> Publish done. Output: $OutputDir" -ForegroundColor Green
Write-Host ''

# ----------------------------------------------------------------
# 2. 验证产物结构(IIS 期望)
# ----------------------------------------------------------------
$expectedFiles = @(
    'BootstrapBlazor.OnlyServer1.dll'
    'BootstrapBlazor.OnlyServer1.runtimeconfig.json'
    'appsettings.json'
    'NLog.config'
    'web.config'
)

$missing = $expectedFiles | Where-Object { -not (Test-Path (Join-Path $OutputDir $_)) }
if ($missing) {
    Write-Warning "Missing expected files in output: $($missing -join ', ')"
}

# IIS 路径下不需要 .exe(那是 Windows Service 时代的产物)
$exePath = Join-Path $OutputDir 'BootstrapBlazor.OnlyServer1.exe'
if (Test-Path $exePath) {
    Write-Warning "Output contains BootstrapBlazor.OnlyServer1.exe — IIS path does not need it. Ignore if SelfContained was used (it generates apphost.exe, harmless)."
}

Write-Host ''
Write-Host '>>> Next steps (for IT):' -ForegroundColor Cyan
Write-Host "  1. Copy '$OutputDir' to the IIS site physical path"
Write-Host "       (e.g. robocopy '$OutputDir' '<IIS-physical-path>' /MIR /XD keys logs)"
Write-Host "  2. Run scripts\create-iis-site.ps1 with the physical path and cert thumbprint"
Write-Host "       (see: Get-ChildItem Cert:\LocalMachine\My | Where Issuer -match '...')"
Write-Host ''
