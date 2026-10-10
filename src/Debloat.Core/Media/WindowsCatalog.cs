using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;

namespace Debloat.Core.Media;

public record CatalogSource(string Versao, string Url);

public record WindowsLanguage(string Code, string Name)
{
  public override string ToString() => Name;
}

/// <summary>Uma versão do Windows 11 (ex.: 25H2) com os .esd de todos os idiomas.</summary>
public record WindowsRelease(string Version, Version Build, IReadOnlyList<EsdFile> Files)
{
  public string Label => $"Windows 11 {Version} (build {Build})";

  public EsdFile? FileFor(string language, string architecture = "x64") => WindowsCatalog.Pick(Files, language, architecture);

  /// <summary>Idiomas oferecidos: só português e inglês (decisão do usuário), se existirem nesta versão.</summary>
  public static readonly IReadOnlyList<string> OfferedLanguages = ["pt-br", "en-us"];

  public IReadOnlyList<WindowsLanguage> Languages => Files
    .Where(f => f.Architecture.Equals("x64", StringComparison.OrdinalIgnoreCase) && f.Edition.Equals("Professional", StringComparison.OrdinalIgnoreCase))
    .Select(f => f.Language.ToLowerInvariant()).Distinct()
    .Where(code => OfferedLanguages.Contains(code))
    .Select(code => new WindowsLanguage(code, NativeName(code)))
    .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

  private static string NativeName(string code)
  {
    try
    {
      string name = System.Globalization.CultureInfo.GetCultureInfo(code).NativeName;
      return char.ToUpper(name[0]) + name[1..];
    }
    catch (System.Globalization.CultureNotFoundException)
    {
      return code;
    }
  }

  public override string ToString() => $"Windows 11 {Version}";
}

/// <summary>Um arquivo .esd do catálogo oficial (o mesmo que a Ferramenta de Criação de Mídia usa).</summary>
public record EsdFile(
  string FileName,
  string Language,
  string Edition,
  string Architecture,
  long Size,
  string? Sha1,
  string? Sha256,
  Uri Url)
{
  /// <summary>"26200.6584" do nome do arquivo, para escolher a versão mais nova.</summary>
  public Version Build
  {
    get
    {
      var parts = FileName.Split('.');
      return parts.Length > 2
        && int.TryParse(parts[0], CultureInfo.InvariantCulture, out int major)
        && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int minor)
          ? new Version(major, minor)
          : new Version(0, 0);
    }
  }
}

/// <summary>
/// Lê os catálogos products.xml da Microsoft. Não usa a página de download do site (que tem proteção
/// contra robôs): estes catálogos existem justamente para download automatizado pela MCT.
/// </summary>
public static class WindowsCatalog
{
  /// <summary>
  /// Lista de catálogos (versão → products.xml). Vem do catalogos.json do repositório no GitHub, para versões novas
  /// aparecerem sem atualizar o programa; a cópia embutida é a reserva.
  /// </summary>
  public const string SourcesUrl = "https://raw.githubusercontent.com/bernardomcr/DEBLOAT/main/catalogos.json";

  public static async Task<IReadOnlyList<CatalogSource>> SourcesAsync(HttpClient http, CancellationToken ct = default)
  {
    try
    {
      string json = await http.GetStringAsync(SourcesUrl, ct);
      var list = System.Text.Json.JsonSerializer.Deserialize<List<CatalogSource>>(json, SourceJson);
      if (list is { Count: > 0 }) return list;
    }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
    {
      // sem internet ou GitHub fora: usa a lista embutida
    }
    return System.Text.Json.JsonSerializer.Deserialize<List<CatalogSource>>(Resources.Data("catalogos.json"), SourceJson)!;
  }

  private static readonly System.Text.Json.JsonSerializerOptions SourceJson = new() { PropertyNameCaseInsensitive = true };

  private const char Bom = (char)0xFEFF;

