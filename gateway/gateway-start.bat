@echo off
cd /d "%USERPROFILE%\Desktop\SimpleVPNDesktop\gateway"
set SIMPLEVPN_APP_SECRET=super-long-random
set PORT=8787
set USE_LOCAL=1
node server.js
