@echo off
setlocal
cd /d "%~dp0"
set "PROJECT=InstructorInformationSystem.csproj"
set "URL=http://localhost:5188/Account/Login"

echo Checking for the .NET 8 SDK...
dotnet --list-sdks | findstr /r "^8\." >nul
if errorlevel 1 (
	echo ERROR: The .NET 8 SDK is not installed.
	echo Install the .NET 8 SDK, then run this file again.
	pause
	exit /b 1
)

echo Restoring packages...
dotnet restore "%PROJECT%"
if errorlevel 1 goto failed

echo Building project...
dotnet build "%PROJECT%" --no-restore
if errorlevel 1 goto failed

echo Starting the application...
start "Instructor Information System" /D "%CD%" cmd /k "set ASPNETCORE_URLS=http://localhost:5188&& set ASPNETCORE_ENVIRONMENT=Development&& dotnet run --no-build --no-launch-profile"

echo Waiting for the web server at %URL% ...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$deadline = (Get-Date).AddSeconds(60); do { try { $r = Invoke-WebRequest -Uri '%URL%' -UseBasicParsing -TimeoutSec 2; if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) { exit 0 } } catch {}; Start-Sleep -Seconds 1 } while ((Get-Date) -lt $deadline); exit 1"
if errorlevel 1 (
	echo ERROR: The server did not respond within 60 seconds.
	echo Check the separate application console for the startup exception.
	pause
	exit /b 1
)

start "" "%URL%"
echo The application is running. Close its console window to stop it.
exit /b 0

:failed
echo ERROR: Restore or build failed. Review the messages above.
pause
exit /b 1
