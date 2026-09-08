@echo off
setlocal
cd /d "%~dp0"
echo Creando el entorno privado de Jarvis...
python -m venv .venv
if errorlevel 1 goto :error
echo Instalando el nucleo y el SDK oficial de Codex...
".venv\Scripts\python.exe" -m pip install --upgrade pip
if errorlevel 1 goto :error
".venv\Scripts\python.exe" -m pip install -r requirements.txt
if errorlevel 1 goto :error
echo.
echo Instalacion basica completada sin API de pago.
echo Para hablar con Jarvis ejecuta tambien INSTALAR_VOZ.bat.
pause
exit /b 0
:error
echo.
echo La instalacion fallo. Copia este mensaje antes de cerrar la ventana.
pause
exit /b 1

