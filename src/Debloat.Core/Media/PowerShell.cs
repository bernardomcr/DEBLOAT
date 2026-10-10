using System.Diagnostics;
using System.Text;

namespace Debloat.Core.Media;

/// <summary>Roda um script do Windows PowerShell (cmdlets de disco e de drivers só existem lá).</summary>
internal static class PowerShell
{
  public static async Task<string> RunAsync(string script, CancellationToken ct = default)
  {
    string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';\n" + script + "\nexit 0"));   // sem o exit 0, um aviso ignorado no último comando vira "falhou"
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
      throw new InvalidOperationException(ErrorText(error) is { Length: > 0 } text ? text : $"PowerShell saiu com {process.ExitCode}.");
    }
    return output;
  }

  /// <summary>
  /// Mensagem de erro legível. Quando o PowerShell escreve progresso antes do erro, tudo vem em CLIXML
  /// ("#&lt; CLIXML" + &lt;S S="Error"&gt;...); sem isto a mensagem era só "#&lt; CLIXML" (VM, 10/10/2026).
  /// </summary>
  internal static string ErrorText(string stderr)
  {
    string text = stderr.Trim();
    if (text.StartsWith("#< CLIXML", StringComparison.Ordinal))
    {
      text = string.Concat(System.Text.RegularExpressions.Regex.Matches(text, "<S S=\"Error\">(.*?)</S>")
        .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Replace("_x000D_", "").Replace("_x000A_", "\n")));
    }
    // Só a mensagem: sem as linhas "No linha:..."/"At line:..." e o resto da posição no script.
    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .TakeWhile(l => !l.StartsWith("No linha", StringComparison.Ordinal) && !l.StartsWith("At line", StringComparison.Ordinal));
    return string.Join(" ", lines).Trim();
  }

  /// <summary>Texto seguro dentro de aspas simples no PowerShell.</summary>
  public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
