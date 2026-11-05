@echo off
REM AppLauncher servisini kaldirir (dosyalar silinmez). Yonetici olarak calistirin.
set SERVICE_NAME=AppLauncher
net session >nul 2>&1 || (echo [HATA] Yonetici olarak calistirin. & pause & exit /b 1)
sc.exe stop %SERVICE_NAME%
timeout /t 5 /nobreak >nul
sc.exe delete %SERVICE_NAME%
echo Servis kaldirildi.
pause
