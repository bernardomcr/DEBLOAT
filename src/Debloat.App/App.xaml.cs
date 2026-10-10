using System.Windows;
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
  }
}
