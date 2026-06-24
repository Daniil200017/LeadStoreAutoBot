@echo off
REM ============================================================
REM Сборка LeadStore Auto Bot (C# / WPF / .NET 9)
REM 1. dotnet publish -> single-file self-contained exe
REM 2. iscc -> Inno Setup установщик
REM ============================================================
setlocal

cd /d "%~dp0"

echo.
echo === Шаг 1: dotnet publish (Release, win-x64, single-file) ===
echo.

dotnet publish LeadStoreAutoBot\LeadStoreAutoBot.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:IncludeAllContentForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true

if errorlevel 1 (
    echo.
    echo ❌ dotnet publish ПРОВАЛИЛСЯ
    pause
    exit /b 1
)

set EXE_PATH=LeadStoreAutoBot\bin\Release\net9.0-windows\win-x64\publish\LeadStoreAutoBot.exe
if not exist "%EXE_PATH%" (
    echo ❌ EXE не найден: %EXE_PATH%
    pause
    exit /b 1
)

echo.
echo ✅ EXE собран: %EXE_PATH%
for %%A in ("%EXE_PATH%") do echo    Размер: %%~zA байт

echo.
echo === Шаг 2: Inno Setup ===
echo.

REM Поиск iscc.exe
set ISCC=
if exist "C:\Program Files (x86)\Inno Setup 6\iscc.exe"  set ISCC="C:\Program Files (x86)\Inno Setup 6\iscc.exe"
if exist "C:\Program Files\Inno Setup 6\iscc.exe"        set ISCC="C:\Program Files\Inno Setup 6\iscc.exe"
if exist "C:\Program Files (x86)\Inno Setup 5\iscc.exe"  set ISCC="C:\Program Files (x86)\Inno Setup 5\iscc.exe"

if "%ISCC%"=="" (
    echo ❌ Inno Setup не найден. Установите его: https://jrsoftware.org/isdl.php
    pause
    exit /b 1
)

%ISCC% LeadStoreAutoBot_Setup.iss
if errorlevel 1 (
    echo.
    echo ❌ Inno Setup сборка ПРОВАЛЕНА
    pause
    exit /b 1
)

echo.
echo ============================================================
echo ✅ ГОТОВО! Установщик: csharp\LeadStoreAutoBot_Setup.exe
echo ============================================================
echo.
pause
