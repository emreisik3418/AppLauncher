@echo off
REM ==========================================================
REM  AppLauncher - sc.exe ile Windows Service kurulumu
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

sc.exe query %SERVICE_NAME% >nul 2>&1
if errorlevel 1 (
  sc.exe create %SERVICE_NAME% binPath= "\"%INSTALL_DIR%\AppLauncher.exe\" --urls http://localhost:%PORT%" start= auto DisplayName= "App Launcher"
) else (
  sc.exe config %SERVICE_NAME% binPath= "\"%INSTALL_DIR%\AppLauncher.exe\" --urls http://localhost:%PORT%" start= auto
)
sc.exe description %SERVICE_NAME% "Uygulamalari web arayuzunden yonetir ve zamanlanmis calistirir."
sc.exe failure %SERVICE_NAME% reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start %SERVICE_NAME%

echo.
echo Tamam. Arayuz: http://localhost:%PORT%
echo Yapilandirma: %INSTALL_DIR%\data\apps.json
pause
