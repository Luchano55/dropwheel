@echo off
rem Drag a file onto this script: it lists the available operations and runs the one you type.
chcp 65001 >nul
cd /d "%~dp0"
if "%~1"=="" (
  echo Drag a file onto this script.
  pause
  exit /b
)
dropwheel-cli.exe list "%~1"
echo.
set /p OP=Operation (for example to-jpg):
dropwheel-cli.exe convert "%~1" %OP%
pause
