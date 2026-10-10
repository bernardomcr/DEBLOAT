using System.Text.Json;
using System.Text.Json.Serialization;

namespace Debloat.Core.Catalog;

public record AppCategory(string Id, string Name);

/// <summary>Programa fora do catálogo, pelo ID do winget (detectado no PC atual).</summary>
public record ExtraApp(string WingetId, string Name)
{
  public AppEntry ToEntry() => new("x-" + WingetId, Name, "extras", "winget", WingetId, false);
}

public record AppEntry(
  string Id,
  string Name,
  string Category,
  string Source,
  string Package,
  bool Default,
  string? Description = null,
  string? Architecture = null,
  string? Args = null,
  string? Asset = null,
  IReadOnlyList<string>? Requires = null,
  string? FallbackUrl = null,     // plano B: link oficial do fabricante, se o manifesto do winget estiver desatualizado
  string? Signer = null,          // o plano B só instala se a assinatura digital for deste fabricante
  IReadOnlyList<int>? SuccessCodes = null,  // códigos de saída que também são sucesso (UniGetUI sai com 1 instalando)
  string? Gpu = null              // "nvidia"/"amd": vem marcado quando o PC tem essa placa de vídeo
);

/// <summary>Catálogo de apps e pré-requisitos (Data\apps.json).</summary>
public sealed class AppCatalog
{
  public static readonly IReadOnlySet<string> KnownSources = new HashSet<string> { "winget", "msstore", "url", "github", "feature", "link" };

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };

  public required IReadOnlyList<AppCategory> Categories { get; init; }

  public required IReadOnlyList<AppEntry> Apps { get; init; }

  public static AppCatalog Load() =>
    JsonSerializer.Deserialize<AppCatalog>(Resources.Data("apps.json"), JsonOptions)
      ?? throw new InvalidDataException("apps.json vazio.");

  public IEnumerable<AppEntry> Defaults => Apps.Where(a => a.Default);

  /// <summary>Expande dependências (ex.: Everything Toolbar puxa o Everything) mantendo a ordem do catálogo.</summary>
  /// <summary>Apps escolhidos (com dependências) + os programas extras do PC atual, sem repetir pacote.</summary>
  public IReadOnlyList<AppEntry> ResolveWithExtras(IEnumerable<string> selectedIds, IEnumerable<ExtraApp> extras)
  {
    var apps = Resolve(selectedIds).ToList();
    apps.AddRange(extras.Where(x => !apps.Any(a => a.Package.Equals(x.WingetId, StringComparison.OrdinalIgnoreCase))).Select(x => x.ToEntry()));
    return apps;
  }

  public IReadOnlyList<AppEntry> Resolve(IEnumerable<string> selectedIds)
  {
    var wanted = new HashSet<string>(selectedIds);
    var queue = new Queue<string>(wanted);
    while (queue.TryDequeue(out var id))
    {
      var app = Apps.FirstOrDefault(a => a.Id == id) ?? throw new ArgumentException($"App '{id}' não existe no catálogo.");
      foreach (var dep in app.Requires ?? [])
      {
        if (wanted.Add(dep)) queue.Enqueue(dep);
      }
    }
    return Apps.Where(a => wanted.Contains(a.Id)).ToList();
  }

  /// <summary>JSON enxuto que o FirstLogon.ps1 lê.</summary>
  public static string ToScriptJson(IEnumerable<AppEntry> apps) => JsonSerializer.Serialize(
    apps.Select(a => new { a.Id, a.Name, a.Category, a.Source, a.Package, a.Architecture, a.Args, a.Asset, a.FallbackUrl, a.Signer, a.Requires }),
    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true });
}
