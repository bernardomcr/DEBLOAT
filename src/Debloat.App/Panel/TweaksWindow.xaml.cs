using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace Debloat.App.Panel;

/// <summary>Um ajuste aplicado na instalação, com o "Desfazer" gerado dos scripts (TweakUndo).</summary>
public partial class TweakUndoRow(TweaksViewModel owner, string id, string name, string group, IReadOnlyList<string> commands) : ObservableObject
{
  public string Id { get; } = id;

  public string Name { get; } = name;

  public string Group { get; } = group;

  public IReadOnlyList<string> Commands { get; } = commands;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CanUndo))]
  private bool undone;

  public bool CanUndo => !Undone;

  [RelayCommand]
  private void Undo() => owner.Undo(this);
}

/// <summary>Lê o C:\Debloat\ajustes.json (escrito no primeiro login) e desfaz um ajuste apagando os valores dele.</summary>
public partial class TweaksViewModel : ObservableObject
{
  private readonly string undoneFile;

  public ObservableCollection<TweakUndoRow> Tweaks { get; } = [];

  [ObservableProperty] private string status = "";

  public TweaksViewModel(string jsonPath)
  {
    undoneFile = Path.Combine(Path.GetDirectoryName(jsonPath)!, "ajustes-desfeitos.txt");
    var undone = File.Exists(undoneFile) ? File.ReadAllLines(undoneFile).ToHashSet() : [];
    using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
    foreach (var item in json.RootElement.EnumerateArray())
    {
      var row = new TweakUndoRow(this, item.GetProperty("id").GetString()!, item.GetProperty("name").GetString()!, item.GetProperty("group").GetString()!,
        item.GetProperty("commands").EnumerateArray().Select(c => c.GetString()!).ToList());
      row.Undone = undone.Contains(row.Id);
      Tweaks.Add(row);
    }
  }

  public void Undo(TweakUndoRow row)
  {
    int failed = 0;
    foreach (string args in row.Commands)
    {
      using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"), args) { CreateNoWindow = true, UseShellExecute = false });
      p!.WaitForExit();
      if (p.ExitCode != 0) failed++;   // valor que já não existe também "falha": o padrão já está de volta
    }
    row.Undone = true;
    File.AppendAllLines(undoneFile, [row.Id]);
    Status = failed == row.Commands.Count
      ? $"\"{row.Name}\": já estava no padrão do Windows."
      : $"\"{row.Name}\" desfeito. Alguns ajustes só valem depois de reiniciar ou sair e entrar de novo.";
  }
}

public partial class TweaksWindow : FluentWindow
{
  public TweaksWindow(TweaksViewModel vm)
  {
    DataContext = vm;
    InitializeComponent();
  }
}
