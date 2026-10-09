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

rem Если из кэша NuGet (обычно %userprofile%\.nuget\packages) что-то удалялось вручную,
rem оставшаяся папка obj может ссылаться на пакеты, которых там больше нет, и молча собираться
rem по устаревшему, уже неверному состоянию. Стираем bin/obj и заново, принудительно (без
rem использования локального кэша) восстанавливаем пакеты - это исключает такой сценарий.
echo Очищаю bin/obj и заново восстанавливаю пакеты...
echo.
if exist "ScreenTranslator\bin" rmdir /s /q "ScreenTranslator\bin"
if exist "ScreenTranslator\obj" rmdir /s /q "ScreenTranslator\obj"

dotnet restore "ScreenTranslator\ScreenTranslator.csproj" -r win-x64 --force --no-cache > build.log 2>&1
set BUILD_RESULT=%ERRORLEVEL%
type build.log

if %BUILD_RESULT% NEQ 0 (
    echo.
    echo ============================================================
    echo   Восстановление пакетов не удалось. Ошибки ^(без предупреждений^):
    echo ============================================================
    findstr /C:"error " build.log
    echo.
    echo Полный лог сохранён в build.log
    pause
    exit /b 1
)

echo.
echo Собираю KainoTranslator (Release, win-x64)...
echo.

dotnet build "ScreenTranslator\ScreenTranslator.csproj" -c Release -r win-x64 --self-contained false --no-restore > build.log 2>&1
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

rem Не гадаем про конкретный путь (он зависит от версии SDK и менялся уже не раз) - ищем
rem KainoTranslator.exe где угодно внутри bin\Release, каким бы ни был путь до него.
set "SEARCH_DIR=%~dp0ScreenTranslator\bin\Release"
set "EXE_PATH="
for /f "delims=" %%F in ('dir /s /b "%SEARCH_DIR%\KainoTranslator.exe" 2^>nul') do set "EXE_PATH=%%F"

echo.
if defined EXE_PATH (
    echo Готово: %EXE_PATH%
    echo.
    explorer /select,"%EXE_PATH%"
) else (
    echo [Внимание] KainoTranslator.exe нигде не нашёлся внутри %SEARCH_DIR%.
    echo Сборка выше не сообщила об ошибке, но exe не появился - показываю, что реально лежит
    echo в этой папке, чтобы разобраться:
    echo.
    if exist "%SEARCH_DIR%" (
        dir /s /b "%SEARCH_DIR%"
    ) else (
        echo Папка %SEARCH_DIR% вообще не существует.
    )
    echo.
    echo Пришлите этот вывод целиком - по нему будет видно, что пошло не так.
)

pause
