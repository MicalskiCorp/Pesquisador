@echo off
rem Duplo-clique para configurar a máquina. Roda configurar.ps1 sem depender da política de
rem scripts do Windows, que por padrão recusa qualquer .ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0configurar.ps1" %*
echo.
pause
