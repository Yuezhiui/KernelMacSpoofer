@echo off
cd /d "%~dp0MacSpoof_Fixed"
if not exist "MacSpoof.exe" (
    echo Updated MacSpoof executable is missing. Publish the project to MacSpoof_Fixed first.
    pause
    exit /b 1
)
start "" "MacSpoof.exe"
