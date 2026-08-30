@echo off
setlocal
set ROOT=%~dp0..
set PRESET=%~1
if "%PRESET%"=="" set PRESET=fhd

set MOCK_PROJECT=%ROOT%\Testing\MockTargetApp\MockTargetApp.csproj
set MOCK=%ROOT%\Testing\MockTargetApp\bin\MockTargetApp\Debug\net8.0-windows\MockTargetApp.exe
set OVERLAY=%ROOT%\EDActivityOverlay\bin\Debug\net8.0-windows10.0.19041.0\EDActivityOverlay.exe

if not exist "%MOCK%" (
  echo MockTargetApp apphost not found. Building it explicitly...
  dotnet build "%MOCK_PROJECT%" -c Debug -p:UseAppHost=true
  if errorlevel 1 (
    echo MockTargetApp apphost build failed.
    exit /b 1
  )
)

if not exist "%MOCK%" (
  echo MockTargetApp.exe is still missing after apphost build:
  echo   %MOCK%
  echo Directory contents:
  dir "%ROOT%\Testing\MockTargetApp\bin\MockTargetApp\Debug\net8.0-windows"
  exit /b 1
)

if not exist "%OVERLAY%" (
  echo EDActivityOverlay is not built. Run .\build.ps1 first.
  exit /b 1
)

echo Starting MockTargetApp preset %PRESET%...
start "MockTargetApp" "%MOCK%" --preset %PRESET%

ping 127.0.0.1 -n 2 >nul

echo Starting EDActivityOverlay against MockTargetApp...
start "EDActivityOverlay" "%OVERLAY%" MockTargetApp

echo.
echo Runtime controls in MockTargetApp:
echo   1  1280x720
echo   2  1600x900
echo   3  1920x1080
echo   4  2560x1440
echo   5  3440x1440
echo   6  3840x2160
echo   F11  next preset
echo   Ctrl+Arrow  move target by 100 px
endlocal
