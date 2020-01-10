@ECHO off
SETLOCAL

PowerShell -ExecutionPolicy bypass -File "./Init.ps1"

ENDLOCAL

IF %ERRORLEVEL% NEQ 0 GOTO err
PAUSE
EXIT /B 0
:err
PAUSE
EXIT /B 1

