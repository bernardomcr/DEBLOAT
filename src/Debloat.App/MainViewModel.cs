using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Principal;
using Debloat.Core.Catalog;
using Debloat.Core.Media;
using Debloat.Core.Presets;
using Debloat.Core.Saves;
using Microsoft.Win32;

namespace Debloat.App;

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

public record DnsOption(DnsChoice Value, string Label);

public partial class MigrationRow(MigrationItem item) : ObservableObject
{
  public MigrationItem Item { get; } = item;

  public string Detail => Item.Kind == MigrationKind.Wifi ? Item.Note : $"{MainViewModel.Size(Item.Bytes)} · {Item.Note}";

  [ObservableProperty]
  private bool isSelected = item.DefaultSelected;
}

public partial class SaveItem(SaveGame game) : ObservableObject
{
  public SaveGame Game { get; } = game;

  public string Detail => $"{Game.Source} · {Game.Files} arquivo(s) · {(Game.Bytes < 1_000_000 ? $"{Game.Bytes / 1e3:F0} KB" : $"{Game.Bytes / 1e6:F1} MB")}";

  [ObservableProperty]
  private bool isSelected = true;
}

public partial class MainViewModel : ObservableObject
{
  private readonly AppCatalog catalog = AppCatalog.Load();

  public MainViewModel()
  {
    Hardware = HardwareProfile.Detect();
    Categories = new(catalog.Categories.Select(c => new CategoryItem(c,
      catalog.Apps.Where(a => a.Category == c.Id).Select(a => new AppItem(a)))));
    foreach (var category in Categories)
    {
      foreach (var app in category.Apps)
      {
        app.PropertyChanged += (_, _) => { category.Refresh(); OnPropertyChanged(nameof(AppsSummary)); };
      }
    }
    selectedDns = DnsOptions[1];
  }

  public HardwareProfile Hardware { get; }

  public string MachineKind => Hardware.HasBattery ? "Notebook (tem bateria)" : "Desktop (sem bateria)";
  public string HelloText => Hardware.HasIrCamera ? "Câmera IR encontrada: Windows Hello por rosto será mantido" : "Sem câmera IR: Windows Hello por rosto será removido";
  public string PenText => Hardware.HasPenOrTouch ? "Tela touch/caneta: manuscrito será mantido" : "Sem touch/caneta: manuscrito e painel de matemática serão removidos";
  public string PowerText => Hardware.HasBattery ? "Plano Equilibrado, hibernação mantida, Localizar Dispositivo mantido" : "Plano Equilibrado + modo Melhor desempenho, hibernação desligada";

  public ObservableCollection<CategoryItem> Categories { get; }

  public string AppsSummary => $"{Categories.Sum(c => c.Apps.Count(a => a.IsSelected))} itens marcados";

  public IReadOnlyList<DnsOption> DnsOptions { get; } =
  [
    new(DnsChoice.Provider, "Do provedor (não mexer)"),
    new(DnsChoice.Cloudflare, "Cloudflare (1.1.1.1) — rápido"),
    new(DnsChoice.CloudflareFamily, "Cloudflare Família — bloqueia adulto e malware"),
    new(DnsChoice.AdGuard, "AdGuard — bloqueia anúncios"),
    new(DnsChoice.Google, "Google (8.8.8.8)"),
    new(DnsChoice.Quad9, "Quad9 — bloqueia sites maliciosos"),
  ];

  [ObservableProperty] private DnsOption selectedDns;
  [ObservableProperty] private string userName = "Usuario";
  [ObservableProperty] private bool darkMode = true;
  [ObservableProperty] private bool leftTaskbar = true;
  [ObservableProperty] private bool classicContextMenu = true;
  [ObservableProperty] private bool classicPhotoViewer = true;
  [ObservableProperty] private string status = "";

