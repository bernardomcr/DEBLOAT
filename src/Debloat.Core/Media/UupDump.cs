using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Debloat.Core.Media;

/// <summary>Uma build qualquer do Windows 11 listada pelo UUP dump.</summary>
public record UupBuild(string Uuid, Version Build, string Title, string Version, DateTimeOffset Created)
{
  public bool Insider => Version == "Insider";
}

/// <summary>
/// Todas as builds do Windows 11 via UUP dump (uupdump.net). Os arquivos vêm dos servidores da Microsoft
/// (SHA-1 conferido); o conversor do UUP dump (abbodi1406, código aberto) monta a pasta de instalação.
/// Só é usado quando a build escolhida não é a que a Microsoft oferece pronta (WindowsCatalog).
/// </summary>
public static partial class UupDump
{
  private const string Api = "https://api.uupdump.net";

  public static async Task<IReadOnlyList<UupBuild>> ListBuildsAsync(HttpClient http, CancellationToken ct = default)
  {
    var json = JsonNode.Parse(await GetAsync(http, $"{Api}/listid.php?search=Windows%2011&sortByDate=1", ct))!;
    var builds = new List<UupBuild>();
    foreach (var node in json["response"]!["builds"]!.AsObject().Select(p => p.Value!))
    {
      if ((string?)node["arch"] != "amd64") continue;
      string title = (string)node["title"]!;
      if (!System.Version.TryParse((string?)node["build"], out var build)) continue;
      // Só Windows 11 de verdade: 22000+ (Windows 10 é 1904x) e sem a 25398 (Windows Server 23H2).
      if (build.Major < 22000 || build.Major == 25398 || !title.StartsWith("Windows 11", StringComparison.OrdinalIgnoreCase)) continue;
      var version = VersionRegex().Match(title);
      string label = title.Contains("Insider", StringComparison.OrdinalIgnoreCase) ? "Insider"
        : version.Success ? version.Groups[1].Value.ToUpperInvariant() : "";
      if (label.Length == 0) continue;   // atualizações soltas, Server etc.
      builds.Add(new UupBuild((string)node["uuid"]!, build, title, label,
        DateTimeOffset.FromUnixTimeSeconds((long?)node["created"] ?? 0)));
    }
    // A mesma build aparece mais de uma vez (atualização cumulativa, feature update): fica a mais recente.
    return builds.GroupBy(b => (b.Version, b.Build)).Select(g => g.OrderByDescending(b => b.Created).First())
      .OrderByDescending(b => b.Build).ToList();
  }

  public record UupFile(string Name, Uri Url, string? Sha1, long Size);

  /// <summary>Arquivos da edição Pro no idioma + os apps básicos (Loja, Calculadora...), todos da Microsoft.</summary>
  public static async Task<IReadOnlyList<UupFile>> FilesAsync(HttpClient http, string uuid, string language, CancellationToken ct = default)
  {
    var files = new List<UupFile>();
    foreach (string query in new[] { $"lang={language}&edition=professional", "lang=neutral&edition=app" })
    {
      JsonNode json;
      try
      {
        json = JsonNode.Parse(await GetAsync(http, $"{Api}/get.php?id={uuid}&{query}", ct))!;
      }
      catch (HttpRequestException) when (query.Contains("edition=app"))
      {
        continue;   // builds antigas não têm apps separados
      }
      foreach (var (name, f) in json["response"]!["files"]!.AsObject())
      {
        if (files.Any(x => x.Name == name)) continue;
        files.Add(new UupFile(name, new Uri((string)f!["url"]!), (string?)f["sha1"], long.Parse((string)f["size"]!)));
      }
    }
    return files;
  }

