using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Debloat.Core.Catalog;
using Debloat.Core.Media;
using Debloat.Core.Presets;
using Debloat.Core.Saves;
using Microsoft.Win32;

namespace Debloat.App;

/// <summary>Uma linha da janela de preparo: o Windows ou um instalador.</summary>
public partial class PrepItem(string id, string name) : ObservableObject
{
  public string Id { get; } = id;

  public string Name { get; } = name;

  [ObservableProperty]
  private string state = "Aguardando";

  [ObservableProperty]
  private bool done;
}

public partial class AppItem(AppEntry entry) : ObservableObject
{
  public AppEntry Entry { get; } = entry;

  [ObservableProperty]
  private bool isSelected = entry.Default;
}

public partial class CategoryItem(AppCategory category, IEnumerable<AppItem> apps) : ObservableObject
{
  public string Name { get; } = category.Name;

  public ObservableCollection<AppItem> Apps { get; } = new(apps);

  public string Summary => $"{Apps.Count(a => a.IsSelected)} de {Apps.Count}";

  public void Refresh() => OnPropertyChanged(nameof(Summary));

  [RelayCommand]
  private void ToggleAll()
  {
    bool select = Apps.Any(a => !a.IsSelected);
    foreach (var app in Apps) app.IsSelected = select;
  }
}

/// <summary>Uma linha da lista do Debloat: um ajuste ou um app a remover.</summary>
public partial class TweakRow(string id, string name, string detail, bool presetValue, bool aggressive) : ObservableObject
{
  public string Id { get; } = id;
  public string Name { get; } = name;
  public string Detail { get; } = detail;
  public bool HasDetail => Detail.Length > 0;
  public bool PresetValue { get; } = presetValue;
  public bool Aggressive { get; } = aggressive;

  [ObservableProperty]
  private bool isSelected = presetValue;
}

public partial class TweakGroup(string name, IEnumerable<TweakRow> rows) : ObservableObject
{
  public string Name { get; } = name;

  public ObservableCollection<TweakRow> Rows { get; } = new(rows);

  public string Summary => $"{Rows.Count(r => r.IsSelected)} de {Rows.Count}";

  public void Refresh() => OnPropertyChanged(nameof(Summary));
}

public record DnsOption(DnsChoice Value, string Label);

public partial class MigrationRow(MigrationItem item, bool selected) : ObservableObject
{
  public MigrationItem Item { get; } = item;

  public string Detail => Item.Kind == MigrationKind.Wifi ? Item.Note : $"{MainViewModel.Size(Item.Bytes)} · {Item.Note}";

  [ObservableProperty]
  private bool isSelected = selected;
}

public partial class SaveItem(SaveGame game) : ObservableObject
{
  public SaveGame Game { get; } = game;

  public string Detail => $"{Game.Source} · {Game.Files} arquivo(s) · {MainViewModel.Size(Game.Bytes)}";

  [ObservableProperty]
  private bool isSelected = true;
}

public partial class MainViewModel : ObservableObject
{
  public const string RemovedAppsGroup = "Apps removidos";

  private readonly AppCatalog catalog = AppCatalog.Load();
  private readonly HardwareProfile hardware = HardwareProfile.Detect();

  public MainViewModel()
  {
    Categories = new(catalog.Categories.Select(c => new CategoryItem(c,
      catalog.Apps.Where(a => a.Category == c.Id).Select(a => new AppItem(a)))));
    foreach (var category in Categories)
    {
      foreach (var app in category.Apps)
      {
        app.PropertyChanged += (_, _) => { category.Refresh(); OnPropertyChanged(nameof(AppsSummary)); };
      }
    }

    var groups = TweakCatalog.All.GroupBy(t => t.Group)
      .Select(g => new TweakGroup(g.Key, g.Select(t => new TweakRow(t.Id, t.Name, t.Detail, t.DefaultFor(hardware), t.Aggressive))))
      .Append(new TweakGroup(RemovedAppsGroup,
        TweakCatalog.Bloatware.Select(b => new TweakRow(b.Id, b.Name, "", b.Default(hardware), false))));
    TweakGroups = new(groups);
    foreach (var group in TweakGroups)
    {
      foreach (var row in group.Rows)
      {
        row.PropertyChanged += (_, _) => { group.Refresh(); OnPropertyChanged(nameof(TweaksSummary)); };
      }
    }
    selectedDns = DnsOptions[1];
  }

