@echo off
taskkill /F /IM sing-box.exe >nul 2>nul
cd /d "%USERPROFILE%\Desktop\SimpleVPNDesktop\bin\sing-box"
start "" "%CD%\sing-box.exe" run -c "%CD%\config.json"
exit /b
