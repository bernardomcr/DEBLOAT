using System.Collections.Immutable;
using System.Drawing;
using System.Xml;
using Debloat.Core.Catalog;
using Schneegans.Unattend;

namespace Debloat.Core.Presets;

/// <summary>Transforma <see cref="DebloatOptions"/> num autounattend.xml usando o gerador do schneegans.</summary>
public sealed class UnattendBuilder
{
  private readonly UnattendGenerator generator = new();
  private readonly AppCatalog catalog;

  public UnattendBuilder(AppCatalog? catalog = null)
  {
    this.catalog = catalog ?? AppCatalog.Load();
  }

  public byte[] BuildBytes(DebloatOptions options) => UnattendGenerator.Serialize(Build(options));

  public XmlDocument Build(DebloatOptions options)
  {
    var apps = catalog.Resolve(options.SelectedApps ?? catalog.Defaults.Select(a => a.Id));
    bool Has(string tweak) => options.Has(tweak);
    bool vlc = apps.Any(a => a.Id == "vlc");
    bool everythingToolbar = apps.Any(a => a.Id == "everything-toolbar");

    // Sem o VLC, tirar o Media Player deixaria o PC sem player de vídeo/música.
    var removed = options.EffectiveRemovedApps.Where(id => id != "RemoveZuneMusic" || vlc);

    var config = Configuration.Default with
    {
      LanguageSettings = new UnattendedLanguageSettings(
        ImageLanguage: generator.Lookup<ImageLanguage>(LanguageId(options.Language)),
        LocaleAndKeyboard: new LocaleAndKeyboard(
          generator.Lookup<UserLocale>(options.Locale),
          generator.Lookup<KeyboardIdentifier>(options.Keyboard)),
        LocaleAndKeyboard2: null,
        LocaleAndKeyboard3: null,
        GeoLocation: generator.Lookup<GeoLocation>(options.GeoId)),
      AccountSettings = new UnattendedAccountSettings(
        accounts: [new Account(options.UserName, "", options.Password, "Administrators")],
        autoLogonSettings: new OwnAutoLogonSettings(),
        obscurePasswords: false),
      PasswordExpirationSettings = new UnlimitedPasswordExpirationSettings(),
      TimeZoneSettings = new ExplicitTimeZoneSettings(generator.Lookup<TimeOffset>(options.TimeZone)),
      WifiSettings = new SkipWifiSettings(),
      PESettings = options.WipeDisk0
        ? new GeneratePESettings(
            PartitionSettings: new UnattendedPartitionSettings(
              new GeneratedTargetDiskSettings(minSizeGiB: 30, index: 0, assertNoPartitions: false), PartitionLayout.GPT, RecoveryMode.Partition),
            InstallFromSettings: new IndexInstallFromSettings(1),     // a mídia do DEBLOAT só tem a edição escolhida
            PagingFileSettings: new AutomaticPagingFileSettings(),
            DisableDefender: false, Disable8Dot3Names: false, PauseBeforeFormatting: false, PauseBeforeReboot: false,
            CompactOs: false, SkipIntegrityCheck: false)
        : new DefaultPESettings(
            EditionSettings: new UnattendedEditionSettings(generator.Lookup<WindowsEdition>(options.Edition)),
            BypassRequirementsCheck: Has("ignorar-requisitos")),
      ActivationKey = options.WipeDisk0 ? new ProductKey(GenericKeys[options.Edition]) : null,
      Bloatwares = removed.Select(generator.Lookup<Bloatware>).ToImmutableList(),
      ExpressSettings = ExpressSettingsMode.DisableAll,
      ScriptSettings = new ScriptSettings(Scripts(options, apps, vlc), RestartExplorer: true),
      HidePowerShellWindows = true,

      // Ajustes da lista (TweakCatalog) que são opções do próprio gerador
      EnableLongPaths = Has("caminhos-longos"),
      AllowPowerShellScripts = Has("scripts-powershell"),
      DisableLastAccess = Has("sem-ultimo-acesso"),
      PreventAutomaticReboot = Has("update-sem-reiniciar"),
      DisableWindowsUpdate = Has("update-desligado"),
      DisableSac = Has("sem-smart-app-control"),
      DisableSmartScreen = Has("sem-smartscreen"),
      DisableUac = Has("sem-uac"),
      DisableCoreIsolation = Has("sem-isolamento-nucleo"),
      DisableFastStartup = Has("sem-inicializacao-rapida"),
      DisableAppSuggestions = Has("sugestoes-apps"),
      DisableWidgets = Has("sem-widgets"),
      PreventDeviceEncryption = Has("sem-criptografia"),
      DisableWpbt = Has("sem-wpbt"),
      PreventDeviceApps = Has("sem-apps-fabricante"),
      DeleteWindowsOld = Has("apagar-windows-old"),
      TurnOffSystemSounds = Has("sem-sons"),
      DisableAutomaticRestartSignOn = Has("sem-login-apos-reinicio"),
      HardenSystemDriveAcl = Has("acl-endurecida"),
      EnableRemoteDesktop = Has("rdp"),
      DisableSystemRestore = Has("sem-restauracao"),
      HideInfoTip = Has("sem-dicas-mouse"),
      DeleteJunctions = Has("sem-junctions"),
      VBoxGuestAdditions = Has("vm-virtualbox"),
      VMwareTools = Has("vm-vmware"),
      VirtIoGuestTools = Has("vm-virtio"),
      ParallelsTools = Has("vm-parallels"),
      HideEdgeFre = Has("edge-boas-vindas"),
      DisableEdgeStartupBoost = Has("edge-segundo-plano"),
      MakeEdgeUninstallable = Has("edge-desinstalavel"),
      DeleteEdgeDesktopIcon = Has("edge-icone"),
      ClassicContextMenu = Has("menu-classico"),
      LaunchToThisPC = Has("abrir-este-computador"),
      ShowFileExtensions = Has("mostrar-extensoes"),
      HideFiles = Has("mostrar-arquivos-sistema") ? HideModes.None : Has("mostrar-ocultos") ? HideModes.HiddenSystem : HideModes.Hidden,
      ShowEndTask = Has("finalizar-tarefa"),
      ShowAllTrayIcons = Has("todos-icones-bandeja"),
      LeftTaskbar = Has("barra-esquerda"),
      HideTaskViewButton = Has("sem-visao-tarefas"),
      DisableBingResults = Has("pesquisa-web"),
      TaskbarSearch = everythingToolbar ? TaskbarSearchMode.Hide : Has("busca-icone") ? TaskbarSearchMode.Icon : TaskbarSearchMode.Box,
      StartPinsSettings = Has("iniciar-vazio") ? new EmptyStartPinsSettings() : new DefaultStartPinsSettings(),
      TaskbarIcons = Has("barra-so-explorador") ? new CustomTaskbarIcons(TaskbarExplorerOnly) : new DefaultTaskbarIcons(),
      DesktopIcons = new CustomDesktopIconSettings(DesktopIconIds
        .ToDictionary(i => generator.Lookup<DesktopIcon>(i.Icon), i => Has(i.Tweak))),
      StartFolderSettings = StartFolderIds.Any(id => Has("pasta-" + id))
        ? new CustomStartFolderSettings(StartFolderIds.ToDictionary(id => generator.StartFolders[id], id => Has("pasta-" + id)))
        : new DefaultStartFolderSettings(),
      LockKeySettings = Has("num-lock") || Has("sem-caps-lock")
        ? new ConfigureLockKeySettings(
            CapsLock: new LockKeySetting(LockKeyInitial.Off, Has("sem-caps-lock") ? LockKeyBehavior.Ignore : LockKeyBehavior.Toggle),
            NumLock: new LockKeySetting(Has("num-lock") ? LockKeyInitial.On : LockKeyInitial.Off, LockKeyBehavior.Toggle),
            ScrollLock: new LockKeySetting(LockKeyInitial.Off, LockKeyBehavior.Toggle))
        : new SkipLockKeySettings(),
      ColorSettings = Has("modo-escuro") || Has("cor-destaque-barra") || Has("sem-transparencia")
        ? new CustomColorSettings(
            Has("modo-escuro") ? ColorTheme.Dark : ColorTheme.Light, Has("modo-escuro") ? ColorTheme.Dark : ColorTheme.Light,
            EnableTransparency: !Has("sem-transparencia"), AccentColorOnStart: Has("cor-destaque-barra"), AccentColorOnBorders: false,
            AccentColor: Color.FromArgb(0x00, 0x78, 0xD4))
        : new DefaultColorSettings(),
      Effects = Has("efeitos-desempenho") ? new BestPerformanceEffects()
        : Has("sem-animacoes") ? new CustomEffects(Enum.GetValues<Effect>().ToImmutableDictionary(e => e, e => !Animations.Contains(e)))
        : new DefaultEffects(),
      DisablePointerPrecision = Has("mouse-sem-aceleracao"),
      StickyKeysSettings = Has("sem-teclas-aderentes") ? new DisabledStickyKeysSettings() : new DefaultStickyKeysSettings(),
    };

    return generator.GenerateXml(config);
  }

