@echo off
setlocal

set TOOL=%1
set JSON=%2

if "%TOOL%"=="" (
    echo Usage: run-json.cmd ^<tool^> [json_params]
    exit /b 2
)

if "%JSON%"=="" set JSON={}

rem Resolve project path relative to script
set PROJECT="%~dp0..\RefactorMCP.ConsoleApp\RefactorMCP.ConsoleApp.csproj"

dotnet run --project %PROJECT% -- --json %TOOL% %JSON%
