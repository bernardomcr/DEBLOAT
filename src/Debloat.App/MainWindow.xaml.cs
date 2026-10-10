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
      await Task.WhenAll(vm.LoadReleasesAsync(), vm.RefreshUsbCommand.ExecuteAsync(null), vm.LoadMigrationAsync());
    };
  }

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
}
