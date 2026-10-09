using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Debloat.Core.Saves;

/// <summary>Um jogo com saves encontrados. Source = "Ludusavi" (manifesto oficial) ou o nome do emulador/crack.</summary>
public record SaveGame(string Name, string Source, long Bytes, int Files, string? Folder = null, string? OriginalPath = null)
{
  public string Key => Source == SaveScanner.LudusaviSource ? Name : $"{Source}|{OriginalPath}";
}

public record SaveRoot(string Label, string Path, string Layout, bool Verified);

/// <summary>
/// Varre o PC atrás de saves: manifesto do Ludusavi (PCGamingWiki) + pastas de emuladores/cracks
/// (Data\save-locations.json), com o AppID da Steam traduzido para o nome pelo próprio Ludusavi.
/// </summary>
public sealed class SaveScanner(HttpClient http, string toolsDir)
{
  public const string LudusaviSource = "Ludusavi";

  private string LudusaviExe => Path.Combine(toolsDir, "ludusavi", "ludusavi.exe");
  private string LudusaviConfig => Path.Combine(toolsDir, "ludusavi-config");

  public static IReadOnlyList<SaveRoot> Roots()
  {
    var doc = JsonNode.Parse(Resources.Data("save-locations.json"))!;
    return doc["roots"]!.AsArray().Select(r => new SaveRoot(
      (string)r!["label"]!, (string)r["path"]!, (string)r["layout"]!, (bool)r["verified"]!)).ToList();
  }

  /// <summary>Baixa o Ludusavi (versão mais recente, GitHub oficial) se ainda não tiver.</summary>
  public async Task EnsureLudusaviAsync(CancellationToken ct = default)
  {
    if (File.Exists(LudusaviExe)) return;
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/mtkennerly/ludusavi/releases/latest");
    request.Headers.UserAgent.ParseAdd("DEBLOAT");
    using var response = await http.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();
    var release = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
    string url = release["assets"]!.AsArray()
      .Select(a => (string)a!["browser_download_url"]!)
      .First(u => u.EndsWith("-win64.zip", StringComparison.OrdinalIgnoreCase));
    byte[] zip = await http.GetByteArrayAsync(url, ct);
    string dir = Directory.CreateDirectory(Path.GetDirectoryName(LudusaviExe)!).FullName;
    using var archive = new ZipArchive(new MemoryStream(zip));
    archive.GetEntry("ludusavi.exe")!.ExtractToFile(LudusaviExe, overwrite: true);
  }

  public async Task<IReadOnlyList<SaveGame>> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
  {
    await EnsureLudusaviAsync(ct);
    var games = new List<SaveGame>();

    progress?.Report("Procurando saves de jogos conhecidos (manifesto do Ludusavi)...");
    var preview = JsonNode.Parse(await LudusaviAsync(["backup", "--preview", "--api"], ct))!;
    foreach (var (name, node) in preview["games"]!.AsObject())
    {
      var files = node!["files"]?.AsObject();
      if (files is null || files.Count == 0) continue;
      long bytes = files.Sum(f => (long?)f.Value!["bytes"] ?? 0);
      games.Add(new SaveGame(name, LudusaviSource, bytes, files.Count));
    }

    progress?.Report("Procurando saves de emuladores e cracks...");
    foreach (var root in Roots())
    {
      string path = Environment.ExpandEnvironmentVariables(root.Path);
      if (!Directory.Exists(path)) continue;
      foreach (var (folder, appId) in GameFolders(path, root.Layout))
      {
        var (bytes, count) = Measure(folder);
        if (count == 0) continue;
        string name = appId is null ? Path.GetFileName(folder) : await NameForSteamIdAsync(appId, ct) ?? $"AppID {appId}";
        string original = TokenizePath(folder);
        if (games.Any(g => g.OriginalPath == original)) continue;     // raiz genérica cobrindo uma específica
        games.Add(new SaveGame(name, root.Label, bytes, count, folder, original));
      }
    }
    return games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
  }

