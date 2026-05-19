@echo off
setlocal
cd /d "%~dp0"
set "BUNDLED_PY=C:\Users\joalr\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
if exist "%BUNDLED_PY%" (
    "%BUNDLED_PY%" ema_optimizer_web.py
) else (
    py ema_optimizer_web.py
)
pause
