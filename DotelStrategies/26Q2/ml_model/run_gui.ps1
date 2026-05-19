$ErrorActionPreference = "Stop"
$Python = "C:\Users\joalr\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
$Script = Join-Path $PSScriptRoot "es_regime_gui.py"

& $Python $Script
