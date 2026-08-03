<#
.SYNOPSIS
    Unregister the DrawingNumber Windows Service and remove its firewall rule.

.DESCRIPTION
    Stop the service, delete it, and remove the firewall rule created by
    install-windows-service.ps1. Idempotent — safe to run when nothing is registered.

.NOTES
    Must run as Administrator.
#>

[CmdletBinding()]
param(
    [int]$Port = 8373
)

$ErrorActionPreference = 'SilentlyContinue'

$ServiceName = 'DrawingNumber'
$RuleName    = "DrawingNumber-HTTPS-$Port"

Write-Host ">>> Removing DrawingNumber service" -ForegroundColor Cyan

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -eq 'Running') {
        Write-Host "  Stopping..."
        sc.exe stop $ServiceName | Out-Null
        # wait for actual stop
        $svc.WaitForStatus('Stopped', '00:00:30')
    }
    Write-Host "  Deleting service..."
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "  + Service removed" -ForegroundColor Green
} else {
    Write-Host "  Service '$ServiceName' was not registered."
}

# Firewall rule
$fw = Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
if ($fw) {
    Remove-NetFirewallRule -DisplayName $RuleName
    Write-Host "  + Removed firewall rule: $RuleName" -ForegroundColor Green
} else {
    Write-Host "  Firewall rule '$RuleName' not present."
}

# Per-service env-vars block (we set via reg add)
reg delete "HKLM\SYSTEM\CurrentControlSet\Services\$ServiceName" /v Environment /f 2>$null | Out-Null

Write-Host ""
Write-Host "DONE. DrawingNumber service uninstalled." -ForegroundColor Green