  public static IReadOnlyList<EsdFile> Parse(string productsXml)
  {
    var doc = XDocument.Parse(productsXml.TrimStart(Bom));   // o products.xml do Download Center vem com BOM
    return doc.Descendants("File").Select(f => new EsdFile(
      FileName: (string?)f.Element("FileName") ?? "",
      Language: (string?)f.Element("LanguageCode") ?? "",
      Edition: (string?)f.Element("Edition") ?? "",
      Architecture: (string?)f.Element("Architecture") ?? "",
      Size: (long?)f.Element("Size") ?? 0,
      Sha1: NullIfEmpty((string?)f.Element("Sha1")),
      Sha256: NullIfEmpty((string?)f.Element("Sha256")),
      Url: new Uri((string?)f.Element("FilePath") ?? "about:blank")
    )).Where(f => f.FileName.EndsWith(".esd", StringComparison.OrdinalIgnoreCase)).ToList();
  }

  /// <summary>O .esd mais novo para idioma/arquitetura. A edição "Professional" fica no ESD de consumidor.</summary>
  public static EsdFile? Pick(IEnumerable<EsdFile> files, string language = "pt-br", string architecture = "x64", string edition = "Professional") =>
    files
      .Where(f => f.Language.Equals(language, StringComparison.OrdinalIgnoreCase)
        && f.Architecture.Equals(architecture, StringComparison.OrdinalIgnoreCase)
        && f.Edition.Equals(edition, StringComparison.OrdinalIgnoreCase))
      .OrderByDescending(f => f.Build)
      .FirstOrDefault();

  /// <summary>Todas as versões disponíveis (mais nova primeiro), cada uma com os seus .esd.</summary>
  public static async Task<IReadOnlyList<WindowsRelease>> LoadReleasesAsync(HttpClient http, CancellationToken ct = default)
  {
    var releases = new List<WindowsRelease>();
    foreach (var source in await SourcesAsync(http, ct))
    {
      try
      {
        byte[] data = await http.GetByteArrayAsync(source.Url, ct);
        var files = Parse(IsCab(data) ? ExtractCab(data) : System.Text.Encoding.UTF8.GetString(data));
        if (files.Count > 0) releases.Add(new WindowsRelease(source.Versao, files.Max(f => f.Build)!, files));
      }
      catch (Exception e) when (e is HttpRequestException or InvalidDataException or System.Xml.XmlException or TaskCanceledException)
      {
        // um catálogo fora do ar não derruba os outros
      }
    }
    return releases.OrderByDescending(r => r.Build).ToList();
  }

  /// <summary>O .esd mais novo para o idioma, entre todas as versões.</summary>
  public static async Task<EsdFile> FindLatestAsync(HttpClient http, string language = "pt-br", string architecture = "x64", CancellationToken ct = default) =>
    (await LoadReleasesAsync(http, ct)).Select(r => r.FileFor(language, architecture)).FirstOrDefault(f => f is not null)
      ?? throw new InvalidDataException("Nenhum catálogo da Microsoft respondeu com esse idioma.");

  private static bool IsCab(byte[] data) => data.Length > 4 && data[0] == 'M' && data[1] == 'S' && data[2] == 'C' && data[3] == 'F';

  /// <summary>O products.cab da MCT tem um único products.xml dentro; o expand.exe do Windows abre.</summary>
  private static string ExtractCab(byte[] cab)
  {
    string dir = Directory.CreateTempSubdirectory("debloat-cab-").FullName;
    try
    {
      string cabPath = Path.Combine(dir, "catalog.cab");
      string outDir = Directory.CreateDirectory(Path.Combine(dir, "out")).FullName;
      File.WriteAllBytes(cabPath, cab);
      using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "expand.exe"), $"\"{cabPath}\" -F:* \"{outDir}\"")
      {
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardOutput = true,
      })!;
      p.StandardOutput.ReadToEnd();
      p.WaitForExit();
      string xml = Directory.EnumerateFiles(outDir).FirstOrDefault()
        ?? throw new InvalidDataException("products.cab vazio.");
      return File.ReadAllText(xml);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
