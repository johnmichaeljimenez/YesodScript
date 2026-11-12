@echo off
setlocal
cd /d "%~dp0"
if errorlevel 1 exit /b 1

dotnet publish -f net10.0 -r browser-wasm -c Release
if errorlevel 1 exit /b 1

where dotnet-serve >nul 2>nul
if errorlevel 1 (
  echo ERROR: dotnet-serve was not found on PATH. Install it with:
  echo   dotnet tool install -g dotnet-serve
  exit /b 1
)

@REM  uses Tailscale IP
powershell -NoProfile -Command "$ip = (tailscale ip -4).Trim(); dotnet serve -d 'bin/Release/net10.0/browser-wasm/AppBundle' --port 5199 --address $ip --open-browser"