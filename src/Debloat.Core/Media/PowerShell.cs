using System.Diagnostics;
using System.Text;

namespace Debloat.Core.Media;

/// <summary>Roda um script do Windows PowerShell (cmdlets de disco e de drivers só existem lá).</summary>
internal static class PowerShell
{
  public static async Task<string> RunAsync(string script, CancellationToken ct = default)
  {
    string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';\n" + script));
    var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
      $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
    {
      CreateNoWindow = true,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8,
    };
    using var process = Process.Start(psi) ?? throw new InvalidOperationException("powershell.exe não abriu.");
    var stdout = process.StandardOutput.ReadToEndAsync(ct);
    var stderr = process.StandardError.ReadToEndAsync(ct);
    await process.WaitForExitAsync(ct);
    string output = await stdout, error = await stderr;
    if (process.ExitCode != 0)
    {
      throw new InvalidOperationException(error.Trim().Split('\n').FirstOrDefault()?.Trim() ?? $"PowerShell saiu com {process.ExitCode}.");
    }
    return output;
  }

  /// <summary>Texto seguro dentro de aspas simples no PowerShell.</summary>
  public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
