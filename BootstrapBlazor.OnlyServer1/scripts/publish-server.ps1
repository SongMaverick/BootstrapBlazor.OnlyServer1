<#
.SYNOPSIS
    Build a server-ready self-contained win-x64 publish bundle for DrawingNumber.

.DESCRIPTION
    Produces <OutputDir>\BootstrapBlazor.OnlyServer1.exe + all native runtimes.
    Target server does NOT need .NET 8 installed (~80MB instead of ~20MB).

.PARAMETER ProjectPath
    Path to the .csproj. Default: parent's BootstrapBlazor.OnlyServer1.csproj

.PARAMETER OutputDir
    Where to drop the publish artifacts. Default: D:\Publish\DrawingNumber

.PARAMETER Configuration
    Release or Debug. Default: Release.

.EXAMPLE
    PS> .\publish-server.ps1

.NOTES
    Run from any PowerShell. dotnet CLI must be on PATH.
#>

[CmdletBinding()]
param(
    [string]$ProjectPath  = (Join-Path $PSScriptRoot '..\BootstrapBlazor.OnlyServer1.csproj'),
    [string]$OutputDir    = 'D:\Publish\DrawingNumber',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# Resolve absolute paths and validate the project file exists
$ProjectPath = (Resolve-Path $ProjectPath -ErrorAction Stop).Path
if (-not (Test-Path $ProjectPath)) {
    Write-Error "Project file not found: $ProjectPath"; exit 1
}

Write-Host ">>> Building server publish bundle" -ForegroundColor Cyan
Write-Host "    Project : $ProjectPath"
Write-Host "    Output  : $OutputDir"
Write-Host "    Config  : $Configuration, RID=win-x64, SelfContained"
Write-Host ""

$pubArgs = @(
    'publish'
    $ProjectPath
    '-c', $Configuration
    '-r', 'win-x64'
    '--self-contained', 'true'
    '-o', $OutputDir
    '-p:PublishSingleFile=false'
    '-p:PublishReadyToRun=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
)

Write-Host "dotnet $($pubArgs -join ' ')"
Write-Host ""
dotnet @pubArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE
}

Write-Host ""
Write-Host "DONE. Artifacts:" -ForegroundColor Green
Write-Host "  $OutputDir\BootstrapBlazor.OnlyServer1.exe"
Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
# (2026-07-XX)项目约定的标准部署根目录是
# D:\Applications\MXB\DrawingNumber\web_publish\。
# 发布者把整个 $OutputDir 拷给 IT(IT 端解压路径由 IT 决定;
# 可用 robocopy 直推到服务器,或在本地打成 zip 后给 IT 解到目标目录)。
Write-Host "  1) Copy or zip the contents of '$OutputDir' and hand to IT"
Write-Host "  2) IT unzips to 'D:\Applications\MXB\DrawingNumber\web_publish\'"
Write-Host "     (若解出 web_publish_<日期>\ 目录,IT 应重命名/移动到 web_publish\)"
Write-Host "  3) IT runs scripts\generate-cert.ps1 on the server"
Write-Host "  4) IT runs scripts\install-windows-service.ps1 on the server"
