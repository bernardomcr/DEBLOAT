# DEBLOAT — primeiro login: serviços, energia, DNS, apps e ponto de restauração.
# Os marcadores de apps e DNS (arroba-arroba) são trocados pelo programa ao gerar o autounattend.xml — não escreva o nome deles em comentários.

$root = 'C:\Debloat'
$logs = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$hasBattery = [bool](Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue)
$started = Get-Date
$ProgressPreference = 'SilentlyContinue'   # a barra de progresso deixa o PowerShell 5.1 muito lento

function Write-Log([string] $File, [string] $Text) {
	# Tenta de novo se o arquivo estiver aberto (na VM, o log aberto no Bloco de Notas fez uma linha sumir).
	foreach( $try in 1..5 ) {
		try {
			"[{0:HH:mm:ss}] {1}" -f (Get-Date), $Text | Add-Content -LiteralPath (Join-Path $logs $File) -Encoding UTF8 -ErrorAction Stop
			return
		} catch {
			Start-Sleep -Milliseconds 300
		}
	}
}

# --- Aviso no canto da tela: o usuário vê que ainda está instalando (na VM ficou ~10 min sem sinal nenhum) ---
$progressFile = Join-Path $root 'progresso.txt'
$noticeScript = Join-Path $root 'aviso.ps1'
@'
param( [string] $Status, [int] $Parent )
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.ShowInTaskbar = $false
$form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::FromArgb( 32, 32, 32 )
$form.AutoScaleMode = 'Dpi'
$form.ClientSize = New-Object System.Drawing.Size( 380, 92 )
$form.StartPosition = 'Manual'
$area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$form.Location = New-Object System.Drawing.Point( ($area.Right - $form.Width - 16), ($area.Bottom - $form.Height - 16) )
$title = New-Object System.Windows.Forms.Label
$title.SetBounds( 16, 12, 348, 24 )
$title.ForeColor = [System.Drawing.Color]::White
$title.Font = New-Object System.Drawing.Font( 'Segoe UI Semibold', 11 )
$title.Text = 'Preparando o Windows'
$detail = New-Object System.Windows.Forms.Label
$detail.SetBounds( 16, 38, 348, 20 )
$detail.ForeColor = [System.Drawing.Color]::FromArgb( 190, 190, 190 )
$detail.Font = New-Object System.Drawing.Font( 'Segoe UI', 9 )
$detail.AutoEllipsis = $true
$track = New-Object System.Windows.Forms.Panel
$track.SetBounds( 16, 70, 348, 4 )
$track.BackColor = [System.Drawing.Color]::FromArgb( 64, 64, 64 )
$bar = New-Object System.Windows.Forms.Panel
$bar.SetBounds( 0, 0, 0, 4 )
$bar.BackColor = [System.Drawing.Color]::FromArgb( 76, 194, 255 )
$track.Controls.Add( $bar )
$form.Controls.AddRange( @( $title, $detail, $track ) )
# Aberto com janela oculta, o Windows esconderia o aviso também: mostra sem tirar o foco de quem estiver usando.
Add-Type -Namespace Debloat -Name Win -MemberDefinition '[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);'
$form.Add_Shown( { [Debloat.Win]::ShowWindow( $form.Handle, 4 ) | Out-Null } )
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 700
$timer.Add_Tick( {
	# Fecha sozinho se o script principal morrer: o aviso nunca fica preso na tela.
	if( -not (Get-Process -Id $Parent -ErrorAction SilentlyContinue) ) { $form.Close(); return }
	try {
		$lines = [System.IO.File]::ReadAllLines( $Status )
		if( $lines[0] -eq 'FIM' ) { $form.Close(); return }
		$title.Text = $lines[0]
		$detail.Text = $lines[1]
		$bar.Width = [int] ($track.Width * [Math]::Min( 100, [int] $lines[2] ) / 100)
	} catch { }
} )
$timer.Start()
[System.Windows.Forms.Application]::Run( $form )
'@ | Set-Content -LiteralPath $noticeScript -Encoding UTF8

