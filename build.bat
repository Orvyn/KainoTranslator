@echo off
chcp 65001 >nul
echo ============================================================
echo   KainoTranslator - sborka gotovogo .exe
echo ============================================================
echo.
echo Eto zaimet 1-2 minuty...
echo.

dotnet publish "%~dp0ScreenTranslator\ScreenTranslator.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0publish"

if %ERRORLEVEL% NEQ 0 (
  echo.
  echo ============================================================
  echo   OSHIBKA SBORKI.
  echo   Ubedites, chto ustanovlen .NET 8 SDK:
  echo   https://dotnet.microsoft.com/download/dotnet/8.0
  echo   ^(nuzhen imenno SDK, ne prosto Runtime^)
  echo ============================================================
  pause
  exit /b 1
)

echo.
echo ============================================================
echo   Gotovo! Fail KainoTranslator.exe nahoditsya v papke "publish".
echo   Otkryvayu papku...
echo ============================================================
start "" "%~dp0publish"
pause
