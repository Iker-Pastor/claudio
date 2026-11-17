@echo off
rem Hace que la mascota NO arranque al encender el PC
rem (borra el acceso directo de la carpeta de Inicio de Windows)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'Mascota Claude.lnk';" ^
  "if (Test-Path $lnk) { Remove-Item $lnk; Write-Host 'Listo: la mascota ya no arrancara al encender el PC.' }" ^
  "else { Write-Host 'No estaba activado el arranque automatico.' }"
pause