function Set-Notice([string] $Title, [string] $Detail = '', [int] $Percent = 0) {
	foreach( $try in 1..5 ) {
		try {
			[System.IO.File]::WriteAllLines( $progressFile, [string[]] @( $Title, $Detail, $Percent ) )
			return
		} catch {
			Start-Sleep -Milliseconds 100
		}
	}
}

Set-Notice 'Preparando o Windows' 'Ajustes finais'
Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$noticeScript`" -Status `"$progressFile`" -Parent $PID"

# --- Janelas de boas-vindas: o que os instaladores abrem sozinhos (Discord, Tailscale, PowerToys...) ---
$keepWindows = @( 'explorer', 'powershell', 'pwsh', 'conhost', 'WindowsTerminal', 'SystemSettings', 'TextInputHost', 'ShellExperienceHost', 'StartMenuExperienceHost', 'SearchHost', 'LockApp' )
$baseline = @( Get-Process | ForEach-Object Id )

function Close-NewWindows {
	# WM_CLOSE (como clicar no X): apps de bandeja continuam rodando, só a janela some.
	Get-Process | Where-Object { $_.Id -notin $baseline -and $_.MainWindowHandle -ne 0 -and $_.ProcessName -notin $keepWindows } | ForEach-Object {
		Write-Log 'apps.log' "  fechando janela: $($_.ProcessName) — $($_.MainWindowTitle)"
		$_.CloseMainWindow() | Out-Null
	}
}

#region tweak:servicos-telemetria
foreach( $svc in 'DiagTrack', 'dmwappushservice' ) {
	Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
	Set-Service -Name $svc -StartupType Disabled -ErrorAction SilentlyContinue
}
@(
	'\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser'
	'\Microsoft\Windows\Application Experience\ProgramDataUpdater'
	'\Microsoft\Windows\Customer Experience Improvement Program\Consolidator'
	'\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip'
	'\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector'
) | ForEach-Object {
	$path = (Split-Path $_ -Parent) + '\'
	Disable-ScheduledTask -TaskPath $path -TaskName (Split-Path $_ -Leaf) -ErrorAction SilentlyContinue | Out-Null
}
#endregion

powercfg.exe /setactive SCHEME_BALANCED

#region tweak:desktop-sem-suspensao
powercfg.exe /change standby-timeout-ac 0
#endregion

#region tweak:sem-hibernacao
powercfg.exe /hibernate off
#endregion

#region associacoes
# A política de programas padrão (SystemTweaks) é aplicada em todo login. Remove no login seguinte, quando o
# VLC já está instalado e a associação já foi aplicada, para o usuário poder trocar depois.
$removeAction = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c timeout /t 120 && reg.exe delete HKLM\SOFTWARE\Policies\Microsoft\Windows\System /v DefaultAssociationsConfiguration /f && schtasks.exe /delete /tn DEBLOAT-associacoes /f'
Register-ScheduledTask -TaskName 'DEBLOAT-associacoes' -Action $removeAction -Trigger (New-ScheduledTaskTrigger -AtLogOn) -User 'SYSTEM' -RunLevel Highest -Force -ErrorAction SilentlyContinue | Out-Null
#endregion