  // --- Debloat ---

  public ObservableCollection<TweakGroup> TweakGroups { get; }

  public string TweaksSummary
  {
    get
    {
      int changed = TweakGroups.SelectMany(g => g.Rows).Count(r => r.IsSelected != r.PresetValue);
      return changed == 0 ? "Preset Recomendado" : $"Personalizado ({changed} {(changed == 1 ? "mudança" : "mudanças")})";
    }
  }

  [RelayCommand]
  private void ResetTweaks()
  {
    foreach (var row in TweakGroups.SelectMany(g => g.Rows)) row.IsSelected = row.PresetValue;
  }

  public IReadOnlyList<string> PresetHighlights { get; } =
  [
    "Remove Copilot, Recall, Teams, Clipchamp, Notícias, Clima, OneDrive, Outlook novo e outros apps inúteis",
    "Mantém Bloco de Notas, Calculadora, Ferramenta de Captura, Xbox e Loja; Media Player vira o VLC",
    "Telemetria no mínimo, sem anúncios no Iniciar, no Explorer, na tela de bloqueio e nas Configurações",
    "Windows Update baixa sozinho, nunca reinicia com você usando o PC e adia versões grandes por 1 ano",
    "Sem Widgets, sem Bing na busca, sem Copilot, sem IA no Paint e no Bloco de Notas",
    "Xbox Game Bar desligada; Modo de Jogo e GPU por hardware ligados",
    "SmartScreen mantido; Smart App Control desligado; sem criptografia automática do disco",
    "Sem PowerShell 2.0, WordPad e modo IE; Reprodução Automática desligada; OneDrive bloqueado",
    "Explorador abre em Este Computador, sem Início e Galeria; Configurações, Explorador e Downloads no Iniciar",
    "No desktop: Num Lock ligado e o PC nunca suspende sozinho",
    "Ponto de restauração no final de tudo",
  ];

  // --- Apps ---

  public ObservableCollection<CategoryItem> Categories { get; }

  public string AppsSummary => $"{Categories.Sum(c => c.Apps.Count(a => a.IsSelected))} itens marcados";

  // --- Windows ---

  public IReadOnlyList<DnsOption> DnsOptions { get; } =
  [
    new(DnsChoice.Provider, "Do provedor"),
    new(DnsChoice.Cloudflare, "Cloudflare (1.1.1.1)"),
    new(DnsChoice.CloudflareFamily, "Cloudflare Família (bloqueia adulto e malware)"),
    new(DnsChoice.AdGuard, "AdGuard (bloqueia anúncios)"),
    new(DnsChoice.Google, "Google (8.8.8.8)"),
    new(DnsChoice.Quad9, "Quad9 (bloqueia sites maliciosos)"),
  ];

  [ObservableProperty] private DnsOption selectedDns;
  [ObservableProperty] private string userName = "Usuario";
  [ObservableProperty] private string status = "";

  // --- Windows: versão, build e idioma ---

  /// <summary>Uma build escolhível: a pronta da Microsoft (Official) ou uma do UUP dump (Uup), que precisa ser convertida.</summary>
  public record BuildOption(Version Build, WindowsRelease? Official, UupBuild? Uup)
  {
    public override string ToString() => Official is not null ? $"{Build} (pronta da Microsoft)" : $"{Build}";
  }

  public record VersionGroup(string Label, List<BuildOption> Builds)
  {
    public override string ToString() => Label == "Insider" ? "Windows 11 Insider" : $"Windows 11 {Label}";
  }

  public ObservableCollection<VersionGroup> Versions { get; } = [];
  public ObservableCollection<BuildOption> Builds { get; } = [];
  public ObservableCollection<WindowsLanguage> Languages { get; } =
    [new("pt-br", "Português (Brasil)"), new("en-us", "English (United States)")];

  [ObservableProperty] private VersionGroup? selectedVersion;
  [ObservableProperty] private BuildOption? selectedBuild;
  [ObservableProperty] private WindowsLanguage? selectedLanguage;

  public string WindowsVersion => SelectedBuild switch
  {
    { Official: { } r } when SelectedLanguage is { } l && r.FileFor(l.Code) is { } f => $"Build {SelectedBuild.Build} · {l.Name} · {f.Size / 1e9:F1} GB",
    { Uup: not null } when SelectedLanguage is { } l => $"Build {SelectedBuild.Build} · {l.Name} · montada a partir das atualizações da Microsoft (mais demorado)",
    _ => Versions.Count == 0 ? "Procurando versões..." : "",
  };

