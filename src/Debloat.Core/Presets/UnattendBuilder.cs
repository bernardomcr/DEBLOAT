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
    bool everythingToolbar = apps.Any(a => a.Id == "everything-toolbar");

    var config = Configuration.Default with
    {
      LanguageSettings = new UnattendedLanguageSettings(
        ImageLanguage: generator.Lookup<ImageLanguage>(options.Language),
        LocaleAndKeyboard: new LocaleAndKeyboard(
          generator.Lookup<UserLocale>(options.Language),
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
              new GeneratedTargetDiskSettings(index: 0, assertNoPartitions: false), PartitionLayout.GPT, RecoveryMode.Partition),
            InstallFromSettings: new IndexInstallFromSettings(1),     // a mídia do DEBLOAT só tem a edição escolhida
            PagingFileSettings: new AutomaticPagingFileSettings(),
            DisableDefender: false, Disable8Dot3Names: false, PauseBeforeFormatting: false, PauseBeforeReboot: false,
            CompactOs: false, SkipIntegrityCheck: false)
        : new DefaultPESettings(
            EditionSettings: new UnattendedEditionSettings(generator.Lookup<WindowsEdition>(options.Edition)),
            BypassRequirementsCheck: true),
      ActivationKey = options.WipeDisk0 ? new ProductKey(GenericKeys[options.Edition]) : null,
      Bloatwares = options.Bloatware.Select(generator.Lookup<Bloatware>).ToImmutableList(),
      ExpressSettings = ExpressSettingsMode.DisableAll,
      ScriptSettings = new ScriptSettings(Scripts(options, apps), RestartExplorer: true),
      HidePowerShellWindows = true,

      // Sistema
      EnableLongPaths = true,
      AllowPowerShellScripts = true,
      DisableLastAccess = true,
      PreventAutomaticReboot = true,
      DisableSac = true,                 // Smart App Control bloqueia muito programa legítimo
      DisableSmartScreen = false,        // SmartScreen fica: protege e não custa nada
      DisableFastStartup = true,
      DisableAppSuggestions = true,
      DisableWidgets = true,
      PreventDeviceEncryption = true,
      DisableWpbt = true,                // bloatware do fabricante injetado pela BIOS
      PreventDeviceApps = true,          // "apps companheiros" de hardware
      DeleteWindowsOld = true,

      // Edge
      HideEdgeFre = true,
      DisableEdgeStartupBoost = true,
      MakeEdgeUninstallable = true,
      DeleteEdgeDesktopIcon = true,

      // Explorer, Iniciar e barra de tarefas
      ClassicContextMenu = options.ClassicContextMenu,
      LaunchToThisPC = true,
      ShowFileExtensions = true,
      HideFiles = HideModes.HiddenSystem,
      ShowEndTask = true,
      ShowAllTrayIcons = true,
      LeftTaskbar = options.LeftTaskbar,
      HideTaskViewButton = true,
      DisableBingResults = true,
      TaskbarSearch = everythingToolbar ? TaskbarSearchMode.Hide : TaskbarSearchMode.Box,
      StartPinsSettings = new EmptyStartPinsSettings(),
      DesktopIcons = new CustomDesktopIconSettings(new Dictionary<DesktopIcon, bool>
      {
        [generator.Lookup<DesktopIcon>("ThisPC")] = true,
        [generator.Lookup<DesktopIcon>("RecycleBin")] = true,
      }),
      ColorSettings = options.DarkMode
        ? new CustomColorSettings(ColorTheme.Dark, ColorTheme.Dark, EnableTransparency: true,
            AccentColorOnStart: false, AccentColorOnBorders: false, AccentColor: Color.FromArgb(0x00, 0x78, 0xD4))
        : new DefaultColorSettings(),

      // Entrada
      DisablePointerPrecision = true,
      StickyKeysSettings = new DisabledStickyKeysSettings(),
    };

    return generator.GenerateXml(config);
  }

  private static List<Script> Scripts(DebloatOptions options, IReadOnlyList<AppEntry> apps)
  {
    var scripts = new List<Script>
    {
      new(Resources.Script("SystemTweaks.ps1"), ScriptPhase.System, ScriptType.Ps1),
      new(Resources.Script("DefaultUserTweaks.ps1"), ScriptPhase.DefaultUser, ScriptType.Ps1),
    };
    if (options.ClassicPhotoViewer)
    {
      scripts.Add(new(Resources.Script("PhotoViewer.reg"), ScriptPhase.FirstLogon, ScriptType.Reg));
    }
    string firstLogon = Resources.Script("FirstLogon.ps1")
      .Replace("@@DNS@@", DnsToken(options.Dns))
      .Replace("@@APPS@@", AppCatalog.ToScriptJson(apps));
    scripts.Add(new(firstLogon, ScriptPhase.FirstLogon, ScriptType.Ps1));
    return scripts;
  }

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