  /// <summary>Salva os jogos escolhidos em &lt;destino&gt;\saves. O Ludusavi vai junto, para restaurar no Windows novo.</summary>
  public async Task BackupAsync(IEnumerable<SaveGame> selected, string destination, CancellationToken ct = default)
  {
    var list = selected.ToList();
    string saves = Directory.CreateDirectory(Path.Combine(destination, "saves")).FullName;

    var official = list.Where(g => g.Source == LudusaviSource).Select(g => g.Name).ToList();
    if (official.Count > 0)
    {
      await LudusaviAsync(["backup", "--path", Path.Combine(saves, "ludusavi"), "--force", "--api", "--format", "simple", .. official], ct);
    }

    var emulated = new JsonArray();
    foreach (var game in list.Where(g => g.Source != LudusaviSource))
    {
      string stored = Path.Combine("emuladores", Sanitize(game.Source), Path.GetFileName(game.Folder!));
      CopyDirectory(game.Folder!, Path.Combine(saves, stored));
      emulated.Add(new JsonObject { ["name"] = game.Name, ["source"] = game.Source, ["original"] = game.OriginalPath, ["stored"] = stored });
    }
    await File.WriteAllTextAsync(Path.Combine(saves, "emuladores.json"), emulated.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);

    string tools = Directory.CreateDirectory(Path.Combine(destination, "ferramentas")).FullName;
    File.Copy(LudusaviExe, Path.Combine(tools, "ludusavi.exe"), overwrite: true);
  }

  // --- auxiliares ---

  private readonly Dictionary<string, string?> names = [];

  private async Task<string?> NameForSteamIdAsync(string appId, CancellationToken ct)
  {
    if (names.TryGetValue(appId, out var cached)) return cached;
    string? name = null;
    try
    {
      var found = JsonNode.Parse(await LudusaviAsync(["--no-manifest-update", "find", "--steam-id", appId, "--api"], ct));
      name = found?["games"]?.AsObject().Select(p => p.Key).FirstOrDefault();
    }
    catch (InvalidOperationException)
    {
      // Ludusavi não conhece o AppID: fica "AppID 123".
    }
    return names[appId] = name;
  }

  private static IEnumerable<(string Folder, string? AppId)> GameFolders(string root, string layout) => layout switch
  {
    "appid" => Directory.EnumerateDirectories(root).Where(d => IsAppId(Path.GetFileName(d))).Select(d => (d, (string?)Path.GetFileName(d))),
    "group-appid" => Directory.EnumerateDirectories(root)
      .SelectMany(g => Directory.EnumerateDirectories(g).Where(d => IsAppId(Path.GetFileName(d))))
      .Select(d => (d, (string?)Path.GetFileName(d))),
    _ => Directory.EnumerateDirectories(root).Select(d => (d, (string?)null)),
  };

  private static bool IsAppId(string name) => name.Length is >= 3 and <= 9 && name.All(char.IsAsciiDigit);

  private static (long Bytes, int Count) Measure(string folder)
  {
    long bytes = 0;
    int count = 0;
    foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
    {
      bytes += file.Length;
      count++;
    }
    return (bytes, count);
  }

  /// <summary>Troca o começo do caminho por variáveis (%APPDATA%…) para restaurar com outro nome de usuário.</summary>
  public static string TokenizePath(string path)
  {
    foreach (var variable in new[] { "LOCALAPPDATA", "APPDATA", "PUBLIC", "PROGRAMDATA", "USERPROFILE" })
    {
      string? value = Environment.GetEnvironmentVariable(variable);
      if (!string.IsNullOrEmpty(value) && path.StartsWith(value + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
      {
        return $"%{variable}%" + path[value.Length..];
      }
    }
    return path;
  }

  private static string Sanitize(string name) =>
    string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));

  private static void CopyDirectory(string from, string to)
  {
    foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
    {
      string target = Path.Combine(to, Path.GetRelativePath(from, file));
      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      File.Copy(file, target, overwrite: true);
    }
  }

  private async Task<string> LudusaviAsync(IEnumerable<string> args, CancellationToken ct)
  {
    var psi = new ProcessStartInfo(LudusaviExe)
    {
      CreateNoWindow = true,
      UseShellExecute = false,
      RedirectStandardInput = true,      // o Ludusavi lê nomes de jogos da entrada padrão até ela fechar
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = Encoding.UTF8,
    };
    psi.ArgumentList.Add("--config");
    psi.ArgumentList.Add(LudusaviConfig);
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var process = Process.Start(psi)!;
    process.StandardInput.Close();
    var stdout = process.StandardOutput.ReadToEndAsync(ct);
    var stderr = process.StandardError.ReadToEndAsync(ct);
    await process.WaitForExitAsync(ct);
    string output = await stdout;
    if (process.ExitCode != 0 || output.Length == 0)
    {
      throw new InvalidOperationException($"Ludusavi falhou: {(await stderr).Trim()}");
    }
    return output;
  }
}