  /// <summary>Ao abrir: primeiro as prontas da Microsoft (rápido, já dá para baixar), depois todas as builds do UUP dump.</summary>
  public async Task LoadReleasesAsync()
  {
    try
    {
      foreach (var r in await WindowsCatalog.LoadReleasesAsync(Http)) AddBuild(r.Version, new BuildOption(r.Build, r, null));
      SortVersions();
      SelectedVersion = Versions.FirstOrDefault(v => v.Builds.Any(b => b.Official is not null)) ?? Versions.FirstOrDefault();
      SelectedLanguage = Languages[0];
    }
    catch (Exception e)
    {
      Status = $"Não deu para buscar as versões do Windows: {e.Message}";
    }
    await MergeUupAsync();
  }

  [RelayCommand]
  private async Task RefreshBuilds() => await RunBusy(MergeUupAsync);

  private async Task MergeUupAsync()
  {
    try
    {
      var keep = SelectedBuild;
      foreach (var b in await UupDump.ListBuildsAsync(Http))
      {
        var group = Versions.FirstOrDefault(v => v.Label == b.Version);
        if (group?.Builds.Any(x => x.Build == b.Build) == true) continue;   // a pronta da Microsoft já está
        AddBuild(b.Version, new BuildOption(b.Build, null, b));
      }
      SortVersions();
      SelectedVersion = Versions.FirstOrDefault(v => keep is not null && v.Builds.Contains(keep)) ?? Versions.FirstOrDefault();
      if (keep is not null && Builds.Contains(keep)) SelectedBuild = keep;
    }
    catch (Exception e)
    {
      Status = $"Não deu para buscar as outras builds (UUP dump): {e.Message}";
    }
  }

  /// <summary>Mais nova primeiro pelo nome da versão (26H2 > 26H1 > 25H2), não pela build: a 26H1 tem build maior que a 26H2.</summary>
  private void SortVersions()
  {
    var ordered = Versions.OrderByDescending(v => VersionRank(v.Label)).ToList();
    Versions.Clear();
    foreach (var v in ordered)
    {
      v.Builds.Sort((a, b) => b.Build.CompareTo(a.Build));
      Versions.Add(v);
    }
  }

  public static int VersionRank(string label) =>
    label.Length == 4 && int.TryParse(label[..2], out int year) && char.ToUpperInvariant(label[2]) == 'H' && char.IsDigit(label[3])
      ? year * 10 + (label[3] - '0')
      : -1;   // Insider e o que não for "AAHn" vão para o fim

  private void AddBuild(string version, BuildOption option)
  {
    var group = Versions.FirstOrDefault(v => v.Label == version);
    if (group is null) Versions.Add(group = new VersionGroup(version, []));
    group.Builds.Add(option);
  }

  partial void OnSelectedVersionChanged(VersionGroup? value)
  {
    Builds.Clear();
    foreach (var b in value?.Builds ?? []) Builds.Add(b);
    SelectedBuild = Builds.FirstOrDefault(b => b.Official is not null) ?? Builds.FirstOrDefault();
  }

  partial void OnSelectedBuildChanged(BuildOption? value) => OnPropertyChanged(nameof(WindowsVersion));

  partial void OnSelectedLanguageChanged(WindowsLanguage? value) => OnPropertyChanged(nameof(WindowsVersion));

  public DebloatOptions BuildOptions()
  {
    var rows = TweakGroups.SelectMany(g => g.Rows.Select(r => (Group: g.Name, Row: r))).ToList();
    return new()
    {
      UserName = UserName.Trim(),
      Hardware = hardware,
      Language = SelectedLanguage?.Code ?? "pt-br",
      Tweaks = rows.Where(x => x.Group != RemovedAppsGroup && x.Row.IsSelected).Select(x => x.Row.Id).ToHashSet(),
      RemovedApps = rows.Where(x => x.Group == RemovedAppsGroup && x.Row.IsSelected).Select(x => x.Row.Id).ToHashSet(),
      Dns = SelectedDns.Value,
      SelectedApps = Categories.SelectMany(c => c.Apps).Where(a => a.IsSelected).Select(a => a.Entry.Id).ToList(),
    };
  }

  // --- Download e mídia ---

  private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

  private static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DEBLOAT");

