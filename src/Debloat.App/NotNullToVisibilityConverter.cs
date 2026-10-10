using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Debloat.App;

/// <summary>Mostra o elemento só quando o valor existe (ex.: aviso de versão nova).</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