# --- DNS ---
$dnsChoice = '@@DNS@@'
$dnsTable = @{
	cloudflare        = @{ v4 = '1.1.1.1', '1.0.0.1'; v6 = '2606:4700:4700::1111', '2606:4700:4700::1001'; doh = 'https://cloudflare-dns.com/dns-query' }
	cloudflare_family = @{ v4 = '1.1.1.3', '1.0.0.3'; v6 = '2606:4700:4700::1113', '2606:4700:4700::1003'; doh = 'https://family.cloudflare-dns.com/dns-query' }
	adguard           = @{ v4 = '94.140.14.14', '94.140.15.15'; v6 = '2a10:50c0::ad1:ff', '2a10:50c0::ad2:ff'; doh = 'https://dns.adguard-dns.com/dns-query' }
	google            = @{ v4 = '8.8.8.8', '8.8.4.4'; v6 = '2001:4860:4860::8888', '2001:4860:4860::8844'; doh = 'https://dns.google/dns-query' }
	quad9             = @{ v4 = '9.9.9.9', '149.112.112.112'; v6 = '2620:fe::fe', '2620:fe::9'; doh = 'https://dns.quad9.net/dns-query' }
}
if( $dnsTable.ContainsKey( $dnsChoice ) ) {
	$dns = $dnsTable[$dnsChoice]
	foreach( $server in $dns.v4 + $dns.v6 ) {
		Add-DnsClientDohServerAddress -ServerAddress $server -DohTemplate $dns.doh -AllowFallbackToUdp $true -AutoUpgrade $true -ErrorAction SilentlyContinue | Out-Null
	}
	# Usa DNS criptografado sempre que o servidor tiver modelo DoH conhecido.
	reg.exe add 'HKLM\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters' /v EnableAutoDoh /t REG_DWORD /d 2 /f | Out-Null
	Get-NetAdapter -Physical -ErrorAction SilentlyContinue | ForEach-Object {
		Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses ($dns.v4 + $dns.v6) -ErrorAction SilentlyContinue
	}
	Write-Log 'dns.log' "DNS aplicado: $dnsChoice"
}

# --- Saves de jogos (backup do DEBLOAT na partição DEBLOAT-DADOS do pendrive) ---
$dados = Get-Volume -FileSystemLabel 'DEBLOAT-DADOS' -ErrorAction SilentlyContinue | Where-Object DriveLetter | Select-Object -First 1
if( $dados ) {
	$saves = "$($dados.DriveLetter):\saves"
	$ludusavi = "$($dados.DriveLetter):\ferramentas\ludusavi.exe"
	if( (Test-Path -LiteralPath "$saves\ludusavi") -and (Test-Path -LiteralPath $ludusavi) ) {
		& $ludusavi --config "$root\ludusavi" restore --path "$saves\ludusavi" --force --api 2>&1 | Out-File -LiteralPath (Join-Path $logs 'saves-ludusavi.log') -Encoding UTF8
	}
	if( Test-Path -LiteralPath "$saves\emuladores.json" ) {
		foreach( $game in (Get-Content -LiteralPath "$saves\emuladores.json" -Raw | ConvertFrom-Json) ) {
			$target = [Environment]::ExpandEnvironmentVariables( $game.original )
			New-Item -ItemType Directory -Force -Path $target | Out-Null
			Copy-Item -Path "$saves\$($game.stored)\*" -Destination $target -Recurse -Force
			Write-Log 'saves.log' "Restaurado: $($game.name) ($($game.source)) -> $target"
		}
	}
	# Migração: Wi-Fi, ShareX, navegadores e pastas (antes dos apps: o Wi-Fi traz a internet e os navegadores
	# precisam achar o perfil antes de abrir pela primeira vez).
	$mig = "$($dados.DriveLetter):\migracao"
	if( Test-Path -LiteralPath "$mig\manifest.json" ) {
		foreach( $item in (Get-Content -LiteralPath "$mig\manifest.json" -Raw | ConvertFrom-Json) ) {
			$source = Join-Path $mig $item.stored
			if( $item.kind -eq 'Wifi' ) {
				Get-ChildItem -LiteralPath $source -Filter '*.xml' | ForEach-Object { netsh.exe wlan add profile filename="$($_.FullName)" user=all | Out-Null }
			} else {
				$target = [Environment]::ExpandEnvironmentVariables( $item.original )
				robocopy.exe $source $target /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
			}
			Write-Log 'migracao.log' "Restaurado: $($item.name)"
		}
	}
}