  private static List<Script> Scripts(DebloatOptions options, IReadOnlyList<AppEntry> apps, bool vlc)
  {
    var tweaks = options.Has("sem-restauracao")
      ? options.EffectiveTweaks.Where(t => t != "ponto-restauracao").ToHashSet()
      : options.EffectiveTweaks;
    string associations = Associations(vlc, options.Has("visualizador-fotos"));
    var named = associations.Length > 0 ? new HashSet<string> { "associacoes" } : new HashSet<string>();
    if (apps.Any(a => a.Id == "everything-toolbar")) named.Add("busca-escondida");

    var scripts = new List<Script>
    {
      new(ScriptRegions.Apply(Resources.Script("SystemTweaks.ps1"), tweaks, named).Replace("@@ASSOC@@", associations), ScriptPhase.System, ScriptType.Ps1),
      new(ScriptRegions.Apply(Resources.Script("DefaultUserTweaks.ps1"), tweaks, named), ScriptPhase.DefaultUser, ScriptType.Ps1),
    };
    if (options.Has("visualizador-fotos"))
    {
      scripts.Add(new(Resources.Script("PhotoViewer.reg"), ScriptPhase.FirstLogon, ScriptType.Reg));
    }
    string firstLogon = ScriptRegions.Apply(Resources.Script("FirstLogon.ps1"), tweaks, named)
      .Replace("@@DNS@@", DnsToken(options.Dns))
      .Replace("@@APPS@@", AppCatalog.ToScriptJson(apps));
    scripts.Add(new(firstLogon, ScriptPhase.FirstLogon, ScriptType.Ps1));
    return scripts;
  }

