@echo off
setlocal
cd /d "%~dp0"

if exist "AutoElectiveOrb.exe" del /q "AutoElectiveOrb.exe"
if exist "UninstallAutoElectiveOrb.exe" del /q "UninstallAutoElectiveOrb.exe"
powershell.exe -NoProfile -Command "$sources = Get-ChildItem -LiteralPath '.\src' -Filter '*.cs' | ForEach-Object FullName; Add-Type -Path $sources -ReferencedAssemblies 'System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions' -OutputAssembly '.\AutoElectiveOrb.exe' -OutputType WindowsApplication"
if errorlevel 1 exit /b 1
powershell.exe -NoProfile -Command "$sources = Get-ChildItem -LiteralPath '.\uninstaller' -Filter '*.cs' | ForEach-Object FullName; Add-Type -Path $sources -ReferencedAssemblies 'System','System.Core','System.Drawing','System.Windows.Forms' -OutputAssembly '.\UninstallAutoElectiveOrb.exe' -OutputType WindowsApplication"
if errorlevel 1 exit /b 1

echo Built %~dp0AutoElectiveOrb.exe
echo Built %~dp0UninstallAutoElectiveOrb.exe
exit /b 0