  public static string MediaDir => Path.Combine(DataDir, "midia");

  private static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

  [ObservableProperty] private bool isBusy;
  [ObservableProperty] private double progressValue;

  private string? esdPath;

  [RelayCommand]
  private async Task DownloadWindows() => await RunBusy(EnsureWindowsAsync);

  private string? preparedFolder;     // build do UUP dump já convertida

  /// <summary>Garante o Windows escolhido em cache: o .esd da Microsoft, ou a pasta convertida pelo UUP dump.</summary>
  private async Task EnsureWindowsAsync()
  {
    var build = SelectedBuild ?? throw new InvalidOperationException("Escolha a versão e a build do Windows.");
    string language = SelectedLanguage?.Code ?? "pt-br";
    string cache = Directory.CreateDirectory(Path.Combine(DataDir, "cache")).FullName;

    if (build.Uup is { } uup)
    {
      if (!IsAdmin) throw new InvalidOperationException("Para montar uma build do UUP dump, abra o DEBLOAT como administrador.");
      string work = Path.Combine(cache, $"uup-{uup.Build}-{language}");
      preparedFolder = await UupDump.BuildFolderAsync(Http, uup, language, work,
        new Progress<MediaStep>(s => { ProgressValue = s.Fraction * 100; Status = s.Text + "..."; }));
      esdPath = null;
      Status = "Windows montado.";
      return;
    }

    var esd = build.Official!.FileFor(language) ?? throw new InvalidOperationException("Essa build não tem esse idioma.");
    string path = Path.Combine(cache, esd.FileName);
    preparedFolder = null;
    if (esdPath == path) return;
    var progress = new Progress<DownloadProgress>(p =>
    {
      ProgressValue = p.Fraction * 100;
      Status = p.Done >= p.Total
        ? "Conferindo a integridade..."
        : $"Baixando: {p.Done / 1e9:F2} de {p.Total / 1e9:F2} GB · {p.BytesPerSecond / 1e6:F0} MB/s";
    });
    await new SegmentedDownloader(Http).DownloadAsync(esd.Url, path, esd.Size, esd.Sha256, esd.Sha1, progress);
    foreach (var old in Directory.EnumerateFiles(cache, "*.esd").Where(f => f != path)) File.Delete(old);
    esdPath = path;
    Status = "Windows baixado.";
  }

  public ObservableCollection<PrepItem> PrepItems { get; } = [];

  /// <summary>A janela abre a lista do que está sendo baixado.</summary>
  public event Action? PreparationStarted;

  /// <summary>
  /// Monta a pasta de instalação com o preset (de .esd ou da pasta convertida). Enquanto o Windows baixa,
  /// os instaladores dos apps também baixam e vão para DEBLOAT\apps na mídia.
  /// </summary>
  private async Task PrepareMediaAsync(double share)
  {
    var options = BuildOptions();
    var apps = catalog.Resolve(options.SelectedApps ?? catalog.Defaults.Select(a => a.Id));
    PrepItems.Clear();
    var windows = new PrepItem("windows", SelectedBuild is { } b ? $"Windows 11 {SelectedVersion?.Label} ({b.Build})" : "Windows 11");
    PrepItems.Add(windows);
    foreach (var app in apps) PrepItems.Add(new PrepItem(app.Id, app.Name));
    PreparationStarted?.Invoke();

    string staging = Path.Combine(DataDir, "cache", "midia-apps");
    if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
    var installers = OfflineInstallers.PrepareAsync(Http, apps, Path.Combine(DataDir, "cache", "apps"), staging,
      new Progress<OfflineProgress>(p =>
      {
        if (PrepItems.FirstOrDefault(i => i.Id == p.Id) is not { } item) return;
        item.State = p.State switch
        {
          OfflineState.Downloading => "Baixando...",
          OfflineState.Ready => Size(p.Bytes),
          _ => "Na instalação, pela internet",
        };
        item.Done = p.State is OfflineState.Ready or OfflineState.Online;
      }));

    windows.State = "Baixando...";
    var windowsTask = EnsureWindowsAsync();
    await Task.WhenAll(windowsTask, installers);
    windows.State = "Pronto";
    windows.Done = true;

    byte[] xml = new UnattendBuilder(catalog).BuildBytes(options);
    var features = apps.Where(a => a.Source == "feature").Select(a => a.Package).ToList();   // .NET 3.5 já vem na imagem
    var progress = new Progress<MediaStep>(step => { ProgressValue = step.Fraction * share; Status = step.Text + "..."; });
    if (preparedFolder is not null)
    {
      await MediaBuilder.BuildFromFolderAsync(preparedFolder, MediaDir, "Professional", xml, progress, features: features);
    }
    else
    {
      await MediaBuilder.BuildAsync(esdPath!, MediaDir, "Professional", xml, progress, features: features);
    }

    Status = "Pondo os instaladores na mídia...";
    await Task.Run(() =>
    {
      foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
      {
        string dest = Path.Combine(MediaDir, Path.GetRelativePath(staging, file));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(file, dest, overwrite: true);
      }
    });
  }

