@echo off
setlocal EnableExtensions EnableDelayedExpansion
title SimpleVPN Launcher (Admin)

rem Resolve script folder
set "SCRIPT_DIR=%~dp0"
rem Normalize trailing backslash removed, re-add later as needed
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

rem Paths
set "BIN_DIR=%SCRIPT_DIR%\..\bin\sing-box"
set "SB_EXE=%BIN_DIR%\sing-box.exe"
set "SB_WINTUN=%BIN_DIR%\wintun.dll"
set "APP_EXE=%SCRIPT_DIR%\SimpleVPN.exe"
set "CODE_TXT=%SCRIPT_DIR%\code.txt"

rem Require admin (Wintun install needs elevation sometimes)
net session >nul 2>&1
if %errorlevel% NEQ 0 (
  echo [*] Elevating to Administrator...
  powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

echo [*] Launcher folder: %SCRIPT_DIR%

rem Env
set "SVPN_GATEWAY=http://127.0.0.1:8787"
set "SVPN_SECRET=super-long-random"
set "ENABLE_DEPRECATED_TUN_ADDRESS_X=true"

rem Checks
if not exist "%CODE_TXT%" (
  echo [ERR] code.txt not found at "%CODE_TXT%"
  pause & exit /b 1
)
if not exist "%SB_EXE%" (
  echo [ERR] sing-box.exe missing at "%SB_EXE%"
  pause & exit /b 1
)
if not exist "%SB_WINTUN%" (
  echo [ERR] wintun.dll missing at "%SB_WINTUN%"
  pause & exit /b 1
)
if not exist "%APP_EXE%" (
  echo [ERR] SimpleVPN.exe not found at "%APP_EXE%"
  pause & exit /b 1
)

rem Check gateway is listening
powershell -NoProfile -Command ^
  "$r=Test-NetConnection -ComputerName 127.0.0.1 -Port 8787; if(-not $r.TcpTestSucceeded){Write-Host '[ERR] Gateway not listening on 127.0.0.1:8787' -ForegroundColor Red; exit 2}"
if errorlevel 2 (
  echo [ERR] Gateway is not reachable on 127.0.0.1:8787
  echo     Make sure server.js is running in the gateway window.
  pause & exit /b 2
)

echo [*] Starting SimpleVPN.exe...
pushd "%SCRIPT_DIR%"
"%APP_EXE%"
set "RC=%ERRORLEVEL%"
popd

echo [*] App exited with code %RC%
pause
exit /b %RC%
