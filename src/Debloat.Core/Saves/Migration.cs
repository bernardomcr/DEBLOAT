using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Debloat.Core.Saves;

public enum MigrationKind { Wifi, ShareX, Firefox, Chromium, Folder, Program }

/// <summary>Uma coisa que dá para levar para o Windows novo. Path é a pasta de origem (vazio para Wi-Fi).</summary>
public record MigrationItem(string Id, MigrationKind Kind, string Name, string Path, long Bytes, string Note, bool DefaultSelected)
{
  public IReadOnlyList<string> ExcludeDirs { get; init; } = [];
  public IReadOnlyList<string> RunningProcesses { get; init; } = [];

  /// <summary>Programa levado pela pasta: o .exe ganha um atalho no Iniciar do Windows novo.</summary>
  public string? Shortcut { get; init; }
}

/// <summary>
/// Migração (fora os saves): Wi-Fi com senhas, ShareX, navegadores e pastas do usuário. Copia para
/// &lt;dados&gt;\migracao com um manifest.json; o FirstLogon.ps1 devolve cada item para o caminho de origem.
/// Limite conhecido: Chrome/Edge/Brave cifram cookies e senhas com uma chave da instalação do Windows
/// (DPAPI) — extensões, favoritos e histórico voltam; login e senhas, só pela sincronização da conta.
/// </summary>
public static class Migration
{
  /// <summary>Pastas de cache dos navegadores Chromium: grandes e recriadas sozinhas.</summary>
  public static readonly IReadOnlyList<string> ChromiumCaches =
  [
    "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "GrShaderCache", "GraphiteDawnCache",
    "ShaderCache", "CacheStorage", "ScriptCache", "Crashpad", "Safe Browsing", "component_crx_cache", "extensions_crx_cache",
    "optimization_guide_model_store", "OnDeviceHeadSuggestModel", "Snapshots", "BrowserMetrics", "GraphiteDawnCache",
  ];

