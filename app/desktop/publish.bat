@echo off
setlocal
cd /d "%~dp0"

echo [1/2] Building frontend (pnpm build)...
call pnpm build
if errorlevel 1 goto :error

echo [2/2] Publishing Nori.Desktop (.NET 10, win-x64, framework-dependent)...
dotnet publish Nori.Desktop/Nori.Desktop.csproj -c Release -r win-x64 --self-contained false -o bin/publish/win-x64
if errorlevel 1 goto :error

echo.
echo ========================================================
echo Publish succeeded: app/desktop/bin/publish/win-x64/
echo ========================================================
goto :end

:error
echo Build or publish failed!
exit /b 1

:end
endlocal
