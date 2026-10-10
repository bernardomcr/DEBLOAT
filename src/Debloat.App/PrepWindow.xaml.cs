using System.Windows;
using Wpf.Ui.Controls;

namespace Debloat.App;

/// <summary>Lista o que está sendo baixado e montado; fechar só esconde, o trabalho continua.</summary>
public partial class PrepWindow : FluentWindow
{
  public PrepWindow(MainViewModel vm)
  {
    DataContext = vm;
    InitializeComponent();
  }

  private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
