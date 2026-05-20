@echo off
REM Wrapper so `deploy` works from cmd.exe as well as PowerShell.
REM Forwards all args to deploy.ps1 in the same folder.
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy.ps1" %*
exit /b %ERRORLEVEL%
