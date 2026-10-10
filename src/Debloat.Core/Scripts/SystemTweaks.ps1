# DEBLOAT — políticas da máquina (fase "specialize", roda como SYSTEM antes de criar a conta).
# Cada "#region tweak:<id>" é um ajuste do TweakCatalog; o programa remove os blocos dos ajustes desligados.

function Set-Policy([string] $Key, [string] $Name, [int] $Value) {
	reg.exe add $Key /v $Name /t REG_DWORD /d $Value /f | Out-Null
}

#region busca-escondida
# Busca da barra escondida (o Everything Toolbar entra no lugar). Só o SearchboxTaskbarMode do usuário não
# bastou no 25H2 (a caixa "Pesquisar" voltava); a política "Search on the taskbar" (0 = esconder) vale sempre.
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search' 'SearchOnTaskbarMode' 0
#endregion

#region tweak:telemetria
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'DoNotShowFeedbackNotifications' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat' 'AITEnable' 0           # telemetria de aplicativos
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat' 'DisableInventory' 1    # coletor de inventário
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat' 'DisableUAR' 1          # gravador de passos
# Mecanismo de compatibilidade e SwitchBack ficam LIGADOS de propósito (quebram programas antigos).
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting' 'Disabled' 1
# O log local do Relatório de Erros continua ligado (não envia nada e ajuda a diagnosticar travamentos).
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy' 'LetAppsAccessBackgroundSpatialPerception' 2
#endregion

#region tweak:id-anuncio
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo' 'DisabledByGroupPolicy' 1
#endregion

#region tweak:conteudo-nuvem
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableCloudOptimizedContent' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableConsumerAccountStateContent' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableSoftLanding' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsConsumerFeatures' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Speech' 'AllowSpeechModelUpdate' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\PushToInstall' 'DisablePushToInstall' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer' 'DisableGraphRecentItems' 1   # insights da conta no Explorer
#endregion

#region tweak:recomendado-iniciar
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer' 'HideRecommendedSection' 1
#endregion

#region tweak:ia-windows
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'DisableClickToDo' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'DisableSettingsAgent' 1      # busca agêntica nas Configurações
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'AllowRecallEnablement' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'DisableAIDataAnalysis' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot' 'TurnOffWindowsCopilot' 1
Set-Policy 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint' 'DisableCocreator' 1
Set-Policy 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint' 'DisableGenerativeFill' 1
Set-Policy 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint' 'DisableImageCreator' 1
Set-Policy 'HKLM\SOFTWARE\Policies\WindowsNotepad' 'DisableAIFeatures' 1
#endregion

#region tweak:pesquisa-web
$search = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search'
Set-Policy $search 'AllowCortana' 0
Set-Policy $search 'AllowCloudSearch' 0
Set-Policy $search 'AllowSearchToUseLocation' 0
Set-Policy $search 'EnableDynamicContentInWSB' 0    # destaques da pesquisa
Set-Policy $search 'AlwaysUseAutoLangDetection' 0
Set-Policy $search 'ConnectedSearchUseWeb' 0
Set-Policy $search 'DisableWebSearch' 1
#endregion

#region tweak:historico-atividades
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'EnableActivityFeed' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'PublishUserActivities' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'UploadUserActivities' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'AllowCrossDeviceClipboard' 0   # Win+V local continua
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\SettingSync' 'DisableSettingSync' 2
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\SettingSync' 'DisableSettingSyncUserOverride' 1
#endregion

#region tweak:sem-widgets
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Dsh' 'DisableWidgetsOnLockScreen' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Dsh' 'DisableWidgetsBoard' 1
#endregion

#region tweak:edge-extras
$edge = 'HKLM\SOFTWARE\Policies\Microsoft\Edge'
Set-Policy $edge 'HubsSidebarEnabled' 0             # barra lateral / Copilot
Set-Policy $edge 'EdgeShoppingAssistantEnabled' 0
Set-Policy $edge 'ShowRecommendationsEnabled' 0
Set-Policy $edge 'PersonalizationReportingEnabled' 0
#endregion

#region tweak:update-controlado
$wu = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'
Set-Policy "$wu\AU" 'NoAutoUpdate' 0
Set-Policy "$wu\AU" 'AUOptions' 4
Set-Policy "$wu\AU" 'NoAutoRebootWithLoggedOnUsers' 1
Set-Policy $wu 'DeferFeatureUpdates' 1
Set-Policy $wu 'DeferFeatureUpdatesPeriodInDays' 365
Set-Policy $wu 'DeferQualityUpdates' 0
#endregion

