@echo off
REM ==========================================================
REM  AppLauncher - sc.exe ile Windows Service kurulumu/guncellemesi
REM  Yonetici olarak calistirin (sag tik > Yonetici olarak calistir)
REM ==========================================================
setlocal
set SERVICE_NAME=AppLauncher
set INSTALL_DIR=C:\Services\AppLauncher
set PORT=5080

net session >nul 2>&1 || (echo [HATA] Yonetici olarak calistirin. & pause & exit /b 1)

sc.exe query %SERVICE_NAME% >nul 2>&1 && (
  echo Mevcut servis durduruluyor...
  sc.exe stop %SERVICE_NAME% >nul 2>&1
  timeout /t 5 /nobreak >nul
)

echo Yayinlaniyor: %INSTALL_DIR%
dotnet publish "%~dp0src\AppLauncher\AppLauncher.csproj" -c Release -r win-x64 --self-contained false -o "%INSTALL_DIR%"
if errorlevel 1 (echo [HATA] dotnet publish basarisiz. & pause & exit /b 1)

REM Sunucuya ozel ayar dosyasi yoksa olustur (kurulumu yapan kullanici izinli listeye eklenir)
if exist "%INSTALL_DIR%\appsettings.Local.json" goto :local_ok
echo appsettings.Local.json olusturuluyor (izinli kullanici: %USERNAME%)
(
echo {
echo   "Urls": "http://0.0.0.0:%PORT%",
echo   "Launcher": {
echo     "Auth": {
echo       "Enabled": true,
echo       "AllowedUsers": [ "%USERNAME%" ],
echo       "AllowedGroups": []
echo     }
echo   }
echo }
) > "%INSTALL_DIR%\appsettings.Local.json"
:local_ok

sc.exe query %SERVICE_NAME% >nul 2>&1
if errorlevel 1 (
  sc.exe create %SERVICE_NAME% binPath= "\"%INSTALL_DIR%\AppLauncher.exe\"" start= auto DisplayName= "App Launcher"
) else (
  sc.exe config %SERVICE_NAME% binPath= "\"%INSTALL_DIR%\AppLauncher.exe\"" start= auto
)
sc.exe description %SERVICE_NAME% "Uygulamalari web arayuzunden yonetir ve zamanlanmis calistirir."
sc.exe failure %SERVICE_NAME% reset= 86400 actions= restart/60000/restart/60000/restart/60000

REM Guvenlik duvari kurali (yoksa ekle)
netsh advfirewall firewall show rule name="AppLauncher %PORT%" >nul 2>&1 || netsh advfirewall firewall add rule name="AppLauncher %PORT%" dir=in action=allow protocol=TCP localport=%PORT% >nul

sc.exe start %SERVICE_NAME%

echo.
echo Tamam. Arayuz: http://%COMPUTERNAME%:%PORT%
echo Izinli siciller: %INSTALL_DIR%\appsettings.Local.json
echo Uygulama listesi: %INSTALL_DIR%\data\apps.json
pause
