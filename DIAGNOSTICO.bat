@echo off
setlocal
cd /d "%~dp0"
if exist ".venv\Scripts\python.exe" (
  ".venv\Scripts\python.exe" jarvis.py --diagnostico
) else (
  python jarvis.py --diagnostico
)
pause

