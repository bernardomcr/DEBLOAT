namespace Debloat.Core.Presets;

/// <summary>
/// Um ajuste da lista do Debloat. Detail: uma linha curta com o que ganha/perde.
/// DefaultFor decide o valor do preset (alguns dependem do hardware). Agressivo = fora do preset.
/// Os ajustes feitos por script têm um bloco "#region tweak:&lt;id&gt;" nos .ps1; os demais viram opções
/// do gerador no UnattendBuilder.
/// </summary>
public record Tweak(string Id, string Group, string Name, string Detail, Func<HardwareProfile, bool> DefaultFor, bool Aggressive = false)
{
  public Tweak(string id, string group, string name, string detail, bool @default, bool aggressive = false)
    : this(id, group, name, detail, _ => @default, aggressive) { }
}

public static class TweakCatalog
{
  public static readonly IReadOnlyList<Tweak> All =
  [
    // Privacidade
    new("telemetria", "Privacidade", "Telemetria no mínimo", "Diagnóstico, inventário de apps e relatório de erros desligados", true),
    new("servicos-telemetria", "Privacidade", "Serviço e tarefas de telemetria desligados", "DiagTrack e tarefas de CEIP/Appraiser", true),
    new("historico-atividades", "Privacidade", "Sem histórico de atividades e sincronização", "Linha do tempo, área de transferência entre aparelhos, sync de configurações", true),
    new("id-anuncio", "Privacidade", "Sem ID de anúncio", "Apps não rastreiam você para anúncios", true),
    new("otimizacao-entrega", "Privacidade", "Atualizações sem P2P pela internet", "Não usa sua banda para enviar atualizações a outros PCs", true),
    new("localizar-dispositivo", "Privacidade", "Desligar Localizar Meu Dispositivo", "Num notebook ele ajuda a achar o aparelho roubado", hw => !hw.HasBattery),

    // Propaganda
    new("sugestoes-apps", "Propaganda e sugestões", "Sem apps sugeridos e instalados sozinhos", "Candy Crush e companhia não voltam", true),
    new("conteudo-nuvem", "Propaganda e sugestões", "Sem dicas e conteúdo da Microsoft", "Dicas, experiências do consumidor, insights da conta no Explorer", true),
    new("propagandas-usuario", "Propaganda e sugestões", "Sem anúncios no Iniciar, Explorer e tela de bloqueio", "Inclui \"Vamos terminar de configurar\" e anúncios do OneDrive", true),
    new("recomendado-iniciar", "Propaganda e sugestões", "Esconder \"Recomendado\" do Iniciar", "", true),

    // IA e pesquisa
    new("ia-windows", "IA e pesquisa", "Sem Copilot, Recall, Click to Do e IA no Paint/Bloco de Notas", "", true),
    new("pesquisa-web", "IA e pesquisa", "Pesquisa só no PC (sem Bing e sem nuvem)", "", true),

    // Windows Update
    new("update-controlado", "Windows Update", "Update automático, versões grandes adiadas 1 ano", "Segurança chega na hora; recursos novos (e bloat novo) esperam", true),
    new("update-sem-reiniciar", "Windows Update", "Nunca reiniciar com você usando o PC", "", true),
    new("update-desligado", "Windows Update", "Desligar o Windows Update", "Sem correções de segurança. Não recomendado", false, aggressive: true),

    // Segurança
    new("sem-smart-app-control", "Segurança", "Desligar Smart App Control", "Ele bloqueia muito programa legítimo", true),
    new("sem-criptografia", "Segurança", "Sem criptografia automática do disco", "Evita perder dados sem a chave do BitLocker; menos proteção se roubarem o PC", true),
    new("sem-wpbt", "Segurança", "Bloquear programas do fabricante pela BIOS (WPBT)", "", true),
    new("sem-apps-fabricante", "Segurança", "Não baixar apps de fabricante (RGB, utilitários)", "", true),
    new("sem-smartscreen", "Segurança", "Desligar SmartScreen", "Menos avisos ao baixar .exe; menos proteção contra vírus", false, aggressive: true),
    new("sem-isolamento-nucleo", "Segurança", "Desligar isolamento de núcleo", "Alguns FPS a mais em jogos; menos proteção", false, aggressive: true),
    new("sem-uac", "Segurança", "Desligar o UAC", "Nada mais pede permissão; qualquer programa vira administrador", false, aggressive: true),

    // Edge
    new("edge-boas-vindas", "Edge", "Sem tela de boas-vindas do Edge", "", true),
    new("edge-segundo-plano", "Edge", "Edge não roda em segundo plano", "Startup Boost e modo em segundo plano", true),
    new("edge-desinstalavel", "Edge", "Permitir desinstalar o Edge", "", true),
    new("edge-icone", "Edge", "Sem ícone do Edge na área de trabalho", "", true),
    new("edge-extras", "Edge", "Sem barra lateral, compras e recomendações no Edge", "", true),

    // Explorador
    new("menu-classico", "Explorador de Arquivos", "Menu de contexto clássico (botão direito completo)", "", true),
    new("abrir-este-computador", "Explorador de Arquivos", "Abrir em \"Este Computador\"", "", true),
    new("mostrar-extensoes", "Explorador de Arquivos", "Mostrar extensões dos arquivos", "", true),
    new("mostrar-ocultos", "Explorador de Arquivos", "Mostrar arquivos ocultos", "Os arquivos protegidos do sistema continuam escondidos", true),
    new("visualizador-fotos", "Explorador de Arquivos", "Visualizador de Fotos antigo como padrão", "", true),

    // Barra de tarefas e Iniciar
    new("barra-esquerda", "Barra de tarefas e Iniciar", "Barra de tarefas à esquerda", "", true),
    new("barra-so-explorador", "Barra de tarefas e Iniciar", "Só o Explorador fixado na barra", "", true),
    new("iniciar-vazio", "Barra de tarefas e Iniciar", "Iniciar sem apps fixados", "", true),
    new("sem-widgets", "Barra de tarefas e Iniciar", "Sem Widgets", "", true),
    new("sem-visao-tarefas", "Barra de tarefas e Iniciar", "Sem botão Visão de Tarefas", "", true),
    new("finalizar-tarefa", "Barra de tarefas e Iniciar", "\"Finalizar tarefa\" no botão direito da barra", "", true),
    new("nunca-agrupar", "Barra de tarefas e Iniciar", "Nunca agrupar janelas na barra", "", true),
    new("segundos-relogio", "Barra de tarefas e Iniciar", "Segundos no relógio", "", true),
    new("todos-icones-bandeja", "Barra de tarefas e Iniciar", "Todos os ícones da bandeja visíveis", "", true),
    new("modo-escuro", "Barra de tarefas e Iniciar", "Modo escuro", "", true),

    // Jogos
    new("sem-game-bar", "Jogos", "Desligar a Xbox Game Bar", "Sem Win+G, sem overlay e sem gravação; o app Xbox e o Game Pass continuam", true),
    new("gpu-agendamento", "Jogos", "Agendamento de GPU acelerado por hardware", "", true),
    new("jogos-janela", "Jogos", "Otimizações para jogos em janela", "Menos latência em jogos sem tela cheia", true),
    new("modo-jogo", "Jogos", "Modo de Jogo ligado", "", true),

    // Sistema
    new("sem-inicializacao-rapida", "Sistema", "Desligar Inicialização Rápida", "Evita bugs de driver e de atualização; boot frio um pouco mais lento", true),
    new("sem-hibernacao", "Sistema", "Desligar hibernação", "Libera vários GB; num notebook a hibernação é útil", hw => !hw.HasBattery),
    new("melhor-desempenho", "Sistema", "Modo de energia \"Melhor desempenho\"", "No notebook gasta mais bateria", hw => !hw.HasBattery),
    new("ponto-restauracao", "Sistema", "Ponto de restauração no fim da instalação", "", true),
    new("caminhos-longos", "Sistema", "Permitir caminhos longos", "", true),
    new("sem-ultimo-acesso", "Sistema", "Não gravar a data de último acesso", "Menos escrita no disco", true),
    new("scripts-powershell", "Sistema", "Permitir scripts do PowerShell", "", true),
    new("sudo", "Sistema", "Ligar o sudo do Windows", "", true),
    new("apagar-windows-old", "Sistema", "Apagar a pasta Windows.old", "", true),
    new("mouse-sem-aceleracao", "Sistema", "Mouse sem aceleração", "", true),
    new("sem-teclas-aderentes", "Sistema", "Sem Teclas de Aderência (Shift 5 vezes)", "", true),
    new("efeitos-desempenho", "Sistema", "Efeitos visuais no modo desempenho", "Sem animações e sombras; o Windows fica mais feio", false, aggressive: true),
    new("sem-sons", "Sistema", "Desligar os sons do sistema", "", false),

    // Mais privacidade (Políticas de Grupo)
    new("sem-historico-clipboard", "Privacidade", "Desligar histórico da área de transferência (Win+V)", "", false),
    new("sem-notificacoes-bloqueio", "Privacidade", "Sem notificações na tela de bloqueio", "", false),
    new("sem-proximidade", "Privacidade", "Desligar Compartilhamento por Proximidade", "Também afeta o Vincular ao Celular", false),
    new("sem-localizacao", "Privacidade", "Desligar a localização do Windows", "Fuso horário automático, clima e mapas param de funcionar", false, aggressive: true),
    new("sem-apps-segundo-plano", "Privacidade", "Bloquear apps da Loja em segundo plano", "WhatsApp e alarmes param de notificar com o app fechado", false, aggressive: true),

    // Mais propaganda
    new("bloquear-onedrive", "Propaganda e sugestões", "Impedir o OneDrive mesmo se reinstalado", "", true),
    new("sem-spotlight", "Propaganda e sugestões", "Sem Windows Spotlight (fotos e dicas na tela de bloqueio)", "", false),

    // Mais Windows Update
    new("sem-drivers-update", "Windows Update", "Não baixar drivers pelo Windows Update", "Drivers só do fabricante; o PC novo pode ficar sem algum driver", false),
    new("sem-update-loja", "Windows Update", "Não atualizar apps da Loja sozinho", "Calculadora, Terminal, codecs e winget ficam desatualizados", false, aggressive: true),
    new("update-avisar", "Windows Update", "Só avisar antes de baixar atualizações", "Na prática muita gente nunca atualiza", false, aggressive: true),

    // Mais segurança
    new("sem-autoplay", "Segurança", "Desligar Reprodução Automática (pendrive, CD)", "Evita vírus que se executam ao conectar um pendrive", true),
    new("ignorar-requisitos", "Segurança", "Instalar mesmo sem TPM 2.0 / CPU suportada", "", true),
    new("sem-login-apos-reinicio", "Segurança", "Não entrar sozinho na conta depois de um reinício do Update", "", false),
    new("acl-endurecida", "Segurança", "Endurecer permissões da unidade C:", "Usuários comuns não criam pastas na raiz do C:", false),
    new("rdp", "Segurança", "Permitir conexões de Área de Trabalho Remota (RDP)", "Para acessar este PC de outro; abre a porta 3389", false),

    // Mais Explorador
    new("mostrar-arquivos-sistema", "Explorador de Arquivos", "Mostrar também os arquivos protegidos do sistema", "Fácil apagar algo importante por engano", false, aggressive: true),
    new("sem-docs-recentes", "Explorador de Arquivos", "Não guardar documentos recentes", "Somem os Recentes e as listas de atalhos dos programas", false),
    new("sem-inicio-galeria", "Explorador de Arquivos", "Tirar \"Início\" e \"Galeria\" do painel lateral", "", true),
    new("caminho-titulo", "Explorador de Arquivos", "Caminho completo na barra de título", "", false),
    new("sem-dicas-mouse", "Explorador de Arquivos", "Sem balões de descrição ao passar o mouse", "", false),
    new("sem-junctions", "Explorador de Arquivos", "Apagar atalhos ocultos antigos (\"Documents and Settings\" etc.)", "", false),

    // Mais barra de tarefas
    new("busca-icone", "Barra de tarefas e Iniciar", "Busca da barra só como ícone", "Sem o Everything Toolbar", false),
    new("iniciar-mais-fixados", "Barra de tarefas e Iniciar", "Iniciar com mais espaço para fixados", "", false),
    new("sem-aero-shake", "Barra de tarefas e Iniciar", "Desligar \"sacudir janela para minimizar as outras\"", "", false),
    new("cor-destaque-barra", "Barra de tarefas e Iniciar", "Cor de destaque no Iniciar e na barra", "", false),
    new("sem-transparencia", "Barra de tarefas e Iniciar", "Sem transparência", "", false),

    // Área de trabalho
    new("icone-este-computador", "Área de trabalho", "Ícone Este Computador", "", true),
    new("icone-lixeira", "Área de trabalho", "Ícone Lixeira", "", true),
    new("icone-pasta-usuario", "Área de trabalho", "Ícone da pasta do usuário", "", false),
    new("icone-painel-controle", "Área de trabalho", "Ícone Painel de Controle", "", false),
    new("icone-rede", "Área de trabalho", "Ícone Rede", "", false),

    // Pastas no Iniciar (ao lado do botão de desligar)
    new("pasta-Settings", "Pastas no Iniciar", "Configurações", "", true),
    new("pasta-FileExplorer", "Pastas no Iniciar", "Explorador de Arquivos", "", true),
    new("pasta-Downloads", "Pastas no Iniciar", "Downloads", "", true),
    new("pasta-Documents", "Pastas no Iniciar", "Documentos", "", false),
    new("pasta-Pictures", "Pastas no Iniciar", "Imagens", "", false),
    new("pasta-Music", "Pastas no Iniciar", "Músicas", "", false),
    new("pasta-Videos", "Pastas no Iniciar", "Vídeos", "", false),
    new("pasta-Network", "Pastas no Iniciar", "Rede", "", false),
    new("pasta-PersonalFolder", "Pastas no Iniciar", "Pasta pessoal", "", false),

    // Teclado
    new("num-lock", "Teclado", "Num Lock ligado ao iniciar", "", hw => !hw.HasBattery),
    new("sem-caps-lock", "Teclado", "Desativar a tecla Caps Lock", "", false),

    // Mais sistema
    new("sem-animacoes", "Sistema", "Sem animações de janela e menus", "Fica mais ágil e mantém fontes suaves e miniaturas", false),
    new("desktop-sem-suspensao", "Sistema", "Nunca suspender sozinho", "A tela ainda desliga; bom para downloads e servidores de jogos", hw => !hw.HasBattery),
    new("taxa-maxima", "Sistema", "Monitor na taxa de atualização máxima", "No notebook gasta mais bateria", hw => !hw.HasBattery),
    new("sem-nomes-8dot3", "Sistema", "Sem nomes curtos 8.3 no NTFS", "Pastas com muitos arquivos ficam mais rápidas", true),
    new("sem-compatibilidade", "Sistema", "Desligar mecanismo de compatibilidade e SwitchBack", "Programas e jogos antigos podem parar de abrir", false, aggressive: true),
    new("sem-restauracao", "Sistema", "Desligar Proteção do Sistema (pontos de restauração)", "Sem como voltar o Windows sem formatar", false, aggressive: true),

    // Máquina virtual (quando o Windows vai rodar dentro de uma VM)
    new("vm-virtualbox", "Máquina virtual", "Instalar Guest Additions do VirtualBox", "", false),
    new("vm-vmware", "Máquina virtual", "Instalar VMware Tools", "", false),
    new("vm-virtio", "Máquina virtual", "Instalar drivers VirtIO e QEMU Guest Agent", "", false),
    new("vm-parallels", "Máquina virtual", "Instalar Parallels Tools", "", false),
  ];

