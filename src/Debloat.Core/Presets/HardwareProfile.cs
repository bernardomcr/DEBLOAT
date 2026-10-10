using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Debloat.Core.Presets;

/// <summary>
/// O que o preset precisa saber do PC antes de formatar. Detectado na máquina atual,
/// porque é nela que o programa roda antes da instalação.
/// </summary>
public record HardwareProfile(bool HasBattery, bool HasIrCamera, bool HasPenOrTouch)
{
  /// <summary>Placa de vídeo NVIDIA/AMD (marca o NVIDIA App/AMD Adrenalin na aba Apps).</summary>
  public bool HasNvidiaGpu { get; init; }

  public bool HasAmdGpu { get; init; }

  /// <summary>Desktop comum: sem bateria, sem câmera IR, sem caneta.</summary>
  public static HardwareProfile Desktop => new(false, false, false);

  [SupportedOSPlatform("windows")]
  public static HardwareProfile Detect() => new(
    HasBattery: Any("SELECT * FROM Win32_Battery"),
    HasIrCamera: Any("SELECT Name FROM Win32_PnPEntity WHERE (PNPClass = 'Camera' OR PNPClass = 'Image') AND Name LIKE '%IR%'"),
    HasPenOrTouch: (GetSystemMetrics(SM_DIGITIZER) & NID_READY) != 0
  )
  {
    HasNvidiaGpu = Any("SELECT * FROM Win32_VideoController WHERE PNPDeviceID LIKE '%VEN_10DE%'"),
    HasAmdGpu = Any("SELECT * FROM Win32_VideoController WHERE PNPDeviceID LIKE '%VEN_1002%'"),
  };

  [SupportedOSPlatform("windows")]
  private static bool Any(string query)
  {
    try
    {
      using var searcher = new ManagementObjectSearcher(query);
      using var results = searcher.Get();
      return results.Count > 0;
    }
    catch (ManagementException)
    {
      return false;
    }
  }

  private const int SM_DIGITIZER = 94;
  private const int NID_READY = 0x80;

  [DllImport("user32.dll")]
  private static extern int GetSystemMetrics(int index);
}
