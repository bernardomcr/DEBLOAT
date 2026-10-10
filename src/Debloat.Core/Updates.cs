using System.Text.Json.Nodes;

namespace Debloat.Core;

/// <summary>Nova versão do DEBLOAT no GitHub (só avisa; quem baixa é o usuário).</summary>
public static class Updates
{
  public record Release(Version Version, string Url);

  public static async Task<Release?> CheckAsync(HttpClient http, Version current, CancellationToken ct = default)
  {
    try
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/bernardomcr/DEBLOAT/releases/latest");
      request.Headers.UserAgent.ParseAdd("DEBLOAT");
      using var response = await http.SendAsync(request, ct);
      if (!response.IsSuccessStatusCode) return null;
      var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
      return Parse((string?)json["tag_name"], (string?)json["html_url"], current);
    }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
    {
      return null;   // sem internet ou GitHub fora: não atrapalha nada
    }
  }

  /// <summary>"v1.2.0" mais nova que a versão atual → aviso.</summary>
  public static Release? Parse(string? tag, string? url, Version current)
  {
    if (tag is null || url is null || !Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
    var normalized = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
    return version > normalized ? new Release(version, url) : null;
  }
}
