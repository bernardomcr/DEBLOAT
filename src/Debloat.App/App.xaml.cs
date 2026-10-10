using System.Windows;
using System.Windows.Threading;
using Debloat.App.Panel;
using Wpf.Ui.Appearance;

namespace Debloat.App;

public partial class App : Application
{
  protected override void OnStartup(StartupEventArgs e)
  {
    // Segue o tema claro/escuro do Windows; as cores da marca acompanham a troca.
    ApplicationThemeManager.Changed += (theme, _) => Brand.Apply(theme);
    ApplicationThemeManager.ApplySystemTheme(updateAccent: false);
#if DEBUG
    // Só no desenvolvimento: DEBLOAT_TEMA=claro|escuro força o tema (para conferir os dois nas imagens).
    switch (Environment.GetEnvironmentVariable("DEBLOAT_TEMA"))
    {
      case "claro": ApplicationThemeManager.Apply(ApplicationTheme.Light, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false); break;
      case "escuro": ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false); break;
    }
#endif
    Brand.Apply(ApplicationThemeManager.GetAppTheme());
    base.OnStartup(e);

    if (e.Args.Length >= 2 && e.Args[0] == "--ajustes")
    {
      // Atalho "DEBLOAT - Ajustes" do Windows instalado: desfazer ajuste por ajuste.
      var tweaks = new TweaksWindow(new TweaksViewModel(e.Args[1]));
      tweaks.Show();
#if DEBUG
      if (Environment.GetEnvironmentVariable("DEBLOAT_PRINTS") is { Length: > 0 } tweakPrints)
      {
        tweaks.Dispatcher.InvokeAsync(() => { Prints.Save(tweaks, System.IO.Path.Combine(tweakPrints, "ajustes.png")); Shutdown(); }, DispatcherPriority.ApplicationIdle);
      }
#endif
      return;
    }
    if (e.Args.Length >= 2 && e.Args[0] == "--painel")
    {
      // --abrir: já abre a lista (o teste na VM não tem como clicar no aviso).
      StartPanel(e.Args[1], e.Args.Length >= 3 && int.TryParse(e.Args[2], out int parent) ? parent : 0, e.Args.Contains("--abrir"));
      return;
    }
    new MainWindow().Show();
  }

  /// <summary>
  /// Modo painel (primeiro login do Windows instalado pelo DEBLOAT, chamado pelo FirstLogon.ps1): aviso pequeno no
  /// canto; clicar abre a lista dos apps. "Minimizar" volta para o aviso. Fecha sozinho quando termina.
  /// </summary>
  private void StartPanel(string statePath, int parentId, bool openList)
  {
    ShutdownMode = ShutdownMode.OnExplicitShutdown;
    var vm = new PanelViewModel(statePath, parentId);
    var notice = new NoticeWindow(vm);
    PanelWindow? panel = null;

    void Quit()
    {
      if (panel is not null) panel.Quitting = true;
      Shutdown();
    }

    PanelWindow OpenPanel()
    {
      if (panel is null)
      {
        panel = new PanelWindow(vm);
        panel.MinimizeRequested += () => { panel.Hide(); notice.Show(); };
        panel.Closed += (_, _) => Quit();
      }
      notice.Hide();
      panel.Show();
      panel.Activate();
      return panel;
    }

    notice.OpenRequested += () => OpenPanel();
    vm.PropertyChanged += (_, args) =>
    {
      // Terminou (ou o script morreu): o aviso some em alguns segundos; a lista, se estiver aberta, fica até fechar.
      if (args.PropertyName is nameof(PanelViewModel.Finished) or nameof(PanelViewModel.ScriptGone) && (vm.Finished || vm.ScriptGone))
      {
        var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        later.Tick += (_, _) =>
        {
          later.Stop();
          notice.Close();
          if (panel is not { IsVisible: true }) Quit();
        };
        later.Start();
      }
    };
    vm.Start();
    notice.Show();
    if (openList) OpenPanel();
#if DEBUG
    // Só no desenvolvimento: DEBLOAT_PRINTS=<pasta> salva o aviso e a lista e fecha.
    if (Environment.GetEnvironmentVariable("DEBLOAT_PRINTS") is { Length: > 0 } prints)
    {
      var shot = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
      shot.Tick += (_, _) =>
      {
        shot.Stop();
        Prints.Save(notice, System.IO.Path.Combine(prints, "aviso.png"));
        var opened = OpenPanel();
        opened.Dispatcher.InvokeAsync(() =>
        {
          Prints.Save(opened, System.IO.Path.Combine(prints, "painel.png"));
          Quit();
        }, DispatcherPriority.ApplicationIdle);
      };
      shot.Start();
    }
#endif
  }
}
