@echo off
rem Izekip Shell kurulumu: yaninda paketlenen izekip.zip'i kalici bir klasore acar,
rem Baslat menusune kisayol birakir ve uygulamayi baslatir. IExpress SFX'in extraction
rem sirasinda calistirdigi betiktir; %~dp0 o anki gecici klasoru gosterir.
setlocal
set "DEST=%LocalAppData%\Programs\IzekipShell"

echo Izekip Shell kuruluyor, lutfen bekleyin...
if exist "%DEST%" rd /s /q "%DEST%" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Expand-Archive -LiteralPath '%~dp0izekip.zip' -DestinationPath '%DEST%' -Force"

if not exist "%DEST%\IzekipShell.exe" (
    echo Kurulum basarisiz oldu.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s = (New-Object -ComObject WScript.Shell).CreateShortcut('%AppData%\Microsoft\Windows\Start Menu\Programs\Izekip Shell.lnk');" ^
  "$s.TargetPath = '%DEST%\IzekipShell.exe'; $s.IconLocation = '%DEST%\Assets\app.ico'; $s.WorkingDirectory = '%DEST%'; $s.Save()"

start "" "%DEST%\IzekipShell.exe"
