using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Debloat.Core.Saves;

/// <summary>
/// Um programa instalado neste PC. Com WingetId, é baixado e instalado no Windows novo como os apps do catálogo;
/// sem, dá para levar a pasta (Folder) — serve para programas portáteis, não para os que dependem de instalador,
/// serviço ou driver.
/// </summary>
public record InstalledProgram(string Name, string? WingetId, string? Folder, string? Exe, long Bytes);

[SupportedOSPlatform("windows")]
public static partial class InstalledPrograms
{
  /// <param name="catalog">Apps do catálogo (aba Apps): ficam de fora, pelo ID do winget ou pelo nome.</param>
  public static async Task<IReadOnlyList<InstalledProgram>> DetectAsync(IReadOnlyCollection<Catalog.AppEntry> catalog, CancellationToken ct = default)
  {
    var catalogPackages = catalog.Select(a => a.Package).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var catalogNames = catalog.Select(a => a.Name).ToList();
    var registry = ReadRegistry();
    var winget = await WingetListAsync(ct);
    var result = new List<InstalledProgram>();
    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var (name, id) in winget)
    {
      // O catálogo (aba Apps) e o próprio Windows (Microsoft.*) já cobrem.
      if (catalogPackages.Contains(id) || Noise(name) || id.StartsWith("Microsoft.", StringComparison.Ordinal)) continue;
      var entry = registry.FirstOrDefault(r => SameName(r.Name, name));
      if (entry.Name is not null) used.Add(entry.Name);
      result.Add(new InstalledProgram(name.TrimEnd('…'), id, null, null, 0));
    }
    foreach (var entry in registry)
    {
      if (used.Contains(entry.Name) || Noise(entry.Name) || entry.Folder is null || FromLauncher(entry.Folder)) continue;
      // "Wand" no PC é o "Wand (antigo WeMod)" do catálogo: vale também a primeira palavra do nome.
      if (catalogNames.Any(n => SameName(entry.Name, n) || Normalize(entry.Name) == Normalize(n.Split(' ')[0]))) continue;
      if (winget.Any(w => SameName(entry.Name, w.Name))) continue;
      long bytes = Migration.Size(entry.Folder, []);
      if (bytes == 0) continue;
      result.Add(new InstalledProgram(entry.Name, null, entry.Folder, entry.Exe, bytes));
    }
    return result.DistinctBy(p => p.Name).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
  }

  /// <summary>Jogos da Steam/Epic/Riot/EA/Ubisoft/Xbox voltam pelo próprio launcher (os saves vão pelo backup de saves).</summary>
  private static bool FromLauncher(string folder) => LauncherRegex().IsMatch(folder.Replace('/', '\\'));

  /// <summary>Atualizações, runtimes e drivers: não são "programas" que o usuário escolheria levar.</summary>
  private static bool Noise(string name) => NoiseRegex().IsMatch(name);

  private static bool SameName(string a, string b)
  {
    string x = Normalize(a), y = Normalize(b.TrimEnd('…'));
    return x == y || (y.Length >= 6 && x.StartsWith(y, StringComparison.Ordinal)) || (x.Length >= 6 && y.StartsWith(x, StringComparison.Ordinal));
  }

  private static string Normalize(string name) => new(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

  private static List<(string Name, string? Folder, string? Exe)> ReadRegistry()
  {
    var list = new List<(string, string?, string?)>();
    foreach (var (hive, path) in new[]
    {
      (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
      (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
      (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    })
    {
      using var root = hive.OpenSubKey(path);
      if (root is null) continue;
      foreach (string sub in root.GetSubKeyNames())
      {
        using var key = root.OpenSubKey(sub);
        if (key?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
        if (key.GetValue("SystemComponent") is int system && system == 1) continue;
        if (key.GetValue("ParentKeyName") is not null || key.GetValue("ReleaseType") is string) continue;
        string? folder = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"').TrimEnd('\\');
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder) || folder.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)
          || Path.GetPathRoot(folder) == folder + "\\" || Path.GetPathRoot(folder) == folder)
        {
          folder = null;   // sem pasta, pasta do Windows ou um disco inteiro: nada para levar
        }
        // Executável principal: o ícone do desinstalador costuma apontar para ele.
        string? exe = (key.GetValue("DisplayIcon") as string)?.Split(',')[0].Trim().Trim('"');
        if (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || folder is null || !exe.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(exe))
        {
          exe = folder is null ? null : Directory.EnumerateFiles(folder, "*.exe").FirstOrDefault(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
        }
        list.Add((name.Trim(), folder, exe));
      }
    }
    return list;
  }

  /// <summary>"winget list --source winget": só o que o winget sabe instalar, com o ID oficial.</summary>
  private static async Task<List<(string Name, string Id)>> WingetListAsync(CancellationToken ct)
  {
    var psi = new ProcessStartInfo("winget.exe")
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = Encoding.UTF8,
    };
    foreach (string a in new[] { "list", "--source", "winget", "--accept-source-agreements", "--disable-interactivity" }) psi.ArgumentList.Add(a);
    try
    {
      using var p = Process.Start(psi)!;
      string output = await p.StandardOutput.ReadToEndAsync(ct);
      await p.WaitForExitAsync(ct);
      return ParseList(output);
    }
    catch (System.ComponentModel.Win32Exception)
    {
      return [];   // sem winget neste PC: só a parte das pastas
    }
  }

  /// <summary>Tabela do winget: as colunas são achadas pela linha de título (vale para qualquer idioma).</summary>
  public static List<(string Name, string Id)> ParseList(string output)
  {
    // Linhas em CRLF; um "\r" sozinho é o winget redesenhando a linha (progresso): vale o que sobra visível.
    var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l)
      .Select(l => l.TrimEnd()).ToList();
    int dashes = lines.FindIndex(l => l.Length > 10 && l.Trim('-').Length == 0);
    if (dashes < 1) return [];
    string header = lines[dashes - 1].TrimStart();
    var columns = HeaderColumnRegex().Matches(header).Select(m => m.Index).ToList();
    if (columns.Count < 3) return [];
    int idStart = columns[1], versionStart = columns[2];
    var rows = new List<(string, string)>();
    foreach (string line in lines.Skip(dashes + 1))
    {
      if (line.Length <= versionStart) continue;
      string name = line[..idStart].Trim(), id = line[idStart..versionStart].Trim();
      if (name.Length > 0 && id.Contains('.') && !id.Contains(' ')) rows.Add((name, id));
    }
    return rows;
  }

  [GeneratedRegex(@"\\steamapps\\|\\Epic Games\\|\\Riot Games\\|\\EA Games\\|\\Ubisoft Game Launcher\\games\\|\\XboxGames\\", RegexOptions.IgnoreCase)]
  private static partial Regex LauncherRegex();

  [GeneratedRegex(@"\S+")]
  private static partial Regex HeaderColumnRegex();

  [GeneratedRegex(@"Visual C\+\+|\.NET|Redistributable|Runtime|Windows SDK|Software Development Kit|Driver|Update for|Hotfix|\bKB\d+|Microsoft Edge|WebView|Windows App Certification|DirectX|^Microsoft |Visual Studio|^NVIDIA|Vanguard|Python Launcher", RegexOptions.IgnoreCase)]
  private static partial Regex NoiseRegex();
}
