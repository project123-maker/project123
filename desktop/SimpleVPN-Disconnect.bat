@echo off
setlocal
set GW=http://127.0.0.1:8787
set SECRET=super-long-random
for /f %%A in ('wmic csproduct get uuid ^| findstr -r "[0-9A-F]"') do set DID=%%A

set /p CODE=Enter redeem code to disconnect: 
curl -s -H "x-svpn-secret: %SECRET%" ^
  -H "Content-Type: application/json" ^
  -X POST "%GW%/disconnect" ^
  --data "{\"code\":\"%CODE%\",\"deviceId\":\"%DID%\"}" | findstr /I /C:"ok" >nul
if %errorlevel%==0 ( echo Disconnected. ) else ( echo Disconnect failed. )
pause
