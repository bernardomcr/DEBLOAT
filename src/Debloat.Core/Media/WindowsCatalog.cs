using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;

namespace Debloat.Core.Media;

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
  /// <summary>Catálogos conhecidos, do mais novo para o mais antigo. Atualizar quando sair uma versão nova.</summary>
  public static readonly IReadOnlyList<Uri> Sources =
  [
    // 25H2 — Microsoft Download Center "products_25H2" (id 108396)
    new("https://download.microsoft.com/download/eb1cc454-1c9a-4c94-adf8-b30c7f3d03d1/products.xml"),
    // Link embutido na MCT (SetupMgr.dll); hoje aponta para a 24H2. Fica como reserva.
    new("https://go.microsoft.com/fwlink/?LinkId=2156292"),
  ];

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

  /// <summary>Baixa todos os catálogos e devolve o .esd mais novo entre eles.</summary>
  public static async Task<EsdFile> FindLatestAsync(HttpClient http, string language = "pt-br", string architecture = "x64", CancellationToken ct = default)
  {
    var all = new List<EsdFile>();
    var errors = new List<string>();
    foreach (var source in Sources)
    {
      try
      {
        byte[] data = await http.GetByteArrayAsync(source, ct);
        all.AddRange(Parse(IsCab(data) ? ExtractCab(data) : System.Text.Encoding.UTF8.GetString(data)));
      }
      catch (Exception e) when (e is HttpRequestException or InvalidDataException or System.Xml.XmlException)
      {
        errors.Add($"{source.Host}: {e.Message}");
      }
    }
    return Pick(all, language, architecture)
      ?? throw new InvalidDataException("Nenhum catálogo da Microsoft respondeu com esse idioma. " + string.Join("; ", errors));
  }

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
