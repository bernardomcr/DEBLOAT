#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Debloat.App;

/// <summary>Só no desenvolvimento: salva uma janela em PNG (DEBLOAT_PRINTS), para conferir o visual sem abrir o app.</summary>
internal static class Prints
{
  public static void Save(Window window, string path)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    window.UpdateLayout();
    var dpi = VisualTreeHelper.GetDpi(window);
    var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY),
      dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
    bitmap.Render(window);
    var png = new PngBitmapEncoder();
    png.Frames.Add(BitmapFrame.Create(bitmap));
    using var file = File.Create(path);
    png.Save(file);
  }
}
#endif
