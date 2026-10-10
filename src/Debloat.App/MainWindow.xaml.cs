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
      await Task.WhenAll(vm.LoadReleasesAsync(), vm.RefreshUsbCommand.ExecuteAsync(null), vm.LoadMigrationAsync());
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
      var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
      var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY),
        dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
      bitmap.Render(this);
      var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
      png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
      using var file = System.IO.File.Create(System.IO.Path.Combine(folder, $"{i + 1}-{((System.Windows.Controls.TabItem)Tabs.Items[i]).Header}.png"));
      png.Save(file);
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
}
