using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Debloat.App;

/// <summary>
/// Cores da identidade DEBLOAT! (docs/marca/implementation/tokens.json). "Grita na marca, fala baixo na interface":
/// fundo neutro, laranja só no ícone, no indicador da aba e na ação principal (com o tom acessível de cada tema).
/// </summary>
public static class Brand
{
  private static readonly Dictionary<string, (string Light, string Dark)> Tokens = new()
  {
    ["DebloatBackground"] = ("#FFFFFF", "#202124"),
    ["DebloatSurface"] = ("#F5F5F5", "#2B2C30"),
    ["DebloatText"] = ("#202124", "#F5F5F5"),
    ["DebloatMuted"] = ("#63666B", "#B4B6BC"),
    ["DebloatBorder"] = ("#DEDFE2", "#45474D"),
    ["DebloatAccent"] = ("#BD3518", "#FF896D"),
    ["DebloatAccentSoft"] = ("#FFF0EB", "#432D27"),
    ["DebloatWarning"] = ("#865900", "#EBC272"),
    ["DebloatOrange"] = ("#F4512A", "#F4512A"),
  };

  public static void Apply(ApplicationTheme theme)
  {
    bool dark = theme == ApplicationTheme.Dark;
    var resources = Application.Current.Resources;
    foreach (var (key, (light, darkValue)) in Tokens)
    {
      var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? darkValue : light));
      brush.Freeze();
      resources[key] = brush;
    }
    // Botão principal, caixas marcadas e barras do WPF-UI usam a cor de destaque: o laranja acessível do tema.
    var accent = (Color)ColorConverter.ConvertFromString(dark ? Tokens["DebloatAccent"].Dark : Tokens["DebloatAccent"].Light);
    ApplicationAccentColorManager.Apply(accent, accent, accent, accent);
  }
}
