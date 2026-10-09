@echo off
rem DEBLOAT — modo sem pendrive. Roda dentro do WinPE (ramdisk) e:
rem   1. acha a partição de instalação (\debloat\alvo.txt) e a partição do Windows antigo
rem      (\DEBLOAT-ALVO.txt com o MESMO token — sem token igual, não formata nada);
rem   2. formata só a partição do Windows antigo;
rem   3. aplica o Windows com DISM, injeta os drivers de hardware, recria o boot (bcdboot);
rem   4. coloca o arquivo de resposta em \Windows\Panther (fases specialize/oobe = o debloat).
rem Qualquer falha ANTES de formatar só reinicia: a entrada de boot era de uso único, o PC volta
rem para o Windows atual intacto.
setlocal EnableExtensions EnableDelayedExpansion
wpeinit
set LETTERS=C D E F G H I J K L M N O P Q R S T U V W

set SRC=
for %%d in (%LETTERS%) do if exist %%d:\debloat\alvo.txt set SRC=%%d:
if not defined SRC goto :abortar
set LOG=%SRC%\debloat\instalacao.log
echo [%time%] Partição de instalação: %SRC% > "%LOG%"
set /p TOKEN=<"%SRC%\debloat\alvo.txt"

set TGT=
for %%d in (%LETTERS%) do (
	if exist %%d:\DEBLOAT-ALVO.txt (
		set /p T=<%%d:\DEBLOAT-ALVO.txt
		if "!T!"=="%TOKEN%" if exist %%d:\Windows\System32 set TGT=%%d:
	)
)
if not defined TGT (
	echo [%time%] Partição do Windows antigo não encontrada. Nada foi apagado. >> "%LOG%"
	goto :abortar
)
if /i "%TGT%"=="%SRC%" goto :abortar
echo [%time%] Windows antigo em %TGT%. Formatando. >> "%LOG%"

rem ---- daqui pra frente não tem volta ----
format %TGT% /fs:ntfs /q /y /v:Windows >> "%LOG%" 2>&1 || goto :falha

set IMG=%SRC%\sources\install.wim
set SWM=
if not exist "%IMG%" (
	set IMG=%SRC%\sources\install.swm
	set SWM=/SWMFile:%SRC%\sources\install*.swm
)
echo [%time%] Aplicando o Windows. >> "%LOG%"
dism /Apply-Image /ImageFile:"%IMG%" %SWM% /Index:1 /ApplyDir:%TGT%\ >> "%LOG%" 2>&1 || goto :falha

if exist "%SRC%\$WinPEDriver$" (
	echo [%time%] Instalando drivers de hardware. >> "%LOG%"
	dism /Image:%TGT%\ /Add-Driver /Driver:"%SRC%\$WinPEDriver$" /Recurse >> "%LOG%" 2>&1
)

echo [%time%] Recriando o boot. >> "%LOG%"
mountvol Y: /s >> "%LOG%" 2>&1 || goto :falha
bcdboot %TGT%\Windows /s Y: /f UEFI >> "%LOG%" 2>&1 || goto :falha

mkdir %TGT%\Windows\Panther 2>nul
copy /y "%SRC%\autounattend.xml" %TGT%\Windows\Panther\unattend.xml >> "%LOG%" 2>&1 || goto :falha
mkdir %TGT%\Windows\Setup\Scripts 2>nul
copy /y "%SRC%\debloat\SetupComplete.cmd" %TGT%\Windows\Setup\Scripts\SetupComplete.cmd >> "%LOG%" 2>&1
copy /y "%SRC%\debloat\limpar-particao.ps1" %TGT%\Windows\Setup\Scripts\limpar-particao.ps1 >> "%LOG%" 2>&1

echo [%time%] Pronto. Reiniciando para terminar a instalação. >> "%LOG%"
wpeutil reboot
exit /b 0

:abortar
rem Nada foi apagado: volta para o Windows atual.
wpeutil reboot
exit /b 1

:falha
echo [%time%] FALHOU depois de formatar. Log em %LOG%. Use o pendrive do DEBLOAT para terminar. >> "%LOG%"
echo.
echo  A instalação falhou. O registro está em %LOG%.
echo  Grave o pendrive do DEBLOAT em outro PC e instale por ele.
echo.
cmd /k
