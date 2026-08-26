@echo off
setlocal
cd /d "%~dp0"

set "APP_VERSION=%NORI_VERSION%"
if "%APP_VERSION%"=="" set "APP_VERSION=%NORI_PRODUCT_VERSION%"
if "%APP_VERSION%"=="" set "APP_VERSION=Dev"
if "%NORI_COMMIT_SHA%"=="" for /f "delims=" %%H in ('git rev-parse HEAD 2^>nul') do set "NORI_COMMIT_SHA=%%H"
set "NORI_VERSION=%APP_VERSION%"
set "NORI_PRODUCT_VERSION=%APP_VERSION%"
set "KEEP_SYMBOLS=%NORI_KEEP_SYMBOLS%"
if /I "%NORI_INCLUDE_RUNTIME%"=="1" (
	echo [错误] Windows 发布固定为 framework-dependent, 不支持打包 .NET Runtime。
	exit /b 2
)
if /I "%NORI_INCLUDE_RUNTIME%"=="true" (
	echo [错误] Windows 发布固定为 framework-dependent, 不支持打包 .NET Runtime。
	exit /b 2
)

if /I "%NORI_SKIP_FRONTEND%"=="1" (
	echo [1/2] 跳过前端构建; 使用现有 dist/...
	if not exist dist\index.html (
		echo [错误] NORI_SKIP_FRONTEND=1 但 dist\index.html 不存在。
		exit /b 2
	)
) else (
	echo [1/2] 构建前端 (pnpm build)...
	call pnpm build
	if errorlevel 1 goto :error
)

echo [2/2] 发布 Nori.Desktop (.NET 10, win-x64, framework-dependent)...
if exist bin\publish\win-x64 rmdir /s /q bin\publish\win-x64
dotnet publish Nori.Desktop/Nori.Desktop.csproj -c Release -r win-x64 --self-contained false -p:NoriProductVersion="%APP_VERSION%" -p:NoriSentryDsnNative="%NORI_SENTRY_DSN_NATIVE%" -p:NoriSentryRelease="%NORI_SENTRY_RELEASE%" -p:NoriSentryEnvironment="%NORI_SENTRY_ENVIRONMENT%" -p:PublishSingleFile=false -p:PublishReadyToRun=false -o bin/publish/win-x64
if errorlevel 1 goto :error
if not exist bin\publish\win-x64\wwwroot\index.html (
	echo [错误] 发布目录缺少 wwwroot\index.html。
	exit /b 2
)
if not "%KEEP_SYMBOLS%"=="1" if /I not "%KEEP_SYMBOLS%"=="true" for /r "bin\publish\win-x64" %%F in (*.pdb) do del /q "%%F"
node scripts\check-package-size.mjs --path bin\publish\win-x64 --label "win-x64 publish" --max-mib 80
if errorlevel 1 goto :error

echo.
echo ========================================================
echo Publish succeeded: app/desktop/bin/publish/win-x64/
echo Mode: framework-dependent (requires .NET 10 Runtime + WebView2)
echo ========================================================
goto :end

:error
echo Build or publish failed!
exit /b 1

:end
endlocal
