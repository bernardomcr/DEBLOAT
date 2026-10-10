using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Debloat.App.Panel;

/// <summary>Aviso pequeno no canto da tela; clicar abre a lista dos apps (PanelWindow).</summary>
public partial class NoticeWindow : Window
{
  private readonly PanelViewModel vm;

  public event Action? OpenRequested;

  public NoticeWindow(PanelViewModel vm)
  {
    this.vm = vm;
    DataContext = vm;
    InitializeComponent();
    Loaded += (_, _) =>
    {
      var area = SystemParameters.WorkArea;
      Left = area.Right - Width - 16;
      Top = area.Bottom - Height - 16;
      UpdateBar();
    };
    vm.PropertyChanged += OnChanged;
  }

  private void OnChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(PanelViewModel.Finished)) UpdateBar();
  }

  private void UpdateBar()
  {
    if (vm.Finished)
    {
      Bar.BeginAnimation(Canvas.LeftProperty, null);
      Canvas.SetLeft(Bar, 0);
      Bar.Width = Track.ActualWidth;
      return;
    }
    // Trecho correndo da esquerda para a direita, sem parar.
    var run = new DoubleAnimation(-Bar.Width, Math.Max(Track.ActualWidth, 300), TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever };
    Bar.BeginAnimation(Canvas.LeftProperty, run);
  }

  private void Open_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenRequested?.Invoke();

  private void Hide_Click(object sender, RoutedEventArgs e)
  {
    e.Handled = true;
    Hide();
  }
}
