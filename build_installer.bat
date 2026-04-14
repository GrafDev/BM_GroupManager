@echo off
setlocal

set SCRIPT_DIR=%~dp0
set VERSION_FILE=%SCRIPT_DIR%version.txt
set ISS_FILE=%SCRIPT_DIR%installer.iss
set INNO=%LocalAppData%\Programs\Inno Setup 6\Compil32.exe
set MSBUILD=dotnet

echo ============================================================
echo  BM Smart Group Manager — Build + Package
echo ============================================================

:: ── 1. Читаем версию ──────────────────────────────────────────
set /p VERSION=<%VERSION_FILE%
set VERSION=%VERSION: =%
echo  Version : %VERSION%

:: ── 2. Сборка Release ─────────────────────────────────────────
echo.
echo [1/2] Building Release...
%MSBUILD% build "%SCRIPT_DIR%BM_GroupManager.csproj" -c Release
if errorlevel 1 (
    echo  ERROR: Build failed!
    pause
    exit /b 1
)
echo  Build OK.

:: ── 3. Создаём папку для инсталятора ──────────────────────────
if not exist "%SCRIPT_DIR%Build\Installer" mkdir "%SCRIPT_DIR%Build\Installer"

:: ── 4. Компилируем инсталятор ─────────────────────────────────
echo.
echo [2/2] Compiling installer...
"%INNO%" /cc /DAppVersion="%VERSION%" "%ISS_FILE%"
if errorlevel 1 (
    echo  ERROR: Inno Setup compilation failed!
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Done! Installer: Build\Installer\BM_GroupManager_v%VERSION%_Setup.exe
echo ============================================================
pause
