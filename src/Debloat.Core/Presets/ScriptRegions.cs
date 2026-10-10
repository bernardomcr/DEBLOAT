using System.Text.RegularExpressions;

namespace Debloat.Core.Presets;

/// <summary>Remove dos scripts os blocos "#region tweak:&lt;id&gt;" … "#endregion" dos ajustes desligados.</summary>
public static partial class ScriptRegions
{
  public static string Apply(string script, IReadOnlySet<string> enabled, IReadOnlySet<string>? namedRegions = null) =>
    RegionRegex().Replace(script, m =>
    {
      string id = m.Groups["id"].Value;
      bool keep = m.Groups["tweak"].Success ? enabled.Contains(id) : namedRegions?.Contains(id) == true;
      return keep ? m.Value : "";
    });

  /// <summary>IDs de ajuste usados nos blocos de um script (para o teste conferir com o catálogo).</summary>
  public static IEnumerable<string> TweakIds(string script) =>
    RegionRegex().Matches(script).Where(m => m.Groups["tweak"].Success).Select(m => m.Groups["id"].Value);

  [GeneratedRegex(@"^#region (?<tweak>tweak:)?(?<id>[\w-]+)[ \t]*\r?\n.*?^#endregion[ \t]*(\r?\n|\z)", RegexOptions.Multiline | RegexOptions.Singleline)]
  private static partial Regex RegionRegex();
}