  public IReadOnlyList<string> PresetHighlights { get; } =
  [
    "Remove Copilot, Recall, Teams, Clipchamp, Notícias, Clima, OneDrive, Outlook novo e outros 25 apps inúteis",
    "Mantém Bloco de Notas, Media Player, Calculadora, Ferramenta de Captura, Xbox e Loja",
    "Telemetria no mínimo, sem anúncios no Iniciar, no Explorer, na tela de bloqueio e nas Configurações",
    "Windows Update baixa sozinho, nunca reinicia com você logado e adia versões grandes por 1 ano",
    "Sem Widgets, sem Bing na busca, sem IA no Paint e no Bloco de Notas, sem Click to Do",
    "Gravação contínua do Xbox desligada; Game Bar, Modo de Jogo e GPU por hardware ligados",
    "SmartScreen mantido; Smart App Control desligado; sem criptografia automática do disco",
    "Bloqueia bloatware do fabricante injetado pela BIOS (WPBT) e apps companheiros de hardware",
    "Ponto de restauração \"Instalação limpa DEBLOAT\" no final de tudo",
  ];

  public DebloatOptions BuildOptions() => new()
  {
    UserName = UserName.Trim(),
    Hardware = Hardware,
    DarkMode = DarkMode,
    LeftTaskbar = LeftTaskbar,
    ClassicContextMenu = ClassicContextMenu,
    ClassicPhotoViewer = ClassicPhotoViewer,
    Dns = SelectedDns.Value,
    SelectedApps = Categories.SelectMany(c => c.Apps).Where(a => a.IsSelected).Select(a => a.Entry.Id).ToList(),
  };

  // --- Windows: download e mídia ---

  private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

  private static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DEBLOAT");

  public static string MediaDir => Path.Combine(DataDir, "midia");

  private static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

  [ObservableProperty] private bool isBusy;
  [ObservableProperty] private double progressValue;
  [ObservableProperty] private string windowsVersion = "Ainda não baixado.";

  private EsdFile? esd;
  private string? esdPath;

  [RelayCommand]
  private async Task DownloadWindows() => await RunBusy(EnsureWindowsAsync);

  private async Task EnsureWindowsAsync()
  {
    Status = "Procurando a versão mais recente nos catálogos da Microsoft...";
    esd ??= await WindowsCatalog.FindLatestAsync(Http);
    WindowsVersion = $"Windows 11 build {esd.Build} · {esd.Language} · {esd.Size / 1e9:F1} GB (servidores da Microsoft)";
    string cache = Directory.CreateDirectory(Path.Combine(DataDir, "cache")).FullName;
    string path = Path.Combine(cache, esd.FileName);
    var progress = new Progress<DownloadProgress>(p =>
    {
      ProgressValue = p.Fraction * 100;
      Status = p.Done >= p.Total
        ? "Conferindo a integridade (SHA-256)..."
        : $"Baixando: {p.Done / 1e9:F2} de {p.Total / 1e9:F2} GB · {p.BytesPerSecond / 1e6:F0} MB/s";
    });
    await new SegmentedDownloader(Http).DownloadAsync(esd.Url, path, esd.Size, esd.Sha256, esd.Sha1, progress);
    foreach (var old in Directory.EnumerateFiles(cache, "*.esd").Where(f => f != path)) File.Delete(old);
    esdPath = path;
    Status = "Windows baixado e conferido.";
  }

  [RelayCommand]
  private async Task BuildMedia()
  {
    if (!IsAdmin)
    {
      Status = "Para montar a instalação, abra o DEBLOAT como administrador.";
      return;
    }
    await RunBusy(async () =>
    {
      if (esdPath is null) await EnsureWindowsAsync();
      byte[] xml = new UnattendBuilder(catalog).BuildBytes(BuildOptions());
      var progress = new Progress<MediaStep>(step => { ProgressValue = step.Fraction * 100; Status = step.Text + "..."; });
      await MediaBuilder.BuildAsync(esdPath!, MediaDir, "Professional", xml, progress);
      Status = $"Instalação montada em {MediaDir}.";
      Process.Start(new ProcessStartInfo("explorer.exe", $"\"{MediaDir}\"") { UseShellExecute = true });
    });
  }

