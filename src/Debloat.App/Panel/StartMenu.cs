using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Debloat.App.Panel;

/// <summary>Itens do Iniciar (Get-StartApps) e o "Fixar em Iniciar" do próprio Windows, quando ele oferece.</summary>
public static class StartMenu
{
  public record Entry(string Name, string Id);

  private const string AppsFolder = "shell:::{4234d49b-0245-4df3-b780-3893943456e1}";

  /// <summary>Atalhos que não são o app (desinstalar, manuais, sites).</summary>
  private static readonly string[] NotTheApp = ["uninstall", "desinstalar", "manual", "docs", "documentation", "website", "release notes", "readme", "help", "support"];

  public static async Task<IReadOnlyList<Entry>> ListAsync()
  {
    var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
      "-NoProfile -NonInteractive -Command \"Get-StartApps | Select-Object Name, AppID | ConvertTo-Json -Compress\"")
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      StandardOutputEncoding = Encoding.UTF8,
    };
    psi.Environment["PSModulePath"] = null;   // não herda módulos do PowerShell 7 se o painel for aberto por ele
    using var p = Process.Start(psi)!;
    string json = await p.StandardOutput.ReadToEndAsync();
    await p.WaitForExitAsync();
    var list = new List<Entry>();
    try
    {
      using var doc = JsonDocument.Parse(json);
      var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
      foreach (var item in items)
      {
        string? name = item.GetProperty("Name").GetString(), id = item.GetProperty("AppID").GetString();
        if (name is null || id is null || NotTheApp.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
        list.Add(new Entry(name, id));
      }
    }
    catch (JsonException) { }
    return list;
  }

  /// <summary>
  /// Nome do catálogo → item do Iniciar: igual, depois começando igual, depois a primeira palavra (≥ 4 letras) quando
  /// só um item tem ela. Ex.: "Wand (antigo WeMod)" → "Wand (WeMod)"; "Python 3.14" → "Python 3.14 (64-bit)".
  /// </summary>
  public static Entry? Match(string appName, IReadOnlyList<Entry> entries)
  {
    string target = Normalize(appName);
    var exact = entries.FirstOrDefault(e => Normalize(e.Name) == target);
    if (exact is not null) return exact;
    var prefix = entries.Where(e => Normalize(e.Name).StartsWith(target, StringComparison.Ordinal) || target.StartsWith(Normalize(e.Name), StringComparison.Ordinal) && Normalize(e.Name).Length >= 4)
      .OrderBy(e => e.Name.Length).FirstOrDefault();
    if (prefix is not null) return prefix;
    // "Mozilla Firefox" no catálogo, "Firefox" no Iniciar: o nome do Iniciar no fim do nome do catálogo.
    var suffix = entries.Where(e => Normalize(e.Name).Length >= 5 && target.EndsWith(Normalize(e.Name), StringComparison.Ordinal))
      .OrderByDescending(e => e.Name.Length).FirstOrDefault();
    if (suffix is not null) return suffix;
    string first = FirstWord(appName);
    if (first.Length < 4) return null;
    var byWord = entries.Where(e => FirstWord(e.Name) == first).ToList();
    return byWord.Count == 1 ? byWord[0] : null;
  }

  public static bool CanPin(string startId, string appName) => PinVerb(startId, appName) is not null;

  public static bool Pin(string? startId, string appName)
  {
    if (startId is null || PinVerb(startId, appName) is not { } verb) return false;
    verb.DoIt();
    return true;
  }

  /// <summary>
  /// O "Fixar em Iniciar" do próprio Windows (não há API oficial para fixar). Na lista de apps ele só aparece para
  /// alguns (Telegram, Steam, VLC sim; Chrome, Firefox, Discord não); no atalho (.lnk) do app no Iniciar aparece para
  /// todos — então o atalho é o plano B.
  /// </summary>
  private static dynamic? PinVerb(string startId, string appName)
  {
    try
    {
      dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
      foreach (dynamic item in shell.Namespace(AppsFolder).Items())
      {
        if ((string)item.Path != startId) continue;
        if (PinVerbOf(item) is { } verb) return verb;
        break;
      }
      if (Shortcut(appName) is { } lnk)
      {
        dynamic file = shell.Namespace(Path.GetDirectoryName(lnk)).ParseName(Path.GetFileName(lnk));
        return file is null ? null : PinVerbOf(file);
      }
    }
    catch (Exception e) when (e is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
    return null;
  }

  private static dynamic? PinVerbOf(dynamic item)
  {
    foreach (dynamic verb in item.Verbs())
    {
      string name = ((string)verb.Name).Replace("&", "");
      bool pin = name.Contains("Fixar", StringComparison.OrdinalIgnoreCase) || name.Contains("Pin to", StringComparison.OrdinalIgnoreCase);
      bool start = name.Contains("Iniciar", StringComparison.OrdinalIgnoreCase) || name.Contains("Start", StringComparison.OrdinalIgnoreCase);
      bool unpin = name.Contains("Desafixar", StringComparison.OrdinalIgnoreCase) || name.Contains("Unpin", StringComparison.OrdinalIgnoreCase);
      if (pin && start && !unpin) return verb;
    }
    return null;
  }

  /// <summary>Atalho do app nas pastas do Iniciar (todos os usuários e o atual), achado pelo nome.</summary>
  private static string? Shortcut(string appName)
  {
    var shortcuts = new[]
    {
      Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
      Environment.GetFolderPath(Environment.SpecialFolder.Programs),
    }
    .Where(Directory.Exists)
    .SelectMany(d => Directory.EnumerateFiles(d, "*.lnk", SearchOption.AllDirectories))
    .Select(f => new Entry(Path.GetFileNameWithoutExtension(f), f))
    .Where(e => !NotTheApp.Any(w => e.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
    .ToList();
    return Match(appName, shortcuts)?.Id;
  }

  private static string Normalize(string name) => new(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

  private static string FirstWord(string name) => Normalize(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "");
}
