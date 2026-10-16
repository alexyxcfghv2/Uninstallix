@echo off
rem Fabrique Uninstallix-Setup.exe : un seul fichier qui installe Uninstallix sur n'importe quel PC
rem (pas besoin d'avoir .NET installe). Le fichier est cree ici et sur le Bureau.
cd /d "%~dp0"
echo Fabrication de l'installateur (la premiere fois, cela peut prendre quelques minutes)...
if exist setup-build rmdir /s /q setup-build
dotnet publish src\Desinstalleur.App -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none -o setup-build
if errorlevel 1 (
  echo.
  echo La fabrication a echoue. Copie le message ci-dessus et envoie-le a Claude.
  pause
  exit /b 1
)
copy /y "setup-build\Uninstallix.exe" "Uninstallix-Setup.exe" >nul
for /f "usebackq delims=" %%D in (`powershell -NoProfile -Command "[Environment]::GetFolderPath('Desktop')"`) do set "BUREAU=%%D"
copy /y "Uninstallix-Setup.exe" "%BUREAU%\Uninstallix-Setup.exe" >nul
rmdir /s /q setup-build
echo.
echo C'est pret : Uninstallix-Setup.exe est sur ton Bureau.
echo Double-clique dessus pour installer Uninstallix.
if /i not "%~1"=="nopause" pause
