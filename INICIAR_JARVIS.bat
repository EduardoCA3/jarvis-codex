@echo off
setlocal
cd /d "%~dp0"
if exist "NativeApp\Jarvis.exe" (
  start "" "NativeApp\Jarvis.exe"
  exit /b 0
)
echo Jarvis nativo no esta compilado.
pause
exit /b 1
