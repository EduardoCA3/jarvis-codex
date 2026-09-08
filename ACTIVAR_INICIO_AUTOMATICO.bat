@echo off
setlocal
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$w=New-Object -ComObject WScript.Shell; $s=$w.CreateShortcut('%STARTUP%\Jarvis Codex.lnk'); $s.TargetPath='%~dp0NativeApp\Jarvis.exe'; $s.WorkingDirectory='%~dp0'; $s.Description='Jarvis Codex nativo'; $s.Save()"
echo Jarvis se iniciara con Windows para este usuario.
echo Puedes desactivarlo ejecutando DESACTIVAR_INICIO_AUTOMATICO.bat.
pause
