@echo off
rem Fabrique l'installateur Uninstallix-Setup.exe puis le lance.
rem (Remplace l'ancien Installer.bat : tout passe maintenant par le vrai installateur.)
cd /d "%~dp0"
call Creer-Setup.bat nopause
if errorlevel 1 exit /b 1
start "" "%~dp0Uninstallix-Setup.exe"
