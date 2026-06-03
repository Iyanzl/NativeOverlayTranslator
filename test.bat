@echo off
setlocal

cd /d "%~dp0"

echo [Native Overlay Translator] Test launcher
echo Project: %CD%
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet was not found in PATH.
    echo Install .NET Desktop Runtime/SDK or open this from a developer shell.
    echo.
    pause
    exit /b 1
)

if not exist "NativeOverlayTranslator.csproj" (
    echo ERROR: NativeOverlayTranslator.csproj was not found.
    echo This script must be run from the project directory.
    echo.
    pause
    exit /b 1
)

tasklist /FI "IMAGENAME eq NativeOverlayTranslator.exe" | find /I "NativeOverlayTranslator.exe" >nul
if not errorlevel 1 (
    echo Existing NativeOverlayTranslator.exe is running. Closing it before build...
    taskkill /F /IM NativeOverlayTranslator.exe >nul 2>nul
    timeout /T 1 /NOBREAK >nul
)

echo Building...
dotnet build
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Starting app...
echo Close the app window or tray icon to return here.
echo.
dotnet run --no-build
set EXITCODE=%ERRORLEVEL%

echo.
echo App exited with code %EXITCODE%.
pause
exit /b %EXITCODE%
