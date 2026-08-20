@echo off
setlocal
cd /d "%~dp0Nori.Gateway"

echo Publishing Nori.Gateway for win-x64 (framework-dependent)...
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ../publish/win-x64
if errorlevel 1 goto :error

echo Publishing Nori.Gateway for linux-x64 (framework-dependent)...
dotnet publish -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true -o ../publish/linux-x64
if errorlevel 1 goto :error

echo.
echo Publish finished: srv/publish/win-x64 and srv/publish/linux-x64
goto :end

:error
echo Gateway publish failed!
exit /b 1

:end
endlocal