  [RelayCommand]
  private async Task BuildMedia()
  {
    if (!IsAdmin)
    {
      Status = "Abra o DEBLOAT como administrador.";
      return;
    }
    await RunBusy(async () =>
    {
      await PrepareMediaAsync(100);
      Status = $"Instalação montada em {MediaDir}.";
      Process.Start(new ProcessStartInfo("explorer.exe", $"\"{MediaDir}\"") { UseShellExecute = true });
    });
  }

  [RelayCommand]
  private async Task BuildIso()
  {
    if (!IsAdmin)
    {
      Status = "Abra o DEBLOAT como administrador.";
      return;
    }
    var dialog = new SaveFileDialog { FileName = "DEBLOAT-Windows11.iso", Filter = "Imagem ISO (*.iso)|*.iso", Title = "Salvar ISO" };
    if (dialog.ShowDialog() != true) return;
    await RunBusy(async () =>
    {
      await PrepareMediaAsync(90);
      Status = "Gerando a ISO...";
      await Task.Run(() => IsoWriter.Write(MediaDir, dialog.FileName));
      Status = $"ISO pronta: {dialog.FileName}";
    });
  }

  // --- Saves ---

  public ObservableCollection<SaveItem> Saves { get; } = [];

  public string SavesSummary => Saves.Count == 0
    ? ""
    : $"{Saves.Count(s => s.IsSelected)} de {Saves.Count} jogos · {Size(Saves.Where(s => s.IsSelected).Sum(s => s.Game.Bytes))}";

  private SaveScanner Scanner => new(Http, Path.Combine(DataDir, "tools"));