#region tweak:otimizacao-entrega
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' 'DODownloadMode' 0
#endregion

#region tweak:gpu-agendamento
Set-Policy 'HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode' 2
#endregion

#region tweak:sem-game-bar
# Desliga gravação/overlay e tira o app da Game Bar; o app Xbox, o Game Pass e o Modo de Jogo continuam.
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR' 'AllowGameDVR' 0
Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq 'Microsoft.XboxGamingOverlay' | Remove-AppxProvisionedPackage -Online -AllUsers -ErrorAction SilentlyContinue | Out-Null
# Sem o app, jogos e o Win+G abririam o aviso "você vai precisar de um novo aplicativo para abrir este link
# ms-gamingoverlay": esses protocolos passam a não fazer nada.
foreach( $protocol in 'ms-gamebar', 'ms-gamebarservices', 'ms-gamingoverlay' ) {
	reg.exe add "HKLM\SOFTWARE\Classes\$protocol" /ve /d "URL:$protocol" /f | Out-Null
	reg.exe add "HKLM\SOFTWARE\Classes\$protocol" /v 'URL Protocol' /d '' /f | Out-Null
	reg.exe add "HKLM\SOFTWARE\Classes\$protocol" /v 'NoOpenWith' /d '' /f | Out-Null
	reg.exe add "HKLM\SOFTWARE\Classes\$protocol\shell\open\command" /ve /d '%SystemRoot%\System32\systray.exe' /f | Out-Null
}
#endregion

#region tweak:localizar-dispositivo
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\FindMyDevice' 'AllowFindMyDevice' 0
#endregion

#region tweak:melhor-desempenho
# Sobreposição "Melhor desempenho" do plano Equilibrado. Só o SYSTEM escreve nessa chave (no primeiro login dava
# "Acesso negado" na VM), por isso fica aqui.
reg.exe add 'HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes' /v ActiveOverlayAcPowerScheme /t REG_SZ /d 'ded574b5-45a0-4f42-8737-46345c09c238' /f | Out-Null
#endregion

#region tweak:sem-historico-clipboard
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'AllowClipboardHistory' 0
#endregion

#region tweak:sem-notificacoes-bloqueio
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'DisableLockScreenAppNotifications' 1
#endregion

#region tweak:sem-proximidade
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'EnableCdp' 0
#endregion

#region tweak:sem-localizacao
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors' 'DisableLocation' 1
#endregion

#region tweak:sem-apps-segundo-plano
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy' 'LetAppsRunInBackground' 2
#endregion

#region tweak:bloquear-onedrive
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\OneDrive' 'DisableFileSyncNGSC' 1
#endregion

#region tweak:sem-drivers-update
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' 'ExcludeWUDriversInQualityUpdate' 1
#endregion

#region tweak:sem-update-loja
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\WindowsStore' 'AutoDownload' 2
#endregion

#region tweak:update-avisar
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'NoAutoUpdate' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'AUOptions' 2
#endregion

#region tweak:sem-autoplay
Set-Policy 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'NoDriveTypeAutoRun' 255
Set-Policy 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'NoAutorun' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer' 'NoAutoplayfornonVolume' 1
#endregion

#region tweak:sem-compatibilidade
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat' 'DisableEngine' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat' 'SbEnable' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat' 'DisablePCA' 1
#endregion

#region tweak:sem-nomes-8dot3
fsutil.exe behavior set disable8dot3 1 | Out-Null
#endregion

#region associacoes
# Programas padrão (VLC, Visualizador de Fotos) pela política oficial de associações. Ela vale em todo login:
# o FirstLogon agenda a remoção depois que o VLC já estiver instalado, para o usuário poder trocar depois.
$assocXml = @'
@@ASSOC@@
'@
if( $assocXml.Trim() ) {
	$assocFile = "$env:SystemRoot\System32\DEBLOAT-associacoes.xml"
	[System.IO.File]::WriteAllText( $assocFile, $assocXml.Trim(), [System.Text.Encoding]::UTF8 )
	reg.exe add 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' /v DefaultAssociationsConfiguration /t REG_SZ /d $assocFile /f | Out-Null
}
#endregion
