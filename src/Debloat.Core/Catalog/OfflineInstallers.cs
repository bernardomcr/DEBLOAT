using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Debloat.Core.Catalog;

public enum OfflineState { Waiting, Downloading, Ready, Online }

public record OfflineProgress(string Id, OfflineState State, long Bytes = 0, string? Note = null);

/// <summary>Um instalador que vai dentro da mídia; o FirstLogon.ps1 lê a lista em DEBLOAT\apps\offline.json.</summary>
public record OfflineInstaller(string Id, string File, string Kind, string? Args, IReadOnlyList<int> SuccessCodes, string? Signer = null);

/// <summary>
/// Baixa os instaladores dos apps escolhidos enquanto a mídia é montada, para o primeiro login só instalar.
/// winget: "winget download" (hash conferido pelo próprio winget) e as opções silenciosas do manifesto.
/// O que não dá para baixar antes (Loja, recursos do Windows, pacotes portáteis) continua pela internet, como antes.
/// </summary>
public static partial class OfflineInstallers
{
  public const string MediaFolder = @"DEBLOAT\apps";

  private static readonly TimeSpan CacheLife = TimeSpan.FromDays(3);

  private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

  public static bool CanDownload(AppEntry app) => app.Source is "winget" or "url" or "github";

  public static async Task<IReadOnlyList<OfflineInstaller>> PrepareAsync(HttpClient http, IReadOnlyList<AppEntry> apps, string cacheDir,
    string mediaDir, IProgress<OfflineProgress>? progress = null, CancellationToken ct = default)
  {
    string target = Directory.CreateDirectory(Path.Combine(mediaDir, MediaFolder)).FullName;
    var ready = new ConcurrentDictionary<string, OfflineInstaller>();
    using var gate = new SemaphoreSlim(4);
    await Task.WhenAll(apps.Select(async app =>
    {
      if (!CanDownload(app))
      {
        progress?.Report(new(app.Id, OfflineState.Online));
        return;
      }
      await gate.WaitAsync(ct);
      try
      {
        progress?.Report(new(app.Id, OfflineState.Downloading));
        string dir = Path.Combine(cacheDir, app.Id);
        var item = Cached(dir) ?? await DownloadAsync(http, app, dir, ct);
        if (item is null)
        {
          progress?.Report(new(app.Id, OfflineState.Online, Note: "sem instalador silencioso"));
          return;
        }
        string dest = Directory.CreateDirectory(Path.Combine(target, app.Id)).FullName;
        File.Copy(Path.Combine(dir, item.File), Path.Combine(dest, item.File), overwrite: true);
        ready[app.Id] = item with { File = $@"{app.Id}\{item.File}" };
        progress?.Report(new(app.Id, OfflineState.Ready, new FileInfo(Path.Combine(dest, item.File)).Length));
      }
      catch (Exception e) when (e is not OperationCanceledException)
      {
        progress?.Report(new(app.Id, OfflineState.Online, Note: e.Message));
      }
      finally
      {
        gate.Release();
      }
    }));
    var list = apps.Where(a => ready.ContainsKey(a.Id)).Select(a => ready[a.Id]).ToList();
    await File.WriteAllTextAsync(Path.Combine(target, "offline.json"), JsonSerializer.Serialize(list, Json), ct);
    return list;
  }

  /// <summary>Reaproveita o que foi baixado há pouco (montar ISO e depois pendrive não baixa tudo de novo).</summary>
  private static OfflineInstaller? Cached(string dir)
  {
    var info = new FileInfo(Path.Combine(dir, "debloat.json"));
    if (!info.Exists || DateTime.UtcNow - info.LastWriteTimeUtc > CacheLife) return null;
    var item = JsonSerializer.Deserialize<OfflineInstaller>(File.ReadAllText(info.FullName), Json);
    return item is not null && File.Exists(Path.Combine(dir, item.File)) ? item : null;
  }

  private static async Task<OfflineInstaller?> DownloadAsync(HttpClient http, AppEntry app, string dir, CancellationToken ct)
  {
    if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    Directory.CreateDirectory(dir);
    OfflineInstaller? item = app.Source switch
    {
      "winget" => await FromWingetAsync(app, dir, ct),
      "url" => await FromUrlAsync(http, app, new Uri(app.Package), dir, ct),
      "github" => await FromGitHubAsync(http, app, dir, ct),
      _ => null,
    };
    if (item is null && app.FallbackUrl is not null)
    {
      // Plano B do catálogo (manifesto do winget desatualizado): o primeiro login confere a assinatura antes de rodar.
      item = await FromUrlAsync(http, app, new Uri(app.FallbackUrl), dir, ct, $"{app.Id}-setup.exe") is { } f ? f with { Signer = app.Signer } : null;
    }
    if (item is not null) await File.WriteAllTextAsync(Path.Combine(dir, "debloat.json"), JsonSerializer.Serialize(item, Json), ct);
    return item;
  }

