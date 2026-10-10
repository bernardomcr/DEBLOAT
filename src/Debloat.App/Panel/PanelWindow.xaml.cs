using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace Debloat.App.Panel;

/// <summary>Lista dos apps com o estado de cada um; prontos ganham Abrir e (quando o Windows deixa) Fixar no Iniciar.</summary>
public partial class PanelWindow : FluentWindow
{
  private readonly PanelViewModel vm;

  public event Action? MinimizeRequested;

  /// <summary>O app está fechando de vez: o X não volta para o aviso.</summary>
  public bool Quitting { get; set; }

  public PanelWindow(PanelViewModel vm)
  {
    this.vm = vm;
    DataContext = vm;
    InitializeComponent();
    Loaded += (_, _) => UpdateFinished();
    vm.PropertyChanged += OnChanged;
  }

  private void OnChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(PanelViewModel.Finished)) UpdateFinished();
  }

  private void UpdateFinished()
  {
    if (vm.Finished)
    {
      Heading.Text = "Seu PC está pronto.";
      MinimizeButton.Visibility = Visibility.Collapsed;
      Bar.BeginAnimation(Canvas.LeftProperty, null);
      Canvas.SetLeft(Bar, 0);
      Bar.Width = Track.ActualWidth;
      return;
    }
    var run = new DoubleAnimation(-Bar.Width, Math.Max(Track.ActualWidth, 600), TimeSpan.FromSeconds(2)) { RepeatBehavior = RepeatBehavior.Forever };
    Bar.BeginAnimation(Canvas.LeftProperty, run);
  }

  private void Minimize_Click(object sender, RoutedEventArgs e) => MinimizeRequested?.Invoke();

  protected override void OnClosing(CancelEventArgs e)
  {
    // Fechar no meio da instalação só volta para o aviso pequeno; depois de pronto, fecha de vez.
    if (!vm.Finished && !Quitting)
    {
      e.Cancel = true;
      MinimizeRequested?.Invoke();
    }
    base.OnClosing(e);
  }
}
