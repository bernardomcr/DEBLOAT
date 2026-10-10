using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Debloat.App.Panel;

/// <summary>Um app da lista do painel (primeiro login do Windows instalado pelo DEBLOAT).</summary>
public partial class PanelApp(string id, string name) : ObservableObject
{
  public string Id { get; } = id;

  public string Name { get; } = name;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(StateText), nameof(IsReady))]
  private string state = "aguardando";

  /// <summary>AppID do Iniciar (Get-StartApps): abre pelo shell:AppsFolder, como o usuário normal (não como administrador).</summary>
  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CanOpen))]
  private string? startId;

  [ObservableProperty]
  private bool canPin;

  public bool IsReady => State == "pronto";

  public bool CanOpen => StartId is not null;

  public string StateText => State switch
  {
    "instalando" => "Instalando",
    "pronto" => "Pronto",
    "erro" => "Não instalou",
    _ => "Aguardando",
  };

  [RelayCommand]
  private void Open()
  {
    // Pelo Explorer: o painel roda como administrador e o app não deve herdar isso.
    if (StartId is not null) Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{StartId}") { UseShellExecute = true });
  }

  [RelayCommand]
  private void Pin()
  {
    if (StartMenu.Pin(StartId)) CanPin = false;
  }
}

/// <summary>Lê o C:\Debloat\estado.json que o FirstLogon.ps1 grava e mantém a lista atualizada.</summary>
public partial class PanelViewModel : ObservableObject
{
  private readonly string statePath;
  private readonly int parentId;
  private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
  private DateTime lastStartScan = DateTime.MinValue;
  private bool scanning;

  public ObservableCollection<PanelApp> Apps { get; } = [];

  [ObservableProperty] private string title = "Preparando o Windows";
  [ObservableProperty] private string detail = "";
  [ObservableProperty] private string runtimes = "";
  [ObservableProperty] private bool finished;
  [ObservableProperty] private bool scriptGone;

  public PanelViewModel(string statePath, int parentId)
  {
    this.statePath = statePath;
    this.parentId = parentId;
    timer.Tick += (_, _) => Refresh();
  }

  public void Start()
  {
    Refresh();
    timer.Start();
  }

  private void Refresh()
  {
    try
    {
      using var json = JsonDocument.Parse(File.ReadAllText(statePath, Encoding.UTF8));
      var root = json.RootElement;
      Title = root.GetProperty("titulo").GetString() ?? "";
      Detail = root.GetProperty("detalhe").GetString() ?? "";
      Finished = root.GetProperty("terminou").GetBoolean();
      int runtimesTotal = 0, runtimesDone = 0;
      foreach (var app in root.GetProperty("apps").EnumerateArray())
      {
        string id = app.GetProperty("id").GetString()!;
        string state = app.GetProperty("estado").GetString() ?? "aguardando";
        // Pré-requisitos (.NET, VC++, DirectX...) não têm o que abrir: viram uma linha só.
        if (app.GetProperty("categoria").GetString() == "runtimes")
        {
          runtimesTotal++;
          if (state is "pronto" or "erro") runtimesDone++;
          continue;
        }
        var row = Apps.FirstOrDefault(a => a.Id == id);
        if (row is null) Apps.Add(row = new PanelApp(id, app.GetProperty("nome").GetString() ?? id));
        row.State = state;
      }
      Runtimes = runtimesTotal == 0 ? "" : $"{runtimesDone} de {runtimesTotal}";
      if (Finished)
      {
        // No fim o título do script é interno ("FIM"/"Pronto"): mostra a contagem.
        int total = root.GetProperty("apps").GetArrayLength();
        int ready = root.GetProperty("apps").EnumerateArray().Count(a => a.GetProperty("estado").GetString() == "pronto");
        Title = $"{ready} de {total} apps instalados";
      }
    }
    catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
    {
      // Arquivo ainda não existe ou está sendo trocado: tenta no próximo segundo.
    }
    if (!ScriptGone && parentId > 0)
    {
      try { using var parent = Process.GetProcessById(parentId); } catch (ArgumentException) { ScriptGone = true; }
    }
    if (Apps.Any(a => a.IsReady && a.StartId is null) && !scanning && DateTime.Now - lastStartScan > TimeSpan.FromSeconds(8))
    {
      _ = ScanStartMenuAsync();
    }
  }

  /// <summary>Acha cada app pronto no Iniciar (para Abrir/Fixar).</summary>
  private async Task ScanStartMenuAsync()
  {
    scanning = true;
    lastStartScan = DateTime.Now;
    try
    {
      var entries = await StartMenu.ListAsync();
      foreach (var app in Apps.Where(a => a.IsReady && a.StartId is null))
      {
        if (StartMenu.Match(app.Name, entries) is { } entry)
        {
          app.StartId = entry.Id;
          app.CanPin = StartMenu.CanPin(entry.Id);
        }
      }
    }
    finally
    {
      scanning = false;
    }
  }
}
