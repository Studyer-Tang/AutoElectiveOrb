@echo off
setlocal
cd /d "%~dp0"

if exist "AutoElectiveOrb.exe" del /q "AutoElectiveOrb.exe"
powershell.exe -NoProfile -Command "$sources = Get-ChildItem -LiteralPath '.\src' -Filter '*.cs' | ForEach-Object FullName; Add-Type -Path $sources -ReferencedAssemblies 'System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions' -OutputAssembly '.\AutoElectiveOrb.exe' -OutputType WindowsApplication"
if errorlevel 1 exit /b 1

echo Built %~dp0AutoElectiveOrb.exe
exit /b 0
