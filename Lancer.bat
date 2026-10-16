@echo off
rem Compile puis lance Uninstallix (Windows demandera les droits administrateur).
cd /d "%~dp0"
echo Compilation en cours...
dotnet build src\Desinstalleur.App -c Release -o publish
if errorlevel 1 (
  echo.
  echo La compilation a echoue. Copie le message ci-dessus et envoie-le a Claude.
  pause
  exit /b 1
)
start "" "publish\Uninstallix.exe"
