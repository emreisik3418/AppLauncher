#Requires -RunAsAdministrator
param([string]$ServiceName = "AppLauncher")

$ErrorActionPreference = "Stop"
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) { Write-Host "Servis bulunamadı."; return }

if ($svc.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
sc.exe delete $ServiceName | Out-Null
Write-Host "Servis kaldırıldı. Kurulum klasörü ve data klasörü silinmedi." -ForegroundColor Yellow
