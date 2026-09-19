@echo off
setlocal

rem Пересобирает KainoTranslator (Release, win-x64) и открывает папку с готовым exe.
rem Класть этот файл в корень репозитория - туда же, где лежит ScreenTranslator.sln.

cd /d "%~dp0"

if not exist "ScreenTranslator\ScreenTranslator.csproj" (
    echo [Ошибка] Рядом с build.bat не найден ScreenTranslator\ScreenTranslator.csproj.
    echo Переложите build.bat в корень репозитория ^(там же, где .sln^).
    pause
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [Ошибка] dotnet не найден в PATH. Нужен .NET 8 SDK:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo Собираю KainoTranslator (Release, win-x64)...
echo.

dotnet build "ScreenTranslator\ScreenTranslator.csproj" -c Release -r win-x64 --self-contained false > build.log 2>&1
set BUILD_RESULT=%ERRORLEVEL%
type build.log

if %BUILD_RESULT% NEQ 0 (
    echo.
    echo ============================================================
    echo   Сборка не удалась. Ошибки ^(без предупреждений^):
    echo ============================================================
    findstr /C:"error " build.log
    echo.
    echo Полный лог сохранён в build.log
    pause
    exit /b 1
)

set "EXE_PATH=%~dp0ScreenTranslator\bin\Release\net8.0-windows10.0.19041.0\win-x64\KainoTranslator.exe"

echo.
echo Готово: %EXE_PATH%
echo.

if exist "%EXE_PATH%" (
    explorer /select,"%EXE_PATH%"
) else (
    echo [Внимание] Ожидаемый exe не найден по этому пути - проверьте вывод сборки выше.
)

pause
