@echo off
rem Compila mascota.cs con los tests y los ejecuta. Uso: tests\run-tests.bat (desde la raiz del repo)
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /main:MascotaTests /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:tests\tests.exe mascota.cs tests\MascotaTests.cs
if errorlevel 1 exit /b 1
tests\tests.exe
exit /b %errorlevel%
