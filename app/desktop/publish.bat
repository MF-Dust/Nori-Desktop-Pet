@echo off
setlocal
cd /d "%~dp0"

set "APP_VERSION=%NORI_VERSION%"
if "%APP_VERSION%"=="" set "APP_VERSION=0.1.0"
set "SELF_CONTAINED=false"
if /I "%NORI_INCLUDE_RUNTIME%"=="1" set "SELF_CONTAINED=true"
if /I "%NORI_INCLUDE_RUNTIME%"=="true" set "SELF_CONTAINED=true"
set "KEEP_SYMBOLS=%NORI_KEEP_SYMBOLS%"

if /I "%NORI_SKIP_FRONTEND%"=="1" (
	echo [1/2] Skipping frontend build; using existing dist/...
) else (
	echo [1/2] Building frontend (pnpm build)...
	call pnpm build
	if errorlevel 1 goto :error
)

set "BUILD_MODE=framework-dependent"
if "%SELF_CONTAINED%"=="true" set "BUILD_MODE=self-contained"
echo [2/2] Publishing Nori.Desktop (.NET 10, win-x64, %BUILD_MODE%)...
if exist bin\publish\win-x64 rmdir /s /q bin\publish\win-x64
dotnet publish Nori.Desktop/Nori.Desktop.csproj -c Release -r win-x64 --self-contained %SELF_CONTAINED% -p:Version="%APP_VERSION%" -p:NoriSentryDsnNative="%NORI_SENTRY_DSN_NATIVE%" -p:NoriSentryRelease="%NORI_SENTRY_RELEASE%" -p:NoriSentryEnvironment="%NORI_SENTRY_ENVIRONMENT%" -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:DebugType=portable -p:DebugSymbols=true -o bin/publish/win-x64
if errorlevel 1 goto :error
if not "%KEEP_SYMBOLS%"=="1" if /I not "%KEEP_SYMBOLS%"=="true" for /r "bin\publish\win-x64" %%F in (*.pdb) do del /q "%%F"

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