#region tweak:taxa-maxima
# Monitor na maior taxa de atualização da resolução atual (o Windows costuma deixar 144/165 Hz em 60 Hz).
# O driver de vídeo chega depois, pelo Windows Update: a tarefa repete no login e a cada 15 min até ele estar instalado.
$hzScript = Join-Path $root 'taxa-maxima.ps1'
@'
if( (Get-ItemProperty -Path 'HKCU:\Software\DEBLOAT' -Name TaxaMaxima -ErrorAction SilentlyContinue).TaxaMaxima -eq 1 ) { exit }
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class DebloatHz {
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct DEVMODE {
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
		public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
		public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
		public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
		public short dmLogPixels;
		public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
		public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
	}
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct DISPLAY_DEVICE {
		public int cb;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
		public int StateFlags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
	}
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr param);

	static DEVMODE New() { var dm = new DEVMODE(); dm.dmSize = (short) Marshal.SizeOf(typeof(DEVMODE)); return dm; }

	public static string Maximize() {
		var log = "";
		for (uint i = 0; ; i++) {
			var dd = new DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
			if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
			if ((dd.StateFlags & 1) == 0) continue;   // não está na área de trabalho
			var cur = New();
			if (!EnumDisplaySettings(dd.DeviceName, -1, ref cur)) continue;
			int best = cur.dmDisplayFrequency;
			var m = New();
			for (int n = 0; EnumDisplaySettings(dd.DeviceName, n, ref m); n++) {
				// Mesma resolução e cor, sem modo entrelaçado.
				if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight && m.dmBitsPerPel == cur.dmBitsPerPel
					&& (m.dmDisplayFlags & 2) == 0 && m.dmDisplayFrequency > best) best = m.dmDisplayFrequency;
			}
			if (best > cur.dmDisplayFrequency) {
				int before = cur.dmDisplayFrequency;
				cur.dmDisplayFrequency = best;
				cur.dmFields = 0x400000;   // DM_DISPLAYFREQUENCY
				int r = ChangeDisplaySettingsEx(dd.DeviceName, ref cur, IntPtr.Zero, 1, IntPtr.Zero);   // CDS_UPDATEREGISTRY
				log += dd.DeviceName + ": " + before + " -> " + best + " Hz (" + r + "); ";
			}
		}
		return log;
	}
}
"@
$result = [DebloatHz]::Maximize()
if( $result ) { "[{0:dd/MM HH:mm}] {1}" -f (Get-Date), $result | Add-Content -LiteralPath "$env:LOCALAPPDATA\DEBLOAT-taxa.log" }
# Pronto só quando nenhuma placa de vídeo está no driver genérico (aí as taxas altas já aparecem).
$generic = @( Get-CimInstance Win32_VideoController | Where-Object { $_.PNPDeviceID -like 'PCI\*' -and $_.Name -like '*Basic Display*' } )
if( $generic.Count -eq 0 ) {
	New-Item -Path 'HKCU:\Software\DEBLOAT' -Force | Out-Null
	Set-ItemProperty -Path 'HKCU:\Software\DEBLOAT' -Name TaxaMaxima -Value 1 -Type DWord
}
'@ | Set-Content -LiteralPath $hzScript -Encoding UTF8
# conhost --headless: sem a janela preta piscando a cada 15 minutos.
$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\conhost.exe" -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$hzScript`""
$triggers = @(
	(New-ScheduledTaskTrigger -AtLogOn),
	(New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes( 1 ) -RepetitionInterval (New-TimeSpan -Minutes 15) -RepetitionDuration (New-TimeSpan -Days 3))
)
$principal = New-ScheduledTaskPrincipal -GroupId 'S-1-5-32-545' -RunLevel Limited   # cada usuário, na própria sessão
Register-ScheduledTask -TaskName 'DEBLOAT-taxa-maxima' -Action $action -Trigger $triggers -Principal $principal -Force -ErrorAction SilentlyContinue | Out-Null
& "$env:SystemRoot\System32\conhost.exe" --headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File $hzScript
#endregion

# --- Apps ---
$apps = @'
@@APPS@@
'@ | ConvertFrom-Json