  public static Tweak Get(string id) => All.FirstOrDefault(t => t.Id == id) ?? throw new ArgumentException($"Ajuste '{id}' não existe.");

  public static IReadOnlySet<string> DefaultsFor(HardwareProfile hardware) =>
    All.Where(t => t.DefaultFor(hardware)).Select(t => t.Id).ToHashSet();

  /// <summary>Apps removidos (IDs do gerador), com o que fica no preset. Ordem = ordem na tela.</summary>
  public static readonly IReadOnlyList<(string Id, string Name, Func<HardwareProfile, bool> Default)> Bloatware =
  [
    ("RemoveCopilot", "Copilot", _ => true), ("RemoveRecall", "Recall", _ => true), ("RemoveTeams", "Teams", _ => true),
    ("RemoveOutlook", "Outlook (novo)", _ => true), ("RemoveMailCalendar", "Email e Calendário", _ => true),
    ("RemoveOneDrive", "OneDrive", _ => true), ("RemoveOffice365", "Office 365 (atalho)", _ => true), ("RemoveOneNote", "OneNote", _ => true),
    ("RemoveClipchamp", "Clipchamp", _ => true), ("RemoveNews", "Notícias", _ => true), ("RemoveWeather", "Clima", _ => true),
    ("RemoveMaps", "Mapas", _ => true), ("RemoveToDo", "To Do", _ => true), ("RemovePeople", "Pessoas", _ => true),
    ("RemoveSolitaire", "Paciência", _ => true), ("RemoveSkype", "Skype", _ => true), ("RemoveFamily", "Família", _ => true),
    ("RemoveGetHelp", "Obter Ajuda", _ => true), ("RemoveGetStarted", "Dicas / Introdução", _ => true), ("RemoveFeedbackHub", "Hub de Comentários", _ => true),
    ("RemoveBingSearch", "Pesquisa Bing", _ => true), ("RemoveCortana", "Cortana", _ => true), ("RemoveDevHome", "Dev Home", _ => true),
    ("RemovePowerAutomate", "Power Automate", _ => true), ("RemoveQuickAssist", "Assistência Rápida", _ => true), ("RemoveWallet", "Carteira", _ => true),
    ("RemoveYourPhone", "Vincular ao Celular", _ => true), ("RemoveMixedReality", "Realidade Misturada", _ => true), ("Remove3DViewer", "Visualizador 3D", _ => true),
    ("RemovePaint3D", "Paint 3D", _ => true), ("RemoveZuneVideo", "Filmes e TV", _ => true), ("RemoveStepsRecorder", "Gravador de Passos", _ => true),
    ("RemoveOneSync", "OneSync", _ => true), ("RemovePhotos", "Fotos", _ => true),
    ("RemoveWindowsHello", "Windows Hello por rosto", hw => !hw.HasIrCamera),
    ("RemoveHandwriting", "Manuscrito", hw => !hw.HasPenOrTouch), ("RemoveMathInputPanel", "Painel de Matemática", hw => !hw.HasPenOrTouch),
    ("RemoveZuneMusic", "Media Player (vira o VLC)", _ => true),
    ("RemoveNotepad", "Bloco de Notas", _ => false), ("RemoveSpeech", "Fala / Narrador", _ => false), ("RemoveRdpClient", "Área de Trabalho Remota", _ => false),
    ("RemoveXboxApps", "Apps do Xbox (quebra o Game Pass)", _ => false), ("RemoveStickyNotes", "Notas Autoadesivas", _ => false),
    ("RemoveVoiceRecorder", "Gravador de Som", _ => false), ("RemoveCamera", "Câmera", _ => false), ("RemoveClock", "Relógio", _ => false),
    ("RemoveCalculator", "Calculadora", _ => false), ("RemoveSnippingTool", "Ferramenta de Captura", _ => false), ("RemovePaint", "Paint", _ => false),
    ("RemoveWindowsTerminal", "Terminal", _ => false), ("RemoveStore", "Microsoft Store (quebra WhatsApp e ChatGPT)", _ => false),
    ("RemoveInternetExplorer", "Modo Internet Explorer", _ => true), ("RemoveWordPad", "WordPad", _ => true),
    ("RemovePowerShell2", "PowerShell 2.0 (antigo)", _ => true), ("RemoveGameAssist", "Assistente de jogos do Edge", _ => true),
    ("RemovePowerShellISE", "PowerShell ISE", _ => false), ("RemoveOpenSSHClient", "Cliente OpenSSH", _ => false),
    ("RemoveWindowsMediaPlayer", "Windows Media Player clássico", _ => false), ("RemoveNotepadClassic", "Bloco de Notas clássico", _ => false),
    ("RemoveMediaFeatures", "Recursos de mídia (quebra apps de vídeo)", _ => false),
  ];
}
