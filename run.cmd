@echo off
setlocal
cd /d "%~dp0"
if exist "runtime\python.exe" goto launch
if exist ".venv\Scripts\python.exe" goto launch
echo Local Python environment is missing. Running the one-click installer...
call "%~dp0install.cmd"
if errorlevel 1 exit /b 1
:launch
if not exist "AutoElectiveOrb.exe" call "%~dp0build.cmd"
if errorlevel 1 exit /b 1
start "" "%~dp0AutoElectiveOrb.exe"
