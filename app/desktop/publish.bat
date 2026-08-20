@echo off
setlocal
cd /d "%~dp0"

echo [1/2] Building frontend (pnpm build)...
call pnpm build
if errorlevel 1 goto :error

echo [2/2] Publishing Nori.Desktop (.NET 10, win-x64, framework-dependent)...
if exist bin\publish\win-x64 rmdir /s /q bin\publish\win-x64
dotnet publish Nori.Desktop/Nori.Desktop.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false -o bin/publish/win-x64
if errorlevel 1 goto :error
for /r "bin\publish\win-x64" %%F in (*.pdb) do del /q "%%F"

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
