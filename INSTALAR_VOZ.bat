@echo off
setlocal
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
  echo Primero ejecuta INSTALAR.bat.
  pause
  exit /b 1
)
echo Instalando reconocimiento de voz local gratuito...
".venv\Scripts\python.exe" -m pip install -r requirements-voice.txt
if errorlevel 1 goto :error
echo Descargando el detector local de la frase Hey Jarvis...
".venv\Scripts\python.exe" -c "from openwakeword.utils import download_models; download_models(['hey_jarvis'])"
if errorlevel 1 goto :error
echo.
echo Voz instalada. El modelo Whisper se descargara la primera vez que pulses Hablar.
pause
exit /b 0
:error
echo.
echo La instalacion de voz fallo. El modo escrito seguira funcionando.
pause
exit /b 1

