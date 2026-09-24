@echo off
setlocal
set "MacSpoofDir=%~dp0"

if exist "%MacSpoofDir%MacSpoof.exe" goto launch

set "MacSpoofDir=%~dp0MacSpoof_Fixed\"
if not exist "%MacSpoofDir%MacSpoof.exe" (
    echo MacSpoof.exe is missing. Publish the project to MacSpoof_Fixed or extract the complete portable release.
    pause
    exit /b 1
)

:launch
start "" "%MacSpoofDir%MacSpoof.exe"
endlocal
