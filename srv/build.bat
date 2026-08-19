@echo off
REM 交叉发布网关 (取代 Go 版的 build.bat)
REM 产物为自包含单文件, 目标机器不需要预装 .NET 运行时

setlocal
cd /d "%~dp0Nori.Gateway"

dotnet publish -c Release -r win-x64 --self-contained true ^
	-p:PublishSingleFile=true -o ../publish/win-x64
if errorlevel 1 exit /b 1

dotnet publish -c Release -r linux-x64 --self-contained true ^
	-p:PublishSingleFile=true -o ../publish/linux-x64
if errorlevel 1 exit /b 1

echo.
echo 发布完成: srv/publish/win-x64 与 srv/publish/linux-x64
echo 记得把 configs/config.yaml 一并放到可执行文件同级目录
endlocal