  [RelayCommand]
  private async Task BuildIso()
  {
    if (!IsAdmin)
    {
      Status = "Para montar a instalação, abra o DEBLOAT como administrador.";
      return;
    }
    var dialog = new SaveFileDialog { FileName = "DEBLOAT-Windows11.iso", Filter = "Imagem ISO (*.iso)|*.iso", Title = "Salvar ISO" };
    if (dialog.ShowDialog() != true) return;
    await RunBusy(async () =>
    {
      if (esdPath is null) await EnsureWindowsAsync();
      byte[] xml = new UnattendBuilder(catalog).BuildBytes(BuildOptions());
      await MediaBuilder.BuildAsync(esdPath!, MediaDir, "Professional", xml,
        new Progress<MediaStep>(step => { ProgressValue = step.Fraction * 90; Status = step.Text + "..."; }));
      Status = "Gerando a ISO...";
      await Task.Run(() => IsoWriter.Write(MediaDir, dialog.FileName));
      Status = $"ISO pronta: {dialog.FileName} (boota em UEFI e BIOS; serve para Ventoy e máquina virtual).";
    });
  }

  // --- Saves ---

  public ObservableCollection<SaveItem> Saves { get; } = [];

  public string SavesSummary => Saves.Count == 0
    ? "Clique em procurar. Nada é copiado até você gravar o pendrive."
    : $"{Saves.Count(s => s.IsSelected)} de {Saves.Count} jogos marcados · {Saves.Where(s => s.IsSelected).Sum(s => s.Game.Bytes) / 1e6:F0} MB";

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

  // --- Migração (Wi-Fi, ShareX, navegadores, pastas) ---

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
    foreach (var item in items) Migration.Add(new MigrationRow(item));
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
      if (UsbDrives.Count == 0) Status = "Nenhum pendrive USB encontrado. Conecte um e clique em atualizar.";
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
      Status = "Para gravar o pendrive, abra o DEBLOAT como administrador.";
      return;
    }
    var migration = Migration.Where(m => m.IsSelected).Select(m => m.Item).ToList();
    var open = Debloat.Core.Saves.Migration.OpenPrograms(migration);
    if (open.Count > 0)
    {
      Status = $"Feche antes de gravar (eles travam os arquivos): {string.Join(", ", open)}.";
      return;
    }
    long dataSpace = drive.Size - UsbWriter.BootPartitionSize(drive.Size);
    if (BackupBytes > 0 && BackupBytes > dataSpace - (512L << 20))
    {
      Status = $"O backup marcado ({Size(BackupBytes)}) não cabe na parte de dados deste pendrive ({Size(dataSpace)}). Desmarque algumas pastas.";
      return;
    }
    await RunBusy(async () =>
    {
      if (esdPath is null) await EnsureWindowsAsync();
      byte[] xml = new UnattendBuilder(catalog).BuildBytes(BuildOptions());
      await MediaBuilder.BuildAsync(esdPath!, MediaDir, "Professional", xml,
        new Progress<MediaStep>(step => { ProgressValue = step.Fraction * 60; Status = step.Text + "..."; }));
      Status = "Levando os drivers de rede, disco e chipset deste PC...";
      var drivers = await DriverExporter.ExportAsync(MediaDir);
      var (_, data) = await UsbWriter.WriteAsync(drive, MediaDir,
        new Progress<WriteStep>(step => { ProgressValue = 60 + step.Fraction * 38; Status = step.Text + "..."; }));
      var saves = Saves.Where(s => s.IsSelected).Select(s => s.Game).ToList();
      if (saves.Count > 0 && data is char d)
      {
        Status = $"Salvando {saves.Count} saves de jogos no pendrive...";
        await Scanner.BackupAsync(saves, $"{d}:\\");
      }
      if (migration.Count > 0 && data is char m)
      {
        await Debloat.Core.Saves.Migration.BackupAsync(migration, $"{m}:\\", new Progress<string>(text => Status = text));
      }
      Status = $"Pendrive pronto ({drivers.Count} drivers de hardware, {saves.Count} saves). Dê boot por ele no PC que vai ser formatado.";
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
      Status = $"Salvo em {dialog.FileName}. Copie para a raiz do pendrive de instalação.";
    }
    catch (Exception e)
    {
      Status = $"Não deu para gerar: {e.Message}";
    }
  }
}
