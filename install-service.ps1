#Requires -RunAsAdministrator
<#
  AppLauncher'ı derleyip Windows Service olarak kurar (veya günceller).
  Kullanım (Yönetici PowerShell):
    .\install-service.ps1
    .\install-service.ps1 -InstallDir "D:\Servisler\AppLauncher" -Port 5080
#>
param(
    [string]$ServiceName = "AppLauncher",
    [string]$DisplayName = "App Launcher",
    [string]$InstallDir  = "C:\Services\AppLauncher",
    [int]$Port = 5080
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\AppLauncher\AppLauncher.csproj"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Servis durduruluyor..."
    Stop-Service -Name $ServiceName -Force
    (Get-Service $ServiceName).WaitForStatus('Stopped', '00:00:30')
}

Write-Host "Yayınlanıyor -> $InstallDir"
# data klasörü (apps.json, geçmiş, loglar) korunur; publish onu silmez.
dotnet publish $project -c Release -r win-x64 --self-contained false -o $InstallDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız." }

$exe = Join-Path $InstallDir "AppLauncher.exe"
$binPath = "`"$exe`" --urls http://localhost:$Port"

if (-not $existing) {
    Write-Host "Servis oluşturuluyor..."
    New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName $DisplayName `
        -Description "Uygulamaları web arayüzünden yönetir ve zamanlanmış çalıştırır." -StartupType Automatic | Out-Null
} else {
    sc.exe config $ServiceName binPath= $binPath start= auto | Out-Null
}

# Hata ile kapanırsa 1 dk sonra yeniden başlat
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

Start-Service -Name $ServiceName
Write-Host ""
Write-Host "Kuruldu ve başlatıldı. Arayüz: http://localhost:$Port" -ForegroundColor Green
Write-Host "Yapılandırma: $InstallDir\data\apps.json"
