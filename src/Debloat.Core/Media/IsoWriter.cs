using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace Debloat.Core.Media;

/// <summary>
/// Gera uma ISO bootável (UEFI + BIOS) da pasta de instalação com o IMAPI2 que vem no Windows.
/// Serve para testar em máquina virtual e para quem usa Ventoy.
/// </summary>
[SupportedOSPlatform("windows")]
public static class IsoWriter
{
  private const int FsiFileSystemUdf = 4;
  private const int MediaTypeBdr = 0x13;      // BD-R: tira o limite de tamanho de DVD
  private const byte PlatformX86 = 0, PlatformEfi = 0xEF;

  public static void Write(string mediaDir, string isoPath, string volumeName = "DEBLOAT_WIN11")
  {
    dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage", throwOnError: true)!)!;
    fs.ChooseImageDefaultsForMediaType(MediaTypeBdr);
    fs.FileSystemsToCreate = FsiFileSystemUdf;
    fs.UDFRevision = 0x102;
    fs.VolumeName = volumeName;

    var boots = new List<object>();
    var streams = new List<IStream>();
    try
    {
      AddBoot(Path.Combine(mediaDir, "boot", "etfsboot.com"), PlatformX86, boots, streams);
      AddBoot(Path.Combine(mediaDir, "efi", "microsoft", "boot", "efisys.bin"), PlatformEfi, boots, streams);
      if (boots.Count > 0) fs.BootImageOptionsArray = boots.ToArray();

      fs.Root.AddTree(mediaDir, false);
      dynamic result = fs.CreateResultImage();
      var image = (IStream)result.ImageStream;
      using var output = File.Create(isoPath);
      byte[] buffer = new byte[4 << 20];
      IntPtr readPtr = Marshal.AllocHGlobal(sizeof(int));
      try
      {
        while (true)
        {
          image.Read(buffer, buffer.Length, readPtr);
          int read = Marshal.ReadInt32(readPtr);
          if (read <= 0) break;
          output.Write(buffer, 0, read);
        }
      }
      finally
      {
        Marshal.FreeHGlobal(readPtr);
        Marshal.ReleaseComObject(image);
      }
    }
    finally
    {
      foreach (var s in streams) Marshal.ReleaseComObject(s);
    }
  }

  private static void AddBoot(string file, byte platform, List<object> boots, List<IStream> streams)
  {
    if (!File.Exists(file)) return;
    SHCreateStreamOnFileEx(file, 0 /* STGM_READ */, 0, false, null, out IStream stream);
    streams.Add(stream);
    dynamic boot = Activator.CreateInstance(Type.GetTypeFromProgID("IMAPI2FS.BootOptions", throwOnError: true)!)!;
    boot.AssignBootImage(stream);
    boot.PlatformId = platform;
    boot.Emulation = 0;           // sem emulação
    boot.Manufacturer = "DEBLOAT";
    boots.Add(boot);
  }

  [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
  private static extern void SHCreateStreamOnFileEx(string file, uint mode, uint attributes, bool create, IStream? template, out IStream stream);
}
