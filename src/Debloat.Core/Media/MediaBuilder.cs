using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Debloat.Core.Media;

public record MediaStep(string Text, double Fraction);

/// <summary>
/// Monta a pasta de instalação (o conteúdo de um pendrive/ISO) a partir do .esd oficial, como a MCT faz:
/// índice 1 = arquivos do Setup, 2 = WinPE, 3 = Windows Setup, 4+ = edições. Usa o dism.exe do Windows
/// (exige administrador).
/// </summary>
public static partial class MediaBuilder
{
  /// <summary>Limite do FAT32; acima disso o install.wim vira install.swm em partes.</summary>
  public const long Fat32Limit = 4L * 1024 * 1024 * 1024 - 1;

  public static async Task BuildAsync(string esdPath, string mediaDir, string edition, byte[]? autounattend,
    IProgress<MediaStep>? progress = null, CancellationToken ct = default)
  {
    if (Directory.Exists(mediaDir)) Directory.Delete(mediaDir, recursive: true);
    Directory.CreateDirectory(mediaDir);
    string sources = Path.Combine(mediaDir, "sources");

    progress?.Report(new("Lendo a imagem da Microsoft", 0.02));
    int editionIndex = await FindEditionIndexAsync(esdPath, edition, ct);

    progress?.Report(new("Extraindo os arquivos de instalação", 0.05));
    await DismAsync($"/Apply-Image /ImageFile:\"{esdPath}\" /Index:1 /ApplyDir:\"{mediaDir}\"", ct);
    File.Delete(Path.Combine(mediaDir, "__chunk_data"));   // sobra do formato .esd; a mídia oficial não tem

    progress?.Report(new("Montando o ambiente de instalação (boot.wim)", 0.15));
    string bootWim = Path.Combine(sources, "boot.wim");
    if (File.Exists(bootWim)) File.Delete(bootWim);
    await DismAsync($"/Export-Image /SourceImageFile:\"{esdPath}\" /SourceIndex:2 /DestinationImageFile:\"{bootWim}\" /Compress:max", ct);
    await DismAsync($"/Export-Image /SourceImageFile:\"{esdPath}\" /SourceIndex:3 /DestinationImageFile:\"{bootWim}\" /Compress:max /Bootable", ct);

    progress?.Report(new($"Extraindo o Windows 11 ({edition})", 0.35));
    string installWim = Path.Combine(sources, "install.wim");
    await DismAsync($"/Export-Image /SourceImageFile:\"{esdPath}\" /SourceIndex:{editionIndex} /DestinationImageFile:\"{installWim}\" /Compress:max /CheckIntegrity", ct);

    if (new FileInfo(installWim).Length > Fat32Limit)
    {
      progress?.Report(new("Dividindo a imagem para caber em FAT32", 0.85));
      await DismAsync($"/Split-Image /ImageFile:\"{installWim}\" /SWMFile:\"{Path.Combine(sources, "install.swm")}\" /FileSize:3800", ct);
      File.Delete(installWim);
    }

    if (autounattend is not null)
    {
      await File.WriteAllBytesAsync(Path.Combine(mediaDir, "autounattend.xml"), autounattend, ct);
    }
    progress?.Report(new("Pronto", 1));
  }

  /// <summary>Procura o índice da edição pelo campo "Edition" (ex.: Professional), que não muda com o idioma.</summary>
  public static async Task<int> FindEditionIndexAsync(string esdPath, string edition, CancellationToken ct = default)
  {
    string info = await DismAsync($"/Get-WimInfo /WimFile:\"{esdPath}\"", ct);
    foreach (int index in IndexRegex().Matches(info).Select(m => int.Parse(m.Groups[1].Value)).Where(i => i >= 4))
    {
      string detail = await DismAsync($"/Get-WimInfo /WimFile:\"{esdPath}\" /Index:{index}", ct);
      var match = EditionRegex().Match(detail);
      if (match.Success && match.Groups[1].Value.Trim().Equals(edition, StringComparison.OrdinalIgnoreCase)) return index;
    }
    throw new InvalidDataException($"A edição '{edition}' não está nesta imagem.");
  }

  private static async Task<string> DismAsync(string arguments, CancellationToken ct)
  {
    var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "dism.exe"), "/English " + arguments)
    {
      CreateNoWindow = true,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = Encoding.UTF8,
    };
    using var process = Process.Start(psi) ?? throw new InvalidOperationException("dism.exe não abriu.");
    using var registration = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
    var stdout = process.StandardOutput.ReadToEndAsync(ct);
    var stderr = process.StandardError.ReadToEndAsync(ct);
    await process.WaitForExitAsync(ct);
    string output = await stdout + await stderr;
    if (process.ExitCode != 0)
    {
      string last = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault(l => l.Length > 0) ?? "";
      throw new InvalidOperationException($"DISM falhou (código {process.ExitCode}): {last}");
    }
    return output;
  }

  [GeneratedRegex(@"^Index\s*:\s*(\d+)", RegexOptions.Multiline)]
  private static partial Regex IndexRegex();

  [GeneratedRegex(@"^Edition\s*:\s*(.+)$", RegexOptions.Multiline)]
  private static partial Regex EditionRegex();
}
