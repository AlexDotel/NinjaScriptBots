@echo off
setlocal
set "PYTHON=C:\Users\joalr\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
set "SCRIPT=%~dp0es_regime_gui.py"

"%PYTHON%" "%SCRIPT%"

if errorlevel 1 (
  echo.
  echo La interfaz se cerro con un error.
  pause
)
