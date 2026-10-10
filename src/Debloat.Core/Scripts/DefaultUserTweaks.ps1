# DEBLOAT — preferências de usuário gravadas no perfil padrão (vale para toda conta nova).
# O gerador carrega o hive em HKU\DefaultUser antes de rodar este script. Blocos "#region tweak:<id>" = TweakCatalog.

function Set-UserValue([string] $Key, [string] $Name, [int] $Value) {
	reg.exe add "HKU\DefaultUser\$Key" /v $Name /t REG_DWORD /d $Value /f | Out-Null
}

$adv = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'

#region tweak:propagandas-usuario
Set-UserValue 'Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement' 'ScoobeSystemSettingEnabled' 0   # "Vamos terminar de configurar"
Set-UserValue $adv 'ShowSyncProviderNotifications' 0     # anúncios do OneDrive no Explorer
Set-UserValue $adv 'Start_IrisRecommendations' 0         # dicas e atalhos recomendados no Iniciar
Set-UserValue $adv 'Start_AccountNotifications' 0        # notificações de conta no Iniciar
Set-UserValue 'Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'RotatingLockScreenOverlayEnabled' 0
Set-UserValue 'Software\Microsoft\Windows\CurrentVersion\Privacy' 'TailoredExperiencesWithDiagnosticDataEnabled' 0
#endregion

#region tweak:recomendado-iniciar
Set-UserValue 'Software\Policies\Microsoft\Windows\Explorer' 'HideRecommendedSection' 1   # a versão de máquina não vale no Pro
Set-UserValue $adv 'Start_TrackProgs' 0                  # "apps adicionados recentemente"
#endregion

#region tweak:nunca-agrupar
Set-UserValue $adv 'TaskbarGlomLevel' 2
Set-UserValue $adv 'MMTaskbarGlomLevel' 2
#endregion

#region tweak:segundos-relogio
Set-UserValue $adv 'ShowSecondsInSystemClock' 1
#endregion

#region tweak:ia-windows
Set-UserValue $adv 'ShowCopilotButton' 0
#endregion

#region tweak:sem-game-bar
Set-UserValue 'Software\Microsoft\Windows\CurrentVersion\GameDVR' 'AppCaptureEnabled' 0
Set-UserValue 'System\GameConfigStore' 'GameDVR_Enabled' 0
Set-UserValue 'Software\Microsoft\GameBar' 'UseNexusForGameBarEnabled' 0   # botão Xbox do controle não abre nada
Set-UserValue 'Software\Microsoft\GameBar' 'ShowStartupPanel' 0
#endregion

#region tweak:modo-jogo
Set-UserValue 'Software\Microsoft\GameBar' 'AutoGameModeEnabled' 1
#endregion

#region tweak:jogos-janela
reg.exe add 'HKU\DefaultUser\Software\Microsoft\DirectX\UserGpuPreferences' /v DirectXUserGlobalSettings /t REG_SZ /d 'SwapEffectUpgradeEnable=1;' /f | Out-Null
#endregion
