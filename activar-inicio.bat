@echo off
rem Hace que la mascota arranque sola al encender el PC
rem (crea un acceso directo a mascota.vbs en la carpeta de Inicio de Windows)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'Mascota Claude.lnk';" ^
  "$s = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk);" ^
  "$s.TargetPath = 'wscript.exe';" ^
  "$s.Arguments = '\"%~dp0mascota.vbs\"';" ^
  "$s.WorkingDirectory = '%~dp0';" ^
  "$s.Description = 'Mascota virtual de Claude';" ^
  "$s.Save();" ^
  "Write-Host 'Listo: la mascota arrancara sola al encender el PC.'"
pause
