using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Debloat.Core.Catalog;
using Debloat.Core.Presets;
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