  /// <summary>Baixa tudo, roda o conversor e devolve a pasta de instalação montada (com install.wim).</summary>
  public static async Task<string> BuildFolderAsync(HttpClient http, UupBuild build, string language, string workDir,
    IProgress<MediaStep>? progress = null, CancellationToken ct = default)
  {
    Directory.CreateDirectory(workDir);
    // Já convertida antes (ex.: falhou depois, na montagem da mídia): usa direto, sem baixar e converter de novo.
    if (Converted(workDir) is { } ready) return ready;
    progress?.Report(new("Baixando o conversor do UUP dump", 0.01));
    string converter = await GetConverterAsync(http, build.Uuid, language, workDir, ct);
    string uups = Directory.CreateDirectory(Path.Combine(converter, "UUPs")).FullName;   // onde o convert-UUP.cmd procura

    progress?.Report(new("Buscando a lista de arquivos da build", 0.02));
    var files = await FilesAsync(http, build.Uuid, language, ct);
    long total = files.Sum(f => f.Size), done = 0;
    var downloader = new SegmentedDownloader(http, connections: 4);
    using var gate = new SemaphoreSlim(4);
    await Task.WhenAll(files.Select(async f =>
    {
      await gate.WaitAsync(ct);
      try
      {
        await downloader.DownloadAsync(f.Url, Path.Combine(uups, f.Name), f.Size, null, f.Sha1, null, ct);
        long now = Interlocked.Add(ref done, f.Size);
        progress?.Report(new($"Baixando da Microsoft: {now / 1e9:F1} de {total / 1e9:F1} GB", 0.03 + 0.5 * now / total));
      }
      finally
      {
        gate.Release();
      }
    }));

    progress?.Report(new("Montando o Windows a partir das atualizações (pode levar 15–30 min)", 0.55));
    await File.WriteAllTextAsync(Path.Combine(converter, "ConvertConfig.ini"), ConvertConfig, ct);
    var psi = new ProcessStartInfo("cmd.exe", "/c convert-UUP.cmd")
    {
      WorkingDirectory = converter,
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardInput = true,     // os "pause" do script terminam na hora
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    using var p = Process.Start(psi)!;
    p.StandardInput.Close();
    var log = File.CreateText(Path.Combine(workDir, "conversor.log"));
    var pump = Task.Run(async () =>
    {
      string? line;
      while ((line = await p.StandardOutput.ReadLineAsync(ct)) is not null) await log.WriteLineAsync(line);
    }, ct);
    await p.WaitForExitAsync(ct);
    await pump;
    await log.DisposeAsync();

    return Converted(workDir) ?? throw new InvalidOperationException($"O conversor não gerou a instalação. Veja {Path.Combine(workDir, "conversor.log")}.");
  }

  /// <summary>
  /// Pasta de instalação que o conversor gerou. Com "sem ISO" ela não se chama ISOFOLDER, e sim o nome da build
  /// (ex.: 26100.1.240331-1435.GE_RELEASE_CLIENTPRO_OEMRET_X64FRE_PT-BR) — por isso a busca é pelo conteúdo.
  /// </summary>
  private static string? Converted(string workDir)
  {
    string converter = Path.Combine(workDir, "conversor");
    if (!Directory.Exists(converter)) return null;
    return Directory.EnumerateDirectories(converter).FirstOrDefault(d =>
      File.Exists(Path.Combine(d, "setup.exe")) && File.Exists(Path.Combine(d, "sources", "boot.wim"))
      && (File.Exists(Path.Combine(d, "sources", "install.wim")) || File.Exists(Path.Combine(d, "sources", "install.esd"))));
  }

  /// <summary>Conversor do pacote oficial do UUP dump: URLs e SHA-256 lidos do próprio pacote (não ficam fixos aqui).</summary>
  private static async Task<string> GetConverterAsync(HttpClient http, string uuid, string language, string workDir, CancellationToken ct)
  {
    using var response = await SendAsync(http, () => new HttpRequestMessage(HttpMethod.Post, $"https://uupdump.net/get.php?id={uuid}&pack={language}&edition=professional")
    {
      Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["autodl"] = "2", ["updates"] = "1", ["cleanup"] = "1" }),
    }, ct);
    using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(ct));
    string list = new StreamReader(zip.GetEntry("files/converter_windows")!.Open()).ReadToEnd();

    string tools = Directory.CreateDirectory(Path.Combine(workDir, "tools")).FullName;
    foreach (Match m in ConverterEntryRegex().Matches(list))
    {
      string url = m.Groups["url"].Value, name = m.Groups["out"].Value, sha = m.Groups["sha"].Value;
      string path = Path.Combine(tools, name);
      byte[] data = await http.GetByteArrayAsync(url, ct);
      if (!Convert.ToHexString(SHA256.HashData(data)).Equals(sha, StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidDataException($"{name} do UUP dump não bate com o SHA-256 publicado.");
      }
      await File.WriteAllBytesAsync(path, data, ct);
    }
    string dest = Path.Combine(workDir, "conversor");
    if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    using var unzip = Process.Start(new ProcessStartInfo(Path.Combine(tools, "7zr.exe"), $"x -y \"-o{dest}\" \"{Path.Combine(tools, "uup-converter-wimlib.7z")}\"")
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
    await unzip.StandardOutput.ReadToEndAsync(ct);
    await unzip.WaitForExitAsync(ct);
    if (unzip.ExitCode != 0) throw new InvalidOperationException("Não deu para extrair o conversor do UUP dump.");
    return dest;
  }

  /// <summary>Sem perguntas, com as atualizações integradas, só a pasta (a ISO e o pendrive são nossos).</summary>
  private const string ConvertConfig = """
    [convert-UUP]
    AutoStart    =1
    AddUpdates   =1
    Cleanup      =1
    ResetBase    =0
    NetFx3       =0
    StartVirtual =0
    wim2esd      =0
    wim2swm      =0
    SkipISO      =1
    SkipWinRE    =0
    LCUwinre     =0
    UpdtBootFiles=0
    ForceDism    =0
    RefESD       =0
    SkipEdge     =0
    AutoExit     =1

    [Store_Apps]
    SkipApps     =0
    AppsLevel    =0
    StubAppsFull =0
    CustomList   =0
    """;

  private static async Task<string> GetAsync(HttpClient http, string url, CancellationToken ct)
  {
    using var response = await SendAsync(http, () =>
    {
      var request = new HttpRequestMessage(HttpMethod.Get, url);
      request.Headers.UserAgent.ParseAdd("DEBLOAT");
      return request;
    }, ct);
    return await response.Content.ReadAsStringAsync(ct);
  }

  /// <summary>
  /// O UUP dump limita pedidos seguidos (429 Too Many Requests — no teste, a lista de arquivos logo depois do
  /// conversor). Espera o que ele pedir (Retry-After) ou 5, 10, 15... s e tenta de novo, até 6 vezes.
  /// </summary>
  private static async Task<HttpResponseMessage> SendAsync(HttpClient http, Func<HttpRequestMessage> request, CancellationToken ct)
  {
    for (int attempt = 1; ; attempt++)
    {
      using var message = request();
      var response = await http.SendAsync(message, ct);
      if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests || attempt == 6)
      {
        response.EnsureSuccessStatusCode();
        return response;
      }
      var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * attempt);
      response.Dispose();
      await Task.Delay(wait < TimeSpan.FromMinutes(2) ? wait : TimeSpan.FromMinutes(2), ct);
    }
  }

  [GeneratedRegex(@"version\s+(\d\d[Hh]\d)")]
  private static partial Regex VersionRegex();

  [GeneratedRegex(@"^(?<url>https://\S+)\s*\r?\n\s*out=(?<out>\S+)\s*\r?\n\s*checksum=sha-256=(?<sha>[0-9a-fA-F]{64})", RegexOptions.Multiline)]
  private static partial Regex ConverterEntryRegex();
}