  private static readonly (string Tweak, string Icon)[] DesktopIconIds =
    [("icone-este-computador", "ThisPC"), ("icone-lixeira", "RecycleBin"), ("icone-pasta-usuario", "UserFiles"),
     ("icone-painel-controle", "ControlPanel"), ("icone-rede", "Network")];

  private static readonly string[] StartFolderIds =
    ["Settings", "FileExplorer", "Downloads", "Documents", "Pictures", "Music", "Videos", "Network", "PersonalFolder"];

  private static readonly Effect[] Animations =
    [Effect.ControlAnimations, Effect.AnimateMinMax, Effect.TaskbarAnimations, Effect.MenuAnimation, Effect.TooltipAnimation,
     Effect.SelectionFade, Effect.ComboBoxAnimation, Effect.ListBoxSmoothScrolling];

  private static readonly string[] VideoAudio =
    [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".3gp",
     ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".opus", ".wma"];

  private static readonly (string Ext, string ProgId)[] Images =
    [(".jpg", "PhotoViewer.FileAssoc.Jpeg"), (".jpeg", "PhotoViewer.FileAssoc.Jpeg"), (".png", "PhotoViewer.FileAssoc.Png"),
     (".bmp", "PhotoViewer.FileAssoc.Bitmap"), (".gif", "PhotoViewer.FileAssoc.Gif"), (".tif", "PhotoViewer.FileAssoc.Tiff"),
     (".tiff", "PhotoViewer.FileAssoc.Tiff")];

  /// <summary>XML da política de programas padrão (VLC e Visualizador de Fotos); vazio se não houver nada.</summary>
  public static string Associations(bool vlc, bool photoViewer)
  {
    var lines = new List<string>();
    if (vlc) lines.AddRange(VideoAudio.Select(e => $"""  <Association Identifier="{e}" ProgId="VLC{e}" ApplicationName="VLC media player" />"""));
    if (photoViewer) lines.AddRange(Images.Select(i => $"""  <Association Identifier="{i.Ext}" ProgId="{i.ProgId}" ApplicationName="Windows Photo Viewer" />"""));
    return lines.Count == 0 ? "" : "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<DefaultAssociations>\n" + string.Join("\n", lines) + "\n</DefaultAssociations>";
  }

  /// <summary>"pt-br" (catálogo da Microsoft) → "pt-BR" (gerador).</summary>
  public static string LanguageId(string code)
  {
    var parts = code.Split('-');
    return parts.Length switch
    {
      2 => $"{parts[0].ToLowerInvariant()}-{parts[1].ToUpperInvariant()}",
      3 => $"{parts[0].ToLowerInvariant()}-{char.ToUpperInvariant(parts[1][0])}{parts[1][1..].ToLowerInvariant()}-{parts[2].ToUpperInvariant()}",   // sr-latn-rs → sr-Latn-RS
      _ => code,
    };
  }

  private const string TaskbarExplorerOnly = """
    <LayoutModificationTemplate xmlns="http://schemas.microsoft.com/Start/2014/LayoutModification" xmlns:defaultlayout="http://schemas.microsoft.com/Start/2014/FullDefaultLayout" xmlns:start="http://schemas.microsoft.com/Start/2014/StartLayout" xmlns:taskbar="http://schemas.microsoft.com/Start/2014/TaskbarLayout" Version="1">
      <CustomTaskbarLayoutCollection PinListPlacement="Replace">
        <defaultlayout:TaskbarLayout>
          <taskbar:TaskbarPinList>
            <taskbar:DesktopApp DesktopApplicationID="Microsoft.Windows.Explorer" />
          </taskbar:TaskbarPinList>
        </defaultlayout:TaskbarLayout>
      </CustomTaskbarLayoutCollection>
    </LayoutModificationTemplate>
    """;

  /// <summary>Chaves genéricas de instalação (não ativam; servem para escolher a edição).</summary>
  private static readonly Dictionary<string, string> GenericKeys = new() { ["pro"] = "VK7JG-NPHTM-C97JM-9MPGT-3V66T" };

  public static string DnsToken(DnsChoice dns) => dns switch
  {
    DnsChoice.Provider => "provider",
    DnsChoice.Cloudflare => "cloudflare",
    DnsChoice.CloudflareFamily => "cloudflare_family",
    DnsChoice.AdGuard => "adguard",
    DnsChoice.Google => "google",
    DnsChoice.Quad9 => "quad9",
    _ => throw new ArgumentOutOfRangeException(nameof(dns)),
  };
}
