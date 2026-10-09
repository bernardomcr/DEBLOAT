# DEBLOAT — limpeza depois do modo sem pendrive (chamado pelo SetupComplete.cmd, como SYSTEM).
$ErrorActionPreference = 'Continue'

# Entradas de boot "DEBLOAT" (a de instalação e o dispositivo de ramdisk)
$current = $null
foreach( $line in (bcdedit.exe /enum all /v) ) {
	if( $line -match '^(identifier|identificador)\s+(\{[0-9a-f-]{36}\})' ) { $current = $Matches[2] }
	if( $line -match '^(description|descrição)\s+DEBLOAT' -and $current ) { bcdedit.exe /delete $current /f | Out-Null }
}

# Partição temporária
$setup = Get-Volume -FileSystemLabel 'DEBLOAT-SETUP' -ErrorAction SilentlyContinue | Get-Partition -ErrorAction SilentlyContinue
if( $setup ) {
	$setup | Remove-Partition -Confirm:$false
	$c = Get-Partition -DriveLetter $env:SystemDrive[0]
	$max = ($c | Get-PartitionSupportedSize).SizeMax
	if( $max -gt $c.Size ) { $c | Resize-Partition -Size $max }
}
Remove-Item -LiteralPath "$env:SystemDrive\DEBLOAT-ALVO.txt" -ErrorAction SilentlyContinue