  [RelayCommand]
  private async Task ScanSaves() => await RunBusy(async () =>
  {
    var games = await Scanner.ScanAsync(new Progress<string>(text => Status = text));
    Saves.Clear();
    foreach (var game in games)
    {
      var item = new SaveItem(game);
      item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SavesSummary));
      Saves.Add(item);
    }
    OnPropertyChanged(nameof(SavesSummary));
    Status = $"{games.Count} jogos com saves encontrados.";
  });

  [RelayCommand]
  private void ToggleAllSaves()
  {
    bool select = Saves.Any(s => !s.IsSelected);
    foreach (var save in Saves) save.IsSelected = select;
  }

  // --- Migração (programas e pastas) ---

  public ObservableCollection<MigrationRow> Migration { get; } = [];

  public static string Size(long bytes) => bytes switch
  {
    < 1_000_000 => $"{bytes / 1e3:F0} KB",
    < 1_000_000_000 => $"{bytes / 1e6:F0} MB",
    _ => $"{bytes / 1e9:F1} GB",
  };

  public async Task LoadMigrationAsync()
  {
    var items = await Task.Run(Debloat.Core.Saves.Migration.Detect);
    Migration.Clear();
    foreach (var item in items) Migration.Add(new MigrationRow(item, item.DefaultSelected));
  }

  [RelayCommand]
  private async Task AddFolder()
  {
    var dialog = new OpenFolderDialog { Title = "Pasta para levar para o Windows novo", Multiselect = true };
    if (dialog.ShowDialog() != true) return;
    foreach (string folder in dialog.FolderNames)
    {
      if (Migration.Any(m => string.Equals(m.Item.Path, folder, StringComparison.OrdinalIgnoreCase))) continue;
      long bytes = await Task.Run(() => Debloat.Core.Saves.Migration.Size(folder, []));
      string id = "pasta-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(folder.ToLowerInvariant())))[..8];
      var item = new MigrationItem(id, MigrationKind.Folder, Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } n ? n : folder, folder, bytes, folder, true);
      Migration.Add(new MigrationRow(item, true));
    }
  }

  /// <summary>Quanto vai para a partição de dados (saves + migração).</summary>
  private long BackupBytes =>
    Saves.Where(s => s.IsSelected).Sum(s => s.Game.Bytes) + Migration.Where(m => m.IsSelected).Sum(m => m.Item.Bytes);

  // --- Pendrive ---

  public ObservableCollection<UsbDrive> UsbDrives { get; } = [];

  [ObservableProperty] private UsbDrive? selectedUsb;

  [RelayCommand]
  private async Task RefreshUsb()
  {
    try
    {
      var drives = await UsbWriter.ListAsync();
      UsbDrives.Clear();
      foreach (var d in drives) UsbDrives.Add(d);
      SelectedUsb = UsbDrives.FirstOrDefault();
      if (UsbDrives.Count == 0) Status = "Nenhum pendrive USB encontrado.";
    }
    catch (Exception e)
    {
      Status = $"Não deu para listar os pendrives: {e.Message}";
    }
  }

  /// <summary>Faz tudo: baixa, monta, leva os drivers e grava. A janela pergunta antes de chamar.</summary>
  public async Task WriteUsbAsync(UsbDrive drive)
  {
    if (!IsAdmin)
    {
      Status = "Abra o DEBLOAT como administrador.";
      return;
    }
    var migration = Migration.Where(m => m.IsSelected).Select(m => m.Item).ToList();
    var open = Debloat.Core.Saves.Migration.OpenPrograms(migration);
    if (open.Count > 0)
    {
      Status = $"Feche antes de gravar: {string.Join(", ", open)}.";
      return;
    }
    long dataSpace = drive.Size - UsbWriter.BootPartitionSize(drive.Size);
    if (BackupBytes > 0 && BackupBytes > dataSpace - (512L << 20))
    {
      Status = $"O backup marcado ({Size(BackupBytes)}) não cabe no pendrive ({Size(dataSpace)} livres para backup).";
      return;
    }
    await RunBusy(async () =>
    {
      await PrepareMediaAsync(60);
      string installers = Path.Combine(MediaDir, OfflineInstallers.MediaFolder);
      long mediaSize = Directory.EnumerateFiles(MediaDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
      if (mediaSize > UsbWriter.BootPartitionSize(drive.Size) - (512L << 20) && Directory.Exists(installers))
      {
        Directory.Delete(installers, recursive: true);   // pendrive pequeno: os apps baixam no primeiro login, como antes
        foreach (var item in PrepItems.Where(i => i.Id != "windows")) item.State = "Na instalação, pela internet";
      }
      Status = "Copiando drivers de rede, disco e chipset...";
      var drivers = await DriverExporter.ExportAsync(MediaDir);
      var (_, data) = await UsbWriter.WriteAsync(drive, MediaDir,
        new Progress<WriteStep>(step => { ProgressValue = 60 + step.Fraction * 38; Status = step.Text + "..."; }));
      var saves = Saves.Where(s => s.IsSelected).Select(s => s.Game).ToList();
      if (saves.Count > 0 && data is char d)
      {
        Status = $"Salvando {saves.Count} saves...";
        await Scanner.BackupAsync(saves, $"{d}:\\");
      }
      if (migration.Count > 0 && data is char m)
      {
        await Debloat.Core.Saves.Migration.BackupAsync(migration, $"{m}:\\", new Progress<string>(text => Status = text));
      }
      Status = $"Pendrive pronto ({drivers.Count} drivers, {saves.Count} saves).";
    });
  }

  private async Task RunBusy(Func<Task> work)
  {
    if (IsBusy) return;
    IsBusy = true;
    ProgressValue = 0;
    try
    {
      await work();
    }
    catch (Exception e)
    {
      Status = $"Não deu certo: {e.Message}";
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private void ExportXml()
  {
    var dialog = new SaveFileDialog
    {
      FileName = "autounattend.xml",
      Filter = "Arquivo de resposta (*.xml)|*.xml",
      Title = "Salvar autounattend.xml",
    };
    if (dialog.ShowDialog() != true) return;
    try
    {
      File.WriteAllBytes(dialog.FileName, new UnattendBuilder(catalog).BuildBytes(BuildOptions()));
      Status = $"Salvo em {dialog.FileName}.";
    }
    catch (Exception e)
    {
      Status = $"Não deu para gerar: {e.Message}";
    }
  }
}
