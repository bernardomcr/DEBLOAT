# DEBLOAT — plano e decisões

Programa de janela (Windows 11) que baixa a ISO oficial mais recente, monta uma instalação
**debloated** e grava o pendrive. Princípio central: **o preset padrão tem que ser excelente
sem o usuário mexer em nada**; personalização completa existe, mas é secundária.

Ponto de partida: o `autounattend.xml` do canal 1155 do ET (vídeo "Só consigo usar o Windows 11
assim"), gerado no [schneegans.de](https://schneegans.de/windows/unattend-generator/), e os prints
de gpedit que acompanham o vídeo. Cada item foi revisado (ganho real × o que quebra).

## Arquitetura

- **C# / .NET 10 + WPF** com [WPF-UI](https://github.com/lepoco/wpfui) (visual Fluent do Windows 11).
- `external/unattend-generator` — submódulo do gerador do schneegans (MIT). Todas as opções do
  site vêm daqui; não reescrevemos o gerador.
- `src/Debloat.Core` — preset, catálogo de apps, scripts do debloat, (depois) ISO, pendrive, backup.
  Testado em `tests/Debloat.Core.Tests`.
- `src/Debloat.App` — a janela (abas: Este PC, Windows, Debloat, Apps, Backup, Criar).

## Decisões do preset Recomendado

**Do usuário (não rediscutir):** Windows 11 **Pro com a chave genérica**; conta local de
administrador **sem senha** com login automático; pt-BR, ABNT2, horário de Brasília.

**Mantido do XML do 1155:** menu de contexto clássico, Visualizador de Fotos antigo como padrão,
Explorer em "Este Computador", extensões e ocultos visíveis, "Finalizar tarefa" na barra, sem
Widgets/Bing/sugestões, Iniciar sem pins, caminhos longos, last-access desligado, sem criptografia
automática, Edge sem boas-vindas/Startup Boost e desinstalável, sem aceleração do mouse, sem
Sticky Keys, .NET 3.5 ativado, barra à esquerda.

**Mudado em relação ao 1155:**

| Item | Decisão | Motivo |
|---|---|---|
| Plano Desempenho Máximo | Equilibrado (+ "Melhor desempenho" no desktop) | Ganho ~zero em CPU moderna, mais calor, pior bateria |
| Bloco de Notas | Mantido (IA desligada por política) | Útil, leve |
| Media Player (ZuneMusic) | Mantido | Sem ele não sobra player |
| Fala / TTS | Mantido | Narrador, Win+H, acessibilidade |
| Windows Hello rosto | Removido só sem câmera IR | Detectado no PC atual |
| Manuscrito / Math Input | Removido só sem touch/caneta | Detectado no PC atual |
| SmartScreen | Mantido | Protege muito, custa nada |
| Smart App Control | Desligado | Bloqueia programa legítimo |
| VC++ só x64 | Catálogo completo de pré-requisitos | Jogos 32 bits |
| Codecs WebP/HEIF/AV1/VP9/Raw | Mantidos | O Visualizador de Fotos usa eles |

**gpedit — rejeitado** (não aplicar): desligar Mecanismo de Compatibilidade e SwitchBack (quebram
programas antigos); desligar atualização automática da Loja (congela apps e o winget); forçar
negação de apps em segundo plano (quebra WhatsApp, alarmes, notificações); desligar o log do
Relatório de Erros; "Avisar antes de baixar" + notificações desligadas no Windows Update (na
prática nunca atualiza); não manter documentos recentes (mata Jump Lists). Sem efeito no Win 11:
"Fornecedor de local do Windows" e "Informações compartilhadas na pesquisa" (só Win 8.1).

**gpedit — aplicado:** ver `src/Debloat.Core/Scripts/SystemTweaks.ps1` (um comentário por bloco).
Windows Update: baixa e instala sozinho, nunca reinicia com usuário logado, versões grandes
adiadas 365 dias, qualidade sem atraso.

**Acrescentado:** DiagTrack e tarefas de CEIP/Appraiser desligados; Otimização de Entrega sem P2P;
gravação contínua do Xbox desligada (Game Bar e captura de tela continuam); Modo de Jogo,
otimizações para jogos em janela e HAGS ligados; Fast Startup desligado; hibernação desligada só
no desktop; Localizar Dispositivo desligado só no desktop; propagandas internas (Scoobe, OneDrive
no Explorer, Iniciar, tela de bloqueio); Edge sem sidebar/compras/recomendações; WPBT bloqueado;
apps companheiros de hardware bloqueados; modo escuro; botões nunca agrupados; segundos no
relógio; todos os ícones da bandeja; Este Computador e Lixeira na área de trabalho; "Recomendado"
do Iniciar escondido; sudo do Windows; ponto de restauração "Instalação limpa DEBLOAT" no fim.

**Nunca fazer (placebo ou perigoso):** desligar Defender, mitigações Spectre/Meltdown, paginação,
SysMain, indexação; desligar serviços em massa; HPET/bcdedit; NetworkThrottlingIndex;
Win32PrioritySeparation; mexer no WinSxS; embutir ativadores de qualquer coisa.

## Drivers

Instalação limpa não leva driver antigo nenhum. O risco real é ficar **sem internet/disco** depois:
1. Antes de formatar, exportar do PC atual **só** drivers de rede, armazenamento e chipset para
   `$WinPEDriver$` no pendrive (o Setup carrega sozinho) — e só de hardware físico presente; drivers de
   dispositivos virtuais de programas (VPN, Parsec, emulador de controle) não vão.
2. Driver de vídeo antigo **não vai junto** (é a "limpeza profunda").
3. Impedir o Windows Update de empurrar driver de vídeo genérico logo após instalar; depois
   instalar o driver oficial (NVIDIA/AMD/Intel pelo ID do hardware) e ajustar a taxa de
   atualização máxima do monitor.
4. Disco do Windows zerado (GPT novo, sem EFI/recuperação/OEM antigos). **Nunca** tocar em BIOS,
   firmware ou nos outros discos.

## Apps (catálogo em `src/Debloat.Core/Data/apps.json`)

IDs conferidos no winget em 10/2026. Fontes: `winget`, `msstore` (WhatsApp, ChatGPT), `github`
(RustDesk, que saiu do winget), `url` (XNA 3.1), `feature` (.NET 3.5 da ISO). Ideias do Ninite.
Marcados por padrão: os apps do usuário + pré-requisitos + utilitários do preset (Everything +
Everything Toolbar, PowerToys, UniGetUI, PowerShell 7). Everything Toolbar esconde a caixa de
busca do Windows (substitui ela). Compactadores: os três instalam; NanaZip vira o padrão.

Pré-requisitos: VC++ 2005–2022 AIO; DirectX June 2010; .NET Framework 3.5; .NET Desktop Runtime
3.1/5/6/7/8/9/10 em **x64 e x86**; XNA 3.1 e 4.0; OpenAL; PhysX legado e atual; GameInput;
Java 8/17/21/25 opcionais. Vulkan vem com o driver de vídeo; WebView2 já vem no Windows.

**Fora:** ativador do WeMod/Wand ou do Windows (burlar licença + script de terceiros como admin).

## DNS

Cloudflare (padrão), Cloudflare Família, AdGuard, Google, Quad9 ou o do provedor; IPv4 + IPv6 em
todas as placas físicas, com DoH automático (`EnableAutoDoh=2`). "Reaplicar DNS" na pasta
`C:\Debloat` para placas instaladas depois.

## Backup / migração (fase 2)

Destino: o próprio pendrive ou outro disco (o disco do Windows será apagado); calcula o tamanho
antes. Caminhos gravados com variáveis (`%APPDATA%`…) para funcionar com outro nome de usuário.

- **Saves de jogos** — o usuário escolhe QUAIS jogos. Três camadas:
  1. Manifesto do [Ludusavi](https://github.com/mtkennerly/ludusavi-manifest) (dados do
     PCGamingWiki, dezenas de milhares de jogos) — via CLI do Ludusavi (`mtkennerly.ludusavi`, MIT)
     ou lendo o manifesto direto.
  2. Pastas de emuladores/cracks em `Data/save-locations.json` (CODEX/PLAZA, RUNE e Goldberg
     confirmados; OnlineFix, EMPRESS, gbe_fork, SKIDROW, RELOADED etc. a confirmar em teste).
     Subpastas = AppID da Steam → nome pelo manifesto.
  3. Varredura profunda opcional: procura arquivos-marcadores de emulador (`steam_emu.ini`,
     `OnlineFix.ini`, `ALI213.ini`…) nos discos e saves locais ao lado deles.
- **Wi-Fi** com senhas (`netsh wlan export profile key=clear` → importar no primeiro login).
- **Navegadores**: Firefox inteiro (volta logado). Chromium: extensões, favoritos, histórico;
  cookies e senhas NÃO (DPAPI/app-bound encryption amarra à instalação) → sincronização da conta
  ou exportar senhas em CSV antes.
- **ShareX**: pasta `Documents\ShareX` (configuração, atalhos, destinos).
- **Pastas do usuário** escolhidas.

## Fases

1. **Núcleo** — [x] preset → autounattend.xml (gerador schneegans) · [x] catálogo de apps + DNS
   no primeiro login · [x] janela com abas e exportar XML · [x] download do Windows pelos catálogos
   da MCT (CDN da Microsoft + SHA-256, várias conexões; 25H2 5 GB em ~60 s) · [x] montar a pasta de
   instalação com DISM (testado: 232 s, boot.wim 0,56 GB + install.swm 3,7 + 2,2 GB) · [x] gravar pendrive (só disco USB, nunca
   boot/sistema, conferido de novo antes de apagar; MBR + FAT32 ativa de até 31 GiB para boot UEFI/BIOS
   + NTFS "DEBLOAT-DADOS" com o resto para os backups; bootsect) · [x] `$WinPEDriver$` só com drivers
   oem de hardware físico presente (PCI/USB/ACPI; da classe USB só controladoras PCI) — o 1º teste levou
   16 drivers virtuais (Parsec, WireGuard, TAP, ViGEm, Xbox) e o filtro foi corrigido · [ ] cache offline dos
   instaladores no pendrive · [ ] testar instalação real em VM
   (GPT/UEFI, split do install.wim >4 GB, `$WinPEDriver$` com drivers de rede/disco, instaladores
   dos apps em cache offline no pendrive).
2. **Migração e acabamento** — backup (saves, Wi-Fi, navegadores, ShareX, pastas), debloat
   direto na imagem (estilo Tiny11 seguro, sem "core"), driver de vídeo oficial + Hz máximo,
   pasta `C:\Debloat` com desfazer por ajuste e relatório "Seu PC está pronto".
3. **Reinstalar sem pendrive** — código escrito (`Media/InPlaceInstaller.cs` + `Scripts/InPlace-*`),
   **não exposto na janela até passar em VM**. `setup.exe /Auto Clean` foi descartado: a Microsoft não
   aceita arquivo de resposta com /Auto (sem debloat). Fluxo: encolhe o C: e cria `DEBLOAT-SETUP`
   (mídia + WinPE próprio) → marcador com token em `C:\DEBLOAT-ALVO.txt` → BitLocker suspenso → entrada
   de ramdisk de **uso único** (bootsequence) → no WinPE o `instalar.cmd` só formata a partição com o
   mesmo token (falha antes disso = volta ao Windows atual), aplica com DISM, injeta drivers, `bcdboot`,
   unattend em `Panther` → `SetupComplete.cmd` apaga a partição temporária, devolve o espaço ao C: e
   remove a entrada de boot. Só UEFI. Não zera EFI/recuperação (o pendrive zera). Ideia do usuário a
   seguir: se houver outro disco com espaço, a partição temporária vai nele e o disco do Windows pode
   ser zerado inteiro. Para testar em VM: gerar ISO (IMAPI2, `efisys.bin`) — Hyper-V não boota pendrive.

## Próximas tarefas combinadas (09/10/2026)

1. ✅ **Aviso na tela no primeiro login** (10/10/2026): janelinha escura no canto inferior direito,
   sempre por cima e sem roubar o foco ("Instalando apps: 12 de 48 — Google Chrome" + barra). Processo
   separado que lê C:\Debloat\progresso.txt; fecha sozinho no fim ou se o script principal morrer.
2. ✅ **Fechar as janelas de boas-vindas**: antes de cada app e 15 s depois do último, manda WM_CLOSE
   (como clicar no X) às janelas de processos que não existiam no início do script. Apps de bandeja
   continuam rodando. A conferir na rodada final da VM.
3. ✅ **Instaladores na mídia** (10/10/2026): enquanto o Windows baixa, `OfflineInstallers` baixa os
   instaladores (4 em paralelo; `winget download` com hash conferido + opções silenciosas do manifesto;
   GitHub/URL direto; Wand pelo plano B com assinatura conferida no primeiro login) para `DEBLOAT\apps`
   na ISO/pendrive, com a janela "Preparando" listando cada um. O primeiro login copia para
   `C:\Debloat\instaladores`, instala de lá e só usa internet/winget para o que faltar ou falhar.
   Medido no preset: 48 de 53 na mídia, 3,3 GB, 1,3 min. Ficam pela internet: Loja (WhatsApp, ChatGPT —
   exige conta Entra), .NET 3.5 (vem do sources\sxs), GameInput e OpenAL (sem instalador direto).
   Pendrive pequeno demais → os instaladores saem da mídia e tudo volta a baixar no primeiro login.
4. **Rodada completa final na VM** antes de usar num PC de verdade, conferindo também o Wand pelo plano B
   (curl + assinatura "WeMod LLC") e o RustDesk (GitHub).
5. VM: parar de recriar tudo a cada ajuste — checkpoint do Hyper-V logo após a instalação e testar só o
   pedaço que mudou. (O PowerShell Direct recusa conta sem senha; a edição offline do registro da VM
   falhou na letra de unidade — revisar se for usar.)
6. "Introdução" ainda aparece nas recomendações do Iniciar (HideRecommendedSection por máquina e por
   usuário e Start_TrackProgs não bastaram). Cosmético; investigar.

## Teste em VM (09/10/2026) — `tools/VmTest`

Hyper-V, Geração 2, Secure Boot + TPM, disco de 80 GB, pendrive simulado por ISO (modo `WipeDisk0`).
- ✅ WinPE do gerador particionou e aplicou o `install.swm` em ~1 min; reiniciou sozinho.
- ✅ Specialize + OOBE **sem nenhuma pergunta**, login automático na conta "Usuario" (sem senha).
- ✅ Na área de trabalho: barra à esquerda, sem Widgets/Visão de Tarefas/busca (Everything Toolbar),
  Iniciar sem fixados, modo escuro, Lixeira e Este Computador, pt-BR + ABNT2 + horário de Brasília.
- ❌ "Recomendações → Introdução" no Iniciar (HideRecommendedSection não vale no Pro) → testar
  `Start_TrackProgs=0` (já no preset).
- ❌ Edge e Loja fixados na barra → layout só com o Explorador (já no preset).
- ❓ Ícone de rede desconectado na VM: falta descobrir se é a rede da VM (Default Switch) ou algo do
  preset. Sem rede os apps não instalam. Próximo passo: `VmTest --teclar {WIN+R} C:\Debloat\logspps.log {ENTER}`
  e `ncpa.cpl` (o envio de espaço pelo teclado da VM não funciona: evitar comandos com espaço).
- Lições do testador: largura/altura do print em UInt16; o gerador exige disco ≥ 100 GB (VM usa 30);
  Progress<T> escreve de outra thread (trava no log); "exit 0" no fim dos scripts de PowerShell.

## A conferir em máquina virtual

Valores marcados "conferir em VM" nos scripts (DisableSettingsAgent, HideRecommendedSection no Pro,
sobreposição de energia), caminhos de saves não confirmados, instalação x86 dos runtimes .NET via
winget, ordem do primeiro login (internet pronta antes do winget).
