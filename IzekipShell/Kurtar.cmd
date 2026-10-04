@echo off
rem Kabuk takilirsa: kapatir ve Windows gorev cubugunu/masaustunu geri getirir.
taskkill /f /im IzekipShell.exe >nul 2>&1
"%~dp0IzekipShell.exe" --geri-yukle
echo Windows arayuzu geri yuklendi.
timeout /t 2 >nul