function Wait-Internet {
	# Mesmo teste que o Windows usa (NCSI). Ping não serve: muita rede/servidor não responde ICMP.
	$deadline = (Get-Date).AddMinutes( 5 )
	while( (Get-Date) -lt $deadline ) {
		try {
			$r = Invoke-WebRequest -Uri 'http://www.msftconnecttest.com/connecttest.txt' -UseBasicParsing -TimeoutSec 5
			if( $r.Content -like 'Microsoft Connect Test*' ) { return $true }
		} catch { }
		Start-Sleep -Seconds 3
	}
	return $false
}

function Get-Winget {
	# No primeiro login o App Installer pode ainda não estar registrado: o winget.exe existe, mas não funciona.
	Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe' -ErrorAction SilentlyContinue
	$exe = "$env:LOCALAPPDATA\Microsoft\WindowsApps\winget.exe"
	$deadline = (Get-Date).AddMinutes( 5 )
	while( (Get-Date) -lt $deadline ) {
		if( Test-Path -LiteralPath $exe ) {
			$version = & $exe --version 2>$null
			if( $version -match '^v\d' ) {
				Write-Log 'apps.log' "winget $version"
				& $exe source update --disable-interactivity 2>&1 | Out-Null
				return $exe
			}
		}
		Start-Sleep -Seconds 3
	}
	return $null
}

function Invoke-Winget([string[]] $Arguments) {
	# Guarda as últimas linhas da saída no log: "saiu com 0" sozinho já escondeu um bug.
	$output = & $winget @Arguments 2>&1 | Out-String
	$code = $LASTEXITCODE
	$tail = ($output -split "`r?`n" | Where-Object { $_ -match '[A-Za-z]{3,}' } | Select-Object -Last 1)   # só texto (as barras de progresso viravam lixo)
	Write-Log 'apps.log' "  winget saiu com $code — $tail"
	return $code
}

function Get-File([string] $Url, [string] $Path) {
	# curl.exe (vem no Windows 11): o Invoke-WebRequest do PowerShell 5.1 levava mais de 15 min para 244 MB na VM.
	& "$env:SystemRoot\System32\curl.exe" --location --fail --silent --show-error --retry 3 --max-time 1800 --output $Path $Url
	if( $LASTEXITCODE -ne 0 ) { throw "download falhou (curl $LASTEXITCODE): $Url" }
}

