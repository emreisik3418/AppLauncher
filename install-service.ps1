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
# data klasörü ve appsettings.Local.json korunur; publish onları ezmez.
dotnet publish $project -c Release -r win-x64 --self-contained false -o $InstallDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız." }

$local = Join-Path $InstallDir "appsettings.Local.json"
if (-not (Test-Path $local)) {
    Write-Host "appsettings.Local.json oluşturuluyor (izinli kullanıcı: $env:USERNAME)"
    $cfg = [ordered]@{
        Urls = "http://0.0.0.0:$Port"
        Launcher = [ordered]@{
            Auth = [ordered]@{ Enabled = $true; AllowedUsers = @($env:USERNAME); AllowedGroups = @() }
        }
    }
    $cfg | ConvertTo-Json -Depth 5 | Set-Content -Path $local -Encoding UTF8
}

$exe = Join-Path $InstallDir "AppLauncher.exe"
$binPath = "`"$exe`""

if (-not $existing) {
    Write-Host "Servis oluşturuluyor..."
    New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName $DisplayName `
        -Description "Uygulamaları web arayüzünden yönetir ve zamanlanmış çalıştırır." -StartupType Automatic | Out-Null
} else {
    sc.exe config $ServiceName binPath= $binPath start= auto | Out-Null
}

sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

$ruleName = "AppLauncher $Port"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port | Out-Null
}

Start-Service -Name $ServiceName
Write-Host ""
Write-Host "Kuruldu ve başlatıldı. Arayüz: http://$($env:COMPUTERNAME):$Port" -ForegroundColor Green
Write-Host "İzinli siciller: $local"
Write-Host "Uygulama listesi: $InstallDir\data\apps.json"
