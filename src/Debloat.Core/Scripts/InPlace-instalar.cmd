@echo off
rem DEBLOAT — modo sem pendrive. Roda dentro do WinPE (ramdisk) e:
rem   1. acha a partição de instalação (\debloat\alvo.txt) e a partição do Windows antigo
rem      (\DEBLOAT-ALVO.txt com o MESMO token — sem token igual, não formata nada);
rem   2. confere TUDO antes de apagar: imagem do Windows, arquivo de resposta, bcdboot, partição de boot (EFI);
rem   3. formata só a partição do Windows antigo;
rem   4. aplica o Windows com DISM, injeta os drivers de hardware, refaz o boot do zero (bcdboot);
rem   5. coloca o arquivo de resposta em \Windows\Panther (fases specialize/oobe = o debloat).
rem Qualquer falha ANTES de formatar só reinicia: a entrada de boot era de uso único, o PC volta
rem para o Windows atual intacto. A partição temporária é apagada no fim do primeiro login (FirstLogon).
setlocal EnableExtensions EnableDelayedExpansion
wpeinit
set LETTERS=C D E F G H I J K L M N O P Q R S T U V W Y Z

set SRC=
for %%d in (%LETTERS%) do if exist %%d:\debloat\alvo.txt set SRC=%%d:
if not defined SRC goto :abortar
set LOG=%SRC%\debloat\instalacao.log
echo [%time%] Particao de instalacao: %SRC% > "%LOG%"
set /p TOKEN=<"%SRC%\debloat\alvo.txt"

set TGT=
for %%d in (%LETTERS%) do (
	if exist %%d:\DEBLOAT-ALVO.txt (
		set /p T=<%%d:\DEBLOAT-ALVO.txt
		if "!T!"=="%TOKEN%" if exist %%d:\Windows\System32 set TGT=%%d:
	)
)
if not defined TGT (
	echo [%time%] Particao do Windows antigo nao encontrada. Nada foi apagado. >> "%LOG%"
	goto :abortar
)
if /i "%TGT%"=="%SRC%" goto :abortar
echo [%time%] Windows antigo em %TGT%. >> "%LOG%"

rem ---- conferências: nada foi apagado ainda ----
set IMG=%SRC%\sources\install.wim
set SWM=
if not exist "%IMG%" (
	set IMG=%SRC%\sources\install.swm
	set SWM=/SWMFile:%SRC%\sources\install*.swm
)
if not exist "%IMG%" (
	echo [%time%] Imagem do Windows nao encontrada em %SRC%\sources. Nada foi apagado. >> "%LOG%"
	goto :abortar
)
if not exist "%SRC%\autounattend.xml" (
	echo [%time%] autounattend.xml nao encontrado. Nada foi apagado. >> "%LOG%"
	goto :abortar
)
if not exist "%SystemRoot%\System32\bcdboot.exe" (
	echo [%time%] bcdboot nao existe neste WinPE. Nada foi apagado. >> "%LOG%"
	goto :abortar
)
rem Partição de boot (EFI) numa letra livre (uma letra fixa podia já estar em uso).
set ESP=
for %%l in (S R Q P O N M L K) do if not defined ESP if not exist %%l:\ set ESP=%%l:
if not defined ESP goto :abortar
mountvol %ESP% /s >> "%LOG%" 2>&1 || (
	echo [%time%] Nao deu para montar a particao de boot. Nada foi apagado. >> "%LOG%"
	goto :abortar
)
if not exist %ESP%\EFI\Microsoft\Boot (
	echo [%time%] A particao de boot nao tem o boot do Windows. Nada foi apagado. >> "%LOG%"
	mountvol %ESP% /d
	goto :abortar
)
echo [%time%] Conferencias OK (imagem, resposta, bcdboot, boot em %ESP%). Formatando %TGT%. >> "%LOG%"

rem ---- daqui pra frente não tem volta ----
format %TGT% /fs:ntfs /q /y /v:Windows >> "%LOG%" 2>&1 || goto :falha

echo [%time%] Aplicando o Windows. >> "%LOG%"
dism /Apply-Image /ImageFile:"%IMG%" %SWM% /Index:1 /ApplyDir:%TGT%\ >> "%LOG%" 2>&1 || goto :falha

if exist "%SRC%\$WinPEDriver$" (
	echo [%time%] Instalando drivers de hardware. >> "%LOG%"
	dism /Image:%TGT%\ /Add-Driver /Driver:"%SRC%\$WinPEDriver$" /Recurse >> "%LOG%" 2>&1
)

echo [%time%] Refazendo o boot. >> "%LOG%"
bcdedit /export %SRC%\debloat\BCD-antes.bak >> "%LOG%" 2>&1
bcdboot %TGT%\Windows /s %ESP% /f UEFI >> "%LOG%" 2>&1 || goto :falha
rem O bcdboot põe o Windows novo como {default}, mas a entrada do Windows apagado continua no menu
rem (duplicada, 30 s de espera a cada boot). Tira só as entradas que apontam para a partição formatada;
rem a nova ({default}) e Windows de outros discos ficam.
set ID=
for /f "tokens=1,*" %%a in ('bcdedit /enum osloader') do (
	if /i "%%a"=="identifier" set ID=%%b
	rem Só identificadores de verdade ({xxxxxxxx-...}); apelidos como {default}/{current} ficam.
	if /i "%%a"=="device" if /i "%%b"=="partition=%TGT%" if "!ID:~9,1!"=="-" bcdedit /delete !ID! /f >> "%LOG%" 2>&1
)
mountvol %ESP% /d >> "%LOG%" 2>&1

mkdir %TGT%\Windows\Panther 2>nul
copy /y "%SRC%\autounattend.xml" %TGT%\Windows\Panther\unattend.xml >> "%LOG%" 2>&1 || goto :falha

echo [%time%] Pronto. Reiniciando para terminar a instalacao. >> "%LOG%"
wpeutil reboot
exit /b 0

:abortar
rem Nada foi apagado: volta para o Windows atual.
wpeutil reboot
exit /b 1

:falha
echo [%time%] FALHOU depois de formatar. Log em %LOG%. Use o pendrive do DEBLOAT para terminar. >> "%LOG%"
echo.
echo  A instalacao falhou. O registro esta em %LOG%.
echo  Grave o pendrive do DEBLOAT em outro PC e instale por ele.
echo.
cmd /k
