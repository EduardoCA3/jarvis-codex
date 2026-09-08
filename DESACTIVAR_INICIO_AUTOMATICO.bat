@echo off
setlocal
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
if exist "%STARTUP%\Jarvis Codex.lnk" del /q "%STARTUP%\Jarvis Codex.lnk"
if exist "%STARTUP%\JarvisCodex.cmd" del /q "%STARTUP%\JarvisCodex.cmd"
echo Inicio automatico de Jarvis desactivado.
pause
