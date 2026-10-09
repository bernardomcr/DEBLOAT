@echo off
rem DEBLOAT — roda uma vez, como SYSTEM, no fim da instalação do modo sem pendrive:
rem apaga a partição temporária DEBLOAT-SETUP, devolve o espaço ao C: e tira a entrada de boot.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0limpar-particao.ps1" > "%WINDIR%\Setup\Scripts\limpar-particao.log" 2>&1
