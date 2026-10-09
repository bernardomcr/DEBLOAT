# DEBLOAT — primeiro login: serviços, energia, DNS, apps e ponto de restauração.
# Os marcadores de apps e DNS (arroba-arroba) são trocados pelo programa ao gerar o autounattend.xml — não escreva o nome deles em comentários.

$root = 'C:\Debloat'
$logs = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$hasBattery = [bool](Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue)

function Write-Log([string] $File, [string] $Text) {
	"[{0:HH:mm:ss}] {1}" -f (Get-Date), $Text | Add-Content -LiteralPath (Join-Path $logs $File) -Encoding UTF8
}

# --- Telemetria: serviço e tarefas agendadas ---
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

# --- Energia: plano Equilibrado sempre; desktop ganha "Melhor desempenho" e perde a hibernação ---
powercfg.exe /setactive SCHEME_BALANCED
if( -not $hasBattery ) {
	powercfg.exe /hibernate off
	# Sobreposição "Melhor desempenho" do modo de energia (conferir em VM).
	$overlay = 'ded574b5-45a0-4f42-8737-46345c09c238'
	reg.exe add 'HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes' /v ActiveOverlayAcPowerScheme /t REG_SZ /d $overlay /f | Out-Null
}

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

function Install-Downloaded([string] $File, [string] $Arguments) {
	if( $File -like '*.msi' ) {
		$p = Start-Process -FilePath 'msiexec.exe' -ArgumentList "/i `"$File`" $Arguments" -Wait -PassThru
	} else {
		$p = Start-Process -FilePath $File -ArgumentList $Arguments -Wait -PassThru
	}
	Write-Log 'apps.log' "  instalador saiu com $($p.ExitCode)"
	Remove-Item -LiteralPath $File -ErrorAction SilentlyContinue
	return $p.ExitCode
}

function Install-FromVendor($App) {
	# Plano B quando o manifesto do winget está desatualizado: baixa do link oficial do fabricante e só
	# instala se a assinatura digital for válida E do fabricante esperado (sem hash do winget, é isso que protege).
	$file = Join-Path $env:TEMP ("debloat-" + $App.id + ".exe")
	Invoke-WebRequest -Uri $App.fallbackUrl -OutFile $file -UseBasicParsing
	$sig = Get-AuthenticodeSignature -LiteralPath $file
	if( $sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike "*$($App.signer)*" ) {
		Write-Log 'apps.log' "  plano B recusado: assinatura $($sig.Status) de '$($sig.SignerCertificate.Subject)'"
		Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue
		return
	}
	Write-Log 'apps.log' "  plano B: instalador oficial assinado por $($App.signer)"
	Install-Downloaded $file $App.args | Out-Null
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

if( $apps.Count -gt 0 ) {
	if( -not (Wait-Internet) ) { Write-Log 'apps.log' 'Sem internet: apps não instalados. Rode C:\Debloat\reinstalar-apps.ps1 depois.' }
	$winget = Get-Winget
	if( -not $winget ) { Write-Log 'apps.log' 'winget não apareceu em 5 minutos; apps do winget/Loja vão falhar.' }
	foreach( $app in $apps ) {
		Write-Log 'apps.log' "Instalando $($app.name)..."
		try {
			switch( $app.source ) {
				'feature' {
					Enable-FromMedia $app.package
					Write-Log 'apps.log' "  recurso do Windows ativado"
				}
				'winget' {
					$wingetArgs = @( 'install', '--exact', '--id', $app.package, '--source', 'winget', '--silent', '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity' )
					# --force: sem ele o x86 é pulado ("No available upgrade found") porque o x64 de mesmo ID já está instalado.
					if( $app.architecture ) { $wingetArgs += @( '--architecture', $app.architecture, '--force' ) }
					$code = Invoke-Winget $wingetArgs
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
					Invoke-WebRequest -Uri $app.package -OutFile $file -UseBasicParsing
					Install-Downloaded $file $app.args | Out-Null
				}
				'github' {
					$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$($app.package)/releases/latest" -Headers @{ 'User-Agent' = 'DEBLOAT' }
					$asset = $release.assets | Where-Object { $_.name -match $app.asset } | Select-Object -First 1
					$file = Join-Path $env:TEMP $asset.name
					Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $file -UseBasicParsing
					Install-Downloaded $file $app.args | Out-Null
				}
			}
		} catch {
			Write-Log 'apps.log' "  ERRO: $_"
		}
	}
}

# --- Sudo do Windows (24H2+) ---
if( Get-Command sudo.exe -ErrorAction SilentlyContinue ) { sudo.exe config --enable normal | Out-Null }

# --- Ponto de restauração "zero", depois que tudo foi instalado ---
Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
reg.exe add 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' /v SystemRestorePointCreationFrequency /t REG_DWORD /d 0 /f | Out-Null
Checkpoint-Computer -Description 'Instalação limpa DEBLOAT' -RestorePointType MODIFY_SETTINGS -ErrorAction SilentlyContinue
