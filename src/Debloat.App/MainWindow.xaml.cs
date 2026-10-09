using Wpf.Ui.Controls;

namespace Debloat.App;

public partial class MainWindow : FluentWindow
{
  public MainWindow()
  {
    DataContext = new MainViewModel();
    InitializeComponent();
  }
}
