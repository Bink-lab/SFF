@echo off
echo Building DepotDL.CLI...
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed!
    pause
    exit /b %ERRORLEVEL%
)
echo [SUCCESS] Build succeeded!
echo Executable is located in: bin\Release\net9.0\DepotDL.CLI.exe
pause