function Install-Downloaded([string] $File, [string] $Arguments) {
	if( $File -like '*.msi' ) {
		$p = Start-Process -FilePath 'msiexec.exe' -ArgumentList "/i `"$File`" $Arguments" -Wait -PassThru
	} elseif( $Arguments ) {
		$p = Start-Process -FilePath $File -ArgumentList $Arguments -Wait -PassThru
	} else {
		$p = Start-Process -FilePath $File -Wait -PassThru
	}
	Write-Log 'apps.log' "  instalador saiu com $($p.ExitCode)"
	Remove-Item -LiteralPath $File -ErrorAction SilentlyContinue
	return $p.ExitCode
}

function Install-FromVendor($App) {
	# Plano B quando o manifesto do winget está desatualizado: baixa do link oficial do fabricante e só
	# instala se a assinatura digital for válida E do fabricante esperado (sem hash do winget, é isso que protege).
	$file = Join-Path $env:TEMP ("debloat-" + $App.id + ".exe")
	Get-File $App.fallbackUrl $file
	$sig = Get-AuthenticodeSignature -LiteralPath $file
	if( $sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike "*$($App.signer)*" ) {
		Write-Log 'apps.log' "  plano B recusado: assinatura $($sig.Status) de '$($sig.SignerCertificate.Subject)'"
		Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue
		return
	}
	Write-Log 'apps.log' "  plano B: instalador oficial assinado por $($App.signer)"
	Install-Downloaded $file $App.args | Out-Null
}

function Install-Offline($Item) {
	# Instalador que veio na mídia (baixado quando o DEBLOAT montou o pendrive/ISO).
	$file = Join-Path $offlineDir $Item.file
	if( $Item.signer ) {
		$sig = Get-AuthenticodeSignature -LiteralPath $file
		if( $sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike "*$($Item.signer)*" ) {
			Write-Log 'apps.log' "  instalador da mídia recusado: assinatura $($sig.Status)"
			return $false
		}
	}
	if( $Item.kind -eq 'msix' ) {
		Add-AppxPackage -Path $file -ErrorAction Stop
		Write-Log 'apps.log' '  instalado da mídia'
		Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue
		return $true
	}
	$code = Install-Downloaded $file $Item.args
	return $code -in (@( 0, 1641, 3010 ) + @( $Item.successCodes ))
}

function Initialize-Online {
	# Só espera internet e winget se algum app não veio na mídia.
	if( $script:onlineReady ) { return }
	$script:onlineReady = $true
	if( -not (Wait-Internet) ) { Write-Log 'apps.log' 'Sem internet: apps que não vieram na mídia não foram instalados. Rode C:\Debloat\reinstalar-apps.ps1 depois.' }
	$script:winget = Get-Winget
	if( -not $script:winget ) { Write-Log 'apps.log' 'winget não apareceu em 5 minutos; apps do winget/Loja vão falhar.' }
}

function Enable-FromMedia([string] $Feature) {
	# Recursos como o .NET 3.5 vêm da pasta sources\sxs do pendrive, sem internet.
	foreach( $drive in [System.IO.DriveInfo]::GetDrives() ) {
		$sxs = Join-Path $drive.RootDirectory 'sources\sxs'
		if( Test-Path -LiteralPath $sxs ) {
			Enable-WindowsOptionalFeature -Online -FeatureName $Feature -Source $sxs -NoRestart -All -ErrorAction Stop | Out-Null
			return
		}
	}
	Enable-WindowsOptionalFeature -Online -FeatureName $Feature -NoRestart -All -ErrorAction Stop | Out-Null
}

# Instaladores que o DEBLOAT já pôs no pendrive/ISO. Copia antes: o pendrive pode ser tirado no meio.
$offline = @{}
$offlineDir = "$env:SystemDrive\Debloat\instaladores"
$onlineReady = $false
$winget = $null
foreach( $drive in [System.IO.DriveInfo]::GetDrives() | Where-Object IsReady ) {
	$source = Join-Path $drive.RootDirectory 'DEBLOAT\apps'
	if( Test-Path -LiteralPath "$source\offline.json" ) {
		robocopy.exe $source $offlineDir /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
		foreach( $item in (Get-Content -LiteralPath "$offlineDir\offline.json" -Raw | ConvertFrom-Json) ) { $offline[$item.id] = $item }
		Write-Log 'apps.log' "$($offline.Count) instaladores vieram na mídia ($($drive.Name))"
		break
	}
}

# Recursos do Windows (.NET 3.5) são ativados pelo sistema e levam ~5 min: rodam em paralelo com a lista.
$featureJobs = @( $apps | Where-Object source -eq 'feature' | ForEach-Object {
	Write-Log 'apps.log' "Instalando $($_.name) em paralelo..."
	Start-Job -Name $_.name -ArgumentList ${function:Enable-FromMedia}.ToString(), $_.package -ScriptBlock {
		param( [string] $Body, [string] $Feature )
		& ([scriptblock]::Create( $Body )) $Feature
	}
} )

if( $apps.Count -gt 0 ) {
	$index = 0
	foreach( $app in $apps ) {
		if( $app.source -eq 'feature' ) { $index++; continue }
		Close-NewWindows   # o que o app anterior abriu
		$index++
		Set-Notice "Instalando apps: $index de $($apps.Count)" $app.name ([int] (100 * ($index - 1) / $apps.Count))
		Write-Log 'apps.log' "Instalando $($app.name)..."
		if( $offline.ContainsKey( $app.id ) ) {
			try {
				if( Install-Offline $offline[$app.id] ) { continue }
			} catch {
				Write-Log 'apps.log' "  ERRO na mídia: $_"
			}
			Write-Log 'apps.log' '  tentando pela internet'
		}
		Initialize-Online
		try {
			switch( $app.source ) {
				'winget' {
					$wingetArgs = @( 'install', '--exact', '--id', $app.package, '--source', 'winget', '--silent', '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity' )
					# --force: sem ele o x86 é pulado ("No available upgrade found") porque o x64 de mesmo ID já está instalado.
					if( $app.architecture ) { $wingetArgs += @( '--architecture', $app.architecture, '--force' ) }
					$code = Invoke-Winget $wingetArgs
					# Segunda tentativa para falhas passageiras (na VM o instalador do Hydra travou uma vez e na outra passou).
					# Não repete "já instalado" (-1978335189) nem hash desatualizado (-1978335215): esses não mudam.
					if( $code -ne 0 -and $code -notin @( -1978335189, -1978335215 ) ) {
						Start-Sleep -Seconds 10
						$code = Invoke-Winget $wingetArgs
					}
					if( $code -ne 0 -and $app.fallbackUrl ) { Install-FromVendor $app }
				}
				'msstore' {
					$storeArgs = @( 'install', '--exact', '--id', $app.package, '--source', 'msstore', '--silent', '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity' )
					$code = Invoke-Winget $storeArgs
					if( $code -eq -1978335138 ) {
						# 0x8A15005E: o winget que vem no Windows não reconhece o certificado atual da Loja.
						# Liga a exceção documentada pela Microsoft só para esta tentativa e desliga em seguida.
						& $winget settings --enable BypassCertificatePinningForMicrosoftStore | Out-Null
						$code = Invoke-Winget $storeArgs
						& $winget settings --disable BypassCertificatePinningForMicrosoftStore | Out-Null
					}
				}
				'url' {
					$file = Join-Path $env:TEMP ([uri] $app.package).Segments[-1]
					Get-File $app.package $file
					Install-Downloaded $file $app.args | Out-Null
				}
				'github' {
					$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$($app.package)/releases/latest" -Headers @{ 'User-Agent' = 'DEBLOAT' }
					$asset = $release.assets | Where-Object { $_.name -match $app.asset } | Select-Object -First 1
					$file = Join-Path $env:TEMP $asset.name
					Get-File $asset.browser_download_url $file
					Install-Downloaded $file $app.args | Out-Null
				}
			}
		} catch {
			Write-Log 'apps.log' "  ERRO: $_"
		}
	}
}

foreach( $job in $featureJobs ) {
	Set-Notice 'Finalizando' $job.Name 100
	Wait-Job -Job $job | Out-Null
	if( $job.State -eq 'Completed' ) {
		Write-Log 'apps.log' "$($job.Name): recurso do Windows ativado"
	} else {
		Write-Log 'apps.log' "$($job.Name): ERRO $($job.ChildJobs[0].JobStateInfo.Reason)"
	}
	Remove-Job -Job $job -Force
}
Remove-Item -LiteralPath $offlineDir -Recurse -Force -ErrorAction SilentlyContinue
if( $apps.Count -gt 0 ) {
	# Alguns abrem a janela só depois de se atualizarem (o Discord leva uns segundos).
	Set-Notice 'Finalizando' 'Fechando as janelas de boas-vindas' 100
	Start-Sleep -Seconds 15
	Close-NewWindows
}
Write-Log 'apps.log' ("FIM da lista de apps em {0:N0} min" -f ((Get-Date) - $started).TotalMinutes)

#region tweak:sudo
if( Get-Command sudo.exe -ErrorAction SilentlyContinue ) { sudo.exe config --enable normal | Out-Null }
#endregion

#region tweak:ponto-restauracao
# Depois que tudo foi instalado: é o "voltar ao zero" sem formatar.
Set-Notice 'Finalizando' 'Criando o ponto de restauração' 100
Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
reg.exe add 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' /v SystemRestorePointCreationFrequency /t REG_DWORD /d 0 /f | Out-Null
Checkpoint-Computer -Description 'Instalação limpa DEBLOAT' -RestorePointType MODIFY_SETTINGS -ErrorAction SilentlyContinue
#endregion

Set-Notice 'Pronto' 'O Windows está configurado' 100
Start-Sleep -Seconds 6
Set-Notice 'FIM'
