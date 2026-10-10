using System.Windows;
using Wpf.Ui.Controls;

namespace Debloat.App;

public partial class MainWindow : FluentWindow
{
  private readonly MainViewModel vm = new();
  private PrepWindow? prep;

  public MainWindow()
  {
    DataContext = vm;
    InitializeComponent();
    vm.PreparationStarted += () =>
    {
      if (prep is { IsVisible: true }) return;
      prep = new PrepWindow(vm) { Owner = this };
      prep.Show();
    };
    Loaded += async (_, _) =>
    {
#if DEBUG
      // Só no desenvolvimento: DEBLOAT_PRINTS=<pasta> salva uma imagem de cada aba e fecha (para conferir o visual).
      if (Environment.GetEnvironmentVariable("DEBLOAT_PRINTS") is { Length: > 0 } prints)
      {
        _ = SavePrintsAsync(prints);
      }
#endif
      await Task.WhenAll(vm.LoadReleasesAsync(), vm.RefreshUsbCommand.ExecuteAsync(null), vm.LoadMigrationAsync(), vm.CheckUpdateAsync());
    };
  }

#if DEBUG
  private async Task SavePrintsAsync(string folder)
  {
    System.IO.Directory.CreateDirectory(folder);
    await Task.Delay(8000);   // versões do Windows e pendrives carregando
    for (int i = 0; i < Tabs.Items.Count; i++)
    {
      Tabs.SelectedIndex = i;
      await Task.Delay(700);
      Prints.Save(this, System.IO.Path.Combine(folder, $"{i + 1}-{((System.Windows.Controls.TabItem)Tabs.Items[i]).Header}.png"));
    }
    Close();
  }
#endif

  private async void WriteUsb_Click(object sender, RoutedEventArgs e)
  {
    if (vm.SelectedUsb is not { } drive)
    {
      vm.Status = "Escolha um pendrive primeiro.";
      return;
    }
    var first = await new Wpf.Ui.Controls.MessageBox
    {
      Title = "Apagar o pendrive?",
      Content = $"Tudo que está em\n\n{drive.Label}\n\nvai ser APAGADO para gravar a instalação do Windows.",
      PrimaryButtonText = "Continuar",
      CloseButtonText = "Cancelar",
    }.ShowDialogAsync();
    if (first != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

    var second = await new Wpf.Ui.Controls.MessageBox
    {
      Title = "Confirmação final",
      Content = $"Última chance: apagar {drive.FriendlyName.Trim()} ({drive.Size / 1e9:F1} GB)?",
      PrimaryButtonText = "Apagar e gravar",
      CloseButtonText = "Cancelar",
    }.ShowDialogAsync();
    if (second != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

    await vm.WriteUsbAsync(drive);
  }

  private async void InPlace_Click(object sender, RoutedEventArgs e)
  {
    var first = await new Wpf.Ui.Controls.MessageBox
    {
      Title = "Reinstalar o Windows neste PC?",
      Content = "O disco C: deste PC vai ser APAGADO e o Windows 11 reinstalado com o preset.\n\n"
        + "Só o que estiver marcado na aba Backup volta depois. O resto do C: é perdido.",
      PrimaryButtonText = "Continuar",
      CloseButtonText = "Cancelar",
    }.ShowDialogAsync();
    if (first != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

    var second = await new Wpf.Ui.Controls.MessageBox
    {
      Title = "Confirmação final",
      Content = "Última chance: apagar o C: e reinstalar? O PC reinicia sozinho no fim da preparação.",
      PrimaryButtonText = "Apagar e reinstalar",
      CloseButtonText = "Cancelar",
    }.ShowDialogAsync();
    if (second != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

    await vm.InstallInPlaceAsync();
  }
}
