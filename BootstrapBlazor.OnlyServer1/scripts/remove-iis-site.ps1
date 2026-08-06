<#
.SYNOPSIS
  Remove the IIS site + AppPool created by create-iis-site.ps1.

.DESCRIPTION
  (2026-08-03 IIS 迁移)替代旧 uninstall-windows-service.ps1。
  反向操作:Stop + Remove Site + Remove AppPool。
  **不动** keys/ logs/ 目录 — 历史登录态密钥 / 日志由运维手动决定是否保留。

.PARAMETER AppPoolName
  默认 DrawingNumberAppPool

.PARAMETER SiteName
  默认 DrawingNumber

.PARAMETER Confirm
  开关。默认 $true(必须显式传 -Confirm:$false 才执行)。
  防误删:生产环境破坏成本高。

.EXAMPLE
  .\remove-iis-site.ps1 -Confirm:$false
#>

[CmdletBinding()]
param(
    [string]$AppPoolName = 'DrawingNumberAppPool',
    [string]$SiteName    = 'DrawingNumber',
    [switch]$Confirm     = $true
)

$ErrorActionPreference = 'Stop'

Write-Host ''
Write-Host '>>> DrawingNumber IIS site remover' -ForegroundColor Cyan
Write-Host "  AppPoolName : $AppPoolName"
Write-Host "  SiteName    : $SiteName"
Write-Host ''

if ($Confirm) {
    Write-Warning 'Pass -Confirm:$false to actually remove the site and AppPool.'
    return
}

# 0. 管理员权限
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Must run as Administrator.'
}

Import-Module WebAdministration -ErrorAction SilentlyContinue
if (-not (Get-Module WebAdministration)) {
    throw 'WebAdministration module not found.'
}

# 1. Stop + Remove Site
if (Test-Path "IIS:\Sites\$SiteName") {
    Write-Host ">>> Removing Site: $SiteName" -ForegroundColor Yellow
    Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Remove-Website -Name $SiteName
    Write-Host "  + Site removed"
} else {
    Write-Host "  + Site '$SiteName' not found (already removed)"
}

# 2. Remove AppPool
if (Test-Path "IIS:\AppPools\$AppPoolName") {
    Write-Host ">>> Removing AppPool: $AppPoolName" -ForegroundColor Yellow
    Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Remove-WebAppPool -Name $AppPoolName
    Write-Host "  + AppPool removed"
} else {
    Write-Host "  + AppPool '$AppPoolName' not found (already removed)"
}

Write-Host ''
Write-Host 'DONE. Site + AppPool removed.' -ForegroundColor Green
Write-Host ''
Write-Host 'NOTE: keys/ and logs/ directories are NOT removed.' -ForegroundColor DarkYellow
Write-Host '      Decide manually whether to keep (login state) or delete (clean slate).'
Write-Host ''
