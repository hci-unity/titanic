@echo off
rem Builds "Titanic Launcher.exe" into the game build folder (next to Titanic.exe).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize /win32icon:icon.ico ^
  /resource:background.png,background.png ^
  /out:"..\Build\Titanic\Titanic Launcher.exe" TitanicLauncher.cs