  private static readonly (string Id, string Name, string Root, string Process)[] Chromium =
  [
    ("chrome", "Google Chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data", "chrome"),
    ("edge", "Microsoft Edge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data", "msedge"),
    ("brave", "Brave", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data", "brave"),
    ("vivaldi", "Vivaldi", @"%LOCALAPPDATA%\Vivaldi\User Data", "vivaldi"),
    ("opera", "Opera", @"%APPDATA%\Opera Software\Opera Stable", "opera"),
    ("operagx", "Opera GX", @"%APPDATA%\Opera Software\Opera GX Stable", "opera"),
  ];

  public static IReadOnlyList<MigrationItem> Detect()
  {
    var items = new List<MigrationItem>();

    if (HasWlan())
    {
      items.Add(new("wifi", MigrationKind.Wifi, "Redes Wi-Fi (com senhas)", "", 0, "Conecta sozinho no primeiro boot", true));
    }

    string sharex = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ShareX");
    if (Directory.Exists(sharex))
    {
      string[] skip = ["Screenshots", "Logs", "Backup"];
      items.Add(new("sharex", MigrationKind.ShareX, "Configuração do ShareX", sharex, Size(sharex, skip), "Atalhos, destinos e pós-captura (sem os prints)", true)
      { ExcludeDirs = skip, RunningProcesses = ["ShareX"] });
    }

    string firefox = Environment.ExpandEnvironmentVariables(@"%APPDATA%\Mozilla\Firefox");
    if (File.Exists(System.IO.Path.Combine(firefox, "profiles.ini")))
    {
      items.Add(new("firefox", MigrationKind.Firefox, "Firefox", firefox, Size(firefox, []), "Volta logado, com senhas, extensões e abas", true)
      { RunningProcesses = ["firefox"] });
    }

    foreach (var (id, name, root, process) in Chromium)
    {
      string path = Environment.ExpandEnvironmentVariables(root);
      if (!Directory.Exists(path)) continue;
      items.Add(new(id, MigrationKind.Chromium, name, path, Size(path, ChromiumCaches),
        "Extensões, favoritos e histórico; login e senhas voltam pela sincronização da conta", id != "edge")
      { ExcludeDirs = ChromiumCaches, RunningProcesses = [process] });
    }

    foreach (var (id, name, folder) in UserFolders())
    {
      if (!Directory.Exists(folder)) continue;
      items.Add(new($"pasta-{id}", MigrationKind.Folder, name, folder, Size(folder, []), folder, false));
    }
    return items;
  }

  /// <summary>Programas abertos que travam arquivos dos itens escolhidos (avisar antes de copiar).</summary>
  public static IReadOnlyList<string> OpenPrograms(IEnumerable<MigrationItem> items) =>
    items.SelectMany(i => i.RunningProcesses).Distinct().Where(p => Process.GetProcessesByName(p).Length > 0).ToList();

  public static async Task BackupAsync(IEnumerable<MigrationItem> items, string destination, IProgress<string>? progress = null, CancellationToken ct = default)
  {
    string root = Directory.CreateDirectory(System.IO.Path.Combine(destination, "migracao")).FullName;
    var manifest = new JsonArray();
    foreach (var item in items)
    {
      progress?.Report($"Copiando {item.Name}...");
      string stored = item.Id;
      string target = System.IO.Path.Combine(root, stored);
      if (item.Kind == MigrationKind.Wifi)
      {
        Directory.CreateDirectory(target);
        await RunAsync("netsh.exe", ["wlan", "export", "profile", "key=clear", $"folder={target}"], ct);
      }
      else
      {
        await RobocopyAsync(item.Path, target, item.ExcludeDirs, ct);
      }
      manifest.Add(new JsonObject
      {
        ["id"] = item.Id,
        ["kind"] = item.Kind.ToString(),
        ["name"] = item.Name,
        ["original"] = item.Kind == MigrationKind.Wifi ? "" : SaveScanner.TokenizePath(item.Path),
        ["stored"] = stored,
        ["exe"] = item.Shortcut is null ? null : SaveScanner.TokenizePath(item.Shortcut),
      });
    }
    await File.WriteAllTextAsync(System.IO.Path.Combine(root, "manifest.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
  }

  // --- auxiliares ---

  private static IEnumerable<(string Id, string Name, string Path)> UserFolders()
  {
    yield return ("desktop", "Área de Trabalho", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    yield return ("documentos", "Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    yield return ("downloads", "Downloads", KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B")));
    yield return ("imagens", "Imagens", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
    yield return ("videos", "Vídeos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
    yield return ("musicas", "Músicas", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
  }

  private static bool HasWlan()
  {
    try
    {
      using var p = Process.Start(new ProcessStartInfo("netsh.exe", "wlan show interfaces") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
      string output = p.StandardOutput.ReadToEnd();
      p.WaitForExit();
      return p.ExitCode == 0 && output.Contains(':');
    }
    catch (System.ComponentModel.Win32Exception)
    {
      return false;
    }
  }

  public static long Size(string folder, IReadOnlyCollection<string> excludeDirs)
  {
    long total = 0;
    var stack = new Stack<DirectoryInfo>([new DirectoryInfo(folder)]);
    while (stack.TryPop(out var dir))
    {
      try
      {
        foreach (var f in dir.EnumerateFiles()) total += f.Length;
        foreach (var d in dir.EnumerateDirectories())
        {
          if (!excludeDirs.Contains(d.Name, StringComparer.OrdinalIgnoreCase) && !d.Attributes.HasFlag(FileAttributes.ReparsePoint)) stack.Push(d);
        }
      }
      catch (Exception e) when (e is UnauthorizedAccessException or IOException)
      {
        // pasta protegida/sumiu: ignora
      }
    }
    return total;
  }

  private static async Task RobocopyAsync(string from, string to, IReadOnlyList<string> excludeDirs, CancellationToken ct)
  {
    var args = new List<string> { from, to, "/E", "/XJ", "/R:1", "/W:1", "/NFL", "/NDL", "/NJH", "/NJS", "/NP", "/MT:8" };
    if (excludeDirs.Count > 0)
    {
      args.Add("/XD");
      args.AddRange(excludeDirs);
    }
    int code = await RunAsync("robocopy.exe", args, ct);
    if (code >= 8) throw new IOException($"robocopy falhou ao copiar {from} (código {code}).");   // < 8 = sucesso no robocopy
  }

  private static async Task<int> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct)
  {
    var psi = new ProcessStartInfo(exe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!;
    var drain = p.StandardOutput.ReadToEndAsync(ct);
    var drainErr = p.StandardError.ReadToEndAsync(ct);
    await p.WaitForExitAsync(ct);
    await drain;
    await drainErr;
    return p.ExitCode;
  }

  private static string KnownFolder(Guid id)
  {
    SHGetKnownFolderPath(id, 0, IntPtr.Zero, out IntPtr ptr);
    try { return Marshal.PtrToStringUni(ptr) ?? ""; }
    finally { Marshal.FreeCoTaskMem(ptr); }
  }

  [DllImport("shell32.dll")]
  private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
