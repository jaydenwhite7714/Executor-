@echo off
setlocal enabledelayedexpansion
title Driven Test Engine - build

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [error] The .NET SDK was not found on PATH.
    echo         Install the .NET 8 SDK ^(or newer^) from https://dot.net and run this script again.
    exit /b 1
)

echo == .NET SDK ==
dotnet --version
echo.

echo == Restore + build (Release) ==
dotnet build "%~dp0DrivenTestEngine.sln" -c Release -v minimal
if errorlevel 1 (
    echo [error] Build failed.
    exit /b 1
)

echo.
echo == Publish (framework-dependent, win-x64) ==
dotnet publish "%~dp0src\DrivenTestEngine.App\DrivenTestEngine.App.csproj" -c Release -r win-x64 --self-contained false -o "%~dp0publish"
if errorlevel 1 (
    echo [error] Publish failed.
    exit /b 1
)

echo.
echo [ok] Executable: %~dp0publish\DrivenTestEngine.exe
echo      Requires the .NET 8 Desktop Runtime (or newer) on this machine.
endlocal
