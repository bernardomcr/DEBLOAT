# DEBLOAT — políticas da máquina (fase "specialize", roda como SYSTEM antes de criar a conta).
# Cada bloco corresponde a uma decisão do PLAN.md. Valores marcados "conferir em VM" ainda não
# foram validados numa instalação real; se o nome estiver errado, o Windows só ignora a chave.

function Set-Policy([string] $Key, [string] $Name, [int] $Value) {
	reg.exe add $Key /v $Name /t REG_DWORD /d $Value /f | Out-Null
}

$hasBattery = [bool](Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue)

# --- Telemetria e diagnóstico ---
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'DoNotShowFeedbackNotifications' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat' 'AITEnable' 0           # telemetria de aplicativos
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat' 'DisableInventory' 1    # coletor de inventário
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat' 'DisableUAR' 1          # gravador de passos
# Mecanismo de compatibilidade e SwitchBack ficam LIGADOS de propósito (quebram programas antigos).
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting' 'Disabled' 1
# O log local do Relatório de Erros continua ligado (não envia nada e ajuda a diagnosticar travamentos).
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo' 'DisabledByGroupPolicy' 1

# --- Conteúdo de nuvem, dicas e "experiências do consumidor" ---
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableCloudOptimizedContent' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableConsumerAccountStateContent' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableSoftLanding' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsConsumerFeatures' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Speech' 'AllowSpeechModelUpdate' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\PushToInstall' 'DisablePushToInstall' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer' 'DisableGraphRecentItems' 1   # insights da conta no Explorer
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer' 'HideRecommendedSection' 1    # "Recomendado" do Iniciar (conferir em VM no Pro)

# --- IA do Windows ---
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'DisableClickToDo' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'DisableSettingsAgent' 1      # busca agêntica nas Configurações (conferir em VM)
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'AllowRecallEnablement' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'DisableAIDataAnalysis' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot' 'TurnOffWindowsCopilot' 1
Set-Policy 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint' 'DisableCocreator' 1
Set-Policy 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint' 'DisableGenerativeFill' 1
Set-Policy 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint' 'DisableImageCreator' 1
Set-Policy 'HKLM\SOFTWARE\Policies\WindowsNotepad' 'DisableAIFeatures' 1

# --- Pesquisa ---
$search = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search'
Set-Policy $search 'AllowCortana' 0
Set-Policy $search 'AllowCloudSearch' 0
Set-Policy $search 'AllowSearchToUseLocation' 0
Set-Policy $search 'EnableDynamicContentInWSB' 0    # destaques da pesquisa
Set-Policy $search 'AlwaysUseAutoLangDetection' 0
Set-Policy $search 'ConnectedSearchUseWeb' 0
Set-Policy $search 'DisableWebSearch' 1

# --- Privacidade de apps (só movimento em segundo plano; apps em segundo plano continuam permitidos) ---
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy' 'LetAppsAccessBackgroundSpatialPerception' 2

# --- Histórico de atividades e sincronização ---
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'EnableActivityFeed' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'PublishUserActivities' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'UploadUserActivities' 0
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\System' 'AllowCrossDeviceClipboard' 0   # Win+V local continua
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\SettingSync' 'DisableSettingSync' 2
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\SettingSync' 'DisableSettingSyncUserOverride' 1

# --- Widgets (além do que o gerador já desliga) ---
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Dsh' 'DisableWidgetsOnLockScreen' 1
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Dsh' 'DisableWidgetsBoard' 1

# --- Edge (Chromium) ---
$edge = 'HKLM\SOFTWARE\Policies\Microsoft\Edge'
Set-Policy $edge 'HubsSidebarEnabled' 0             # barra lateral / Copilot
Set-Policy $edge 'EdgeShoppingAssistantEnabled' 0
Set-Policy $edge 'ShowRecommendationsEnabled' 0
Set-Policy $edge 'PersonalizationReportingEnabled' 0

# --- Windows Update: baixa sozinho, nunca reinicia com alguém logado, versões grandes adiadas ---
$wu = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'
Set-Policy "$wu\AU" 'NoAutoUpdate' 0
Set-Policy "$wu\AU" 'AUOptions' 4
Set-Policy "$wu\AU" 'NoAutoRebootWithLoggedOnUsers' 1
Set-Policy $wu 'DeferFeatureUpdates' 1
Set-Policy $wu 'DeferFeatureUpdatesPeriodInDays' 365
Set-Policy $wu 'DeferQualityUpdates' 0

# --- Otimização de Entrega: sem enviar atualizações para estranhos na internet ---
Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' 'DODownloadMode' 0

# --- Jogos: agendamento de GPU acelerado por hardware ---
Set-Policy 'HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode' 2

# --- Depende do hardware ---
if( -not $hasBattery ) {
	# Desktop: sem "Localizar meu dispositivo"; num notebook ele fica (acha o notebook roubado).
	Set-Policy 'HKLM\SOFTWARE\Policies\Microsoft\FindMyDevice' 'AllowFindMyDevice' 0
	# Modo de energia "Melhor desempenho" (sobreposição do plano Equilibrado). Fica aqui porque a chave só aceita
	# escrita do SYSTEM: no primeiro login deu "Acesso negado" na VM.
	reg.exe add 'HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes' /v ActiveOverlayAcPowerScheme /t REG_SZ /d 'ded574b5-45a0-4f42-8737-46345c09c238' /f | Out-Null
}