  private static async Task<OfflineInstaller?> FromWingetAsync(AppEntry app, string dir, CancellationToken ct)
  {
    var args = new List<string> { "download", "--exact", "--id", app.Package, "--source", "winget", "--download-directory", dir,
      "--skip-dependencies", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" };
    if (app.Architecture is not null) args.AddRange(["--architecture", app.Architecture]);
    var psi = new ProcessStartInfo(WingetPath())
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = Encoding.UTF8,
    };
    foreach (string a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi) ?? throw new InvalidOperationException("winget não abriu.");
    var stdout = p.StandardOutput.ReadToEndAsync(ct);
    var stderr = p.StandardError.ReadToEndAsync(ct);
    await p.WaitForExitAsync(ct);
    await stdout;
    await stderr;
    if (p.ExitCode != 0) return null;

    string? manifest = Directory.EnumerateFiles(dir, "*.yaml").FirstOrDefault();
    string? installer = Directory.EnumerateFiles(dir).FirstOrDefault(f => !f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));
    if (manifest is null || installer is null) return null;
    var plan = ParseManifest(File.ReadAllText(manifest));
    if (plan is null) return null;
    // Nome curto: os do winget têm espaços, parênteses e passam de 100 caracteres.
    string file = app.Id + Path.GetExtension(installer);
    File.Move(installer, Path.Combine(dir, file));
    File.Delete(manifest);
    return new OfflineInstaller(app.Id, file, plan.Value.Kind, plan.Value.Args, plan.Value.SuccessCodes);
  }

  private static async Task<OfflineInstaller?> FromGitHubAsync(HttpClient http, AppEntry app, string dir, CancellationToken ct)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{app.Package}/releases/latest");
    request.Headers.UserAgent.ParseAdd("DEBLOAT");
    using var response = await http.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();
    var release = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
    var regex = new Regex(app.Asset ?? ".", RegexOptions.IgnoreCase);
    var asset = release["assets"]!.AsArray().FirstOrDefault(a => regex.IsMatch((string)a!["name"]!));
    if (asset is null) return null;
    return await FromUrlAsync(http, app, new Uri((string)asset["browser_download_url"]!), dir, ct);
  }

  private static async Task<OfflineInstaller?> FromUrlAsync(HttpClient http, AppEntry app, Uri url, string dir, CancellationToken ct, string? name = null)
  {
    string ext = Path.GetExtension(name ?? url.Segments[^1]).ToLowerInvariant();
    string file = name ?? app.Id + ext;
    using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
    {
      response.EnsureSuccessStatusCode();
      await using var output = File.Create(Path.Combine(dir, file));
      await response.Content.CopyToAsync(output, ct);
    }
    string kind = ext switch
    {
      ".msi" => "msi",
      ".msix" or ".msixbundle" or ".appx" or ".appxbundle" => "msix",
      _ => "exe",
    };
    return new OfflineInstaller(app.Id, file, kind, app.Args, []);
  }

  /// <summary>
  /// Como o winget instalaria: tipo do instalador e opções silenciosas do manifesto, ou as padrão de cada tipo.
  /// Null quando não dá para instalar sem o winget (zip, portátil).
  /// </summary>
  public static (string Kind, string? Args, IReadOnlyList<int> SuccessCodes)? ParseManifest(string yaml)
  {
    string? type = Field(yaml, "InstallerType")?.ToLowerInvariant();
    string? silent = Field(yaml, "Silent");
    string? custom = Field(yaml, "Custom");
    var codes = SuccessCodesRegex().Match(yaml) is { Success: true } m
      ? m.Groups[1].Captures.Select(c => int.Parse(c.Value)).ToList() : [];
    (string Kind, string? Args)? plan = type switch
    {
      "msi" or "wix" => ("msi", silent ?? "/qn /norestart"),
      "msix" or "appx" => ("msix", null),
      "nullsoft" => ("exe", silent ?? "/S"),
      "inno" => ("exe", silent ?? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"),
      "burn" => ("exe", silent ?? "/quiet /norestart"),
      "exe" => ("exe", silent),   // sem opção o winget roda sem argumentos (ex.: Discord já é silencioso)
      _ => null,
    };
    if (plan is null) return null;
    string? args = string.Join(' ', new[] { plan.Value.Args, custom }.Where(s => !string.IsNullOrWhiteSpace(s)));
    return (plan.Value.Kind, args.Length == 0 ? null : args, codes);
  }

  private static string? Field(string yaml, string key)
  {
    var m = Regex.Match(yaml, $@"^[ \t-]*{key}:[ \t]*([^\r\n]+?)[ \t]*\r?$", RegexOptions.Multiline);
    if (!m.Success) return null;
    string value = m.Groups[1].Value;
    if (value.Length >= 2 && (value[0] == '\'' || value[0] == '"') && value[^1] == value[0]) value = value[1..^1];
    return value;
  }

  private static string WingetPath()
  {
    string alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
    return File.Exists(alias) ? alias : "winget.exe";
  }

  [GeneratedRegex(@"^[ \t]*InstallerSuccessCodes:[ \t]*\r?\n(?:[ \t]*-[ \t]*(-?\d+)[ \t]*\r?\n?)+", RegexOptions.Multiline)]
  private static partial Regex SuccessCodesRegex();
}
