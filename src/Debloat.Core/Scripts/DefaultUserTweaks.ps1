# DEBLOAT — preferências de usuário gravadas no perfil padrão (vale para toda conta nova).
# O gerador carrega o hive em HKU\DefaultUser antes de rodar este script.

function Set-UserValue([string] $Key, [string] $Name, [int] $Value) {
	reg.exe add "HKU\DefaultUser\$Key" /v $Name /t REG_DWORD /d $Value /f | Out-Null
}

$adv = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'

# Propagandas e "sugestões"
Set-UserValue 'Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement' 'ScoobeSystemSettingEnabled' 0   # "Vamos terminar de configurar"
Set-UserValue $adv 'ShowSyncProviderNotifications' 0     # anúncios do OneDrive no Explorer
Set-UserValue $adv 'Start_IrisRecommendations' 0         # dicas e atalhos recomendados no Iniciar
Set-UserValue $adv 'Start_AccountNotifications' 0        # notificações de conta no Iniciar
Set-UserValue $adv 'Start_TrackProgs' 0                  # "apps adicionados recentemente" (a "Introdução" aparecia por aqui na VM)
Set-UserValue 'Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'RotatingLockScreenOverlayEnabled' 0
Set-UserValue 'Software\Microsoft\Windows\CurrentVersion\Privacy' 'TailoredExperiencesWithDiagnosticDataEnabled' 0

# Barra de tarefas
Set-UserValue $adv 'TaskbarGlomLevel' 2                  # nunca agrupar botões
Set-UserValue $adv 'MMTaskbarGlomLevel' 2
Set-UserValue $adv 'ShowSecondsInSystemClock' 1
Set-UserValue $adv 'ShowCopilotButton' 0

# Jogos: Game Bar e Modo de Jogo continuam; só a gravação contínua em segundo plano sai.
Set-UserValue 'Software\Microsoft\Windows\CurrentVersion\GameDVR' 'HistoricalCaptureEnabled' 0
Set-UserValue 'Software\Microsoft\GameBar' 'AutoGameModeEnabled' 1
reg.exe add 'HKU\DefaultUser\Software\Microsoft\DirectX\UserGpuPreferences' /v DirectXUserGlobalSettings /t REG_SZ /d 'SwapEffectUpgradeEnable=1;' /f | Out-Null   # otimizações para jogos em janela
