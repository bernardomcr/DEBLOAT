using System.Text.Json;
using System.Text.RegularExpressions;

namespace Debloat.Core.Presets;

/// <summary>Como desfazer um ajuste no Windows instalado: argumentos do reg.exe ("delete ... /f").</summary>
public record UndoEntry(string Id, string Name, string Group, IReadOnlyList<string> Commands);

/// <summary>
/// "Desfazer" ajuste por ajuste, gerado dos próprios scripts: cada valor de registro que um bloco "#region tweak:&lt;id&gt;"
/// grava vira um "reg delete" (apagar o valor = o padrão do Windows volta). Ajustes que não são de registro
/// (apps removidos, opções do gerador) não entram — para eles há o ponto de restauração.
/// </summary>
public static partial class TweakUndo
{
  public static IReadOnlyList<UndoEntry> Build(IReadOnlySet<string> enabled)
  {
    var commands = new Dictionary<string, List<string>>();
    foreach (var (script, user) in new[] { (Resources.Script("SystemTweaks.ps1"), false), (Resources.Script("DefaultUserTweaks.ps1"), true) })
    {
      var variables = new Dictionary<string, string>();
      foreach (Match m in VariableRegex().Matches(script)) variables[m.Groups["n"].Value] = m.Groups["v"].Value;
      foreach (Match region in RegionRegex().Matches(script))
      {
        string id = region.Groups["id"].Value;
        if (!enabled.Contains(id)) continue;
        foreach (string line in region.Value.Split('\n'))
        {
          if (Undo(line, variables, user) is not { } command) continue;
          if (!commands.TryGetValue(id, out var list)) commands[id] = list = [];
          if (!list.Contains(command)) list.Add(command);
        }
      }
    }
    return TweakCatalog.All.Where(t => commands.ContainsKey(t.Id)).Select(t => new UndoEntry(t.Id, t.Name, t.Group, commands[t.Id])).ToList();
  }

  public static string ToJson(IReadOnlyList<UndoEntry> entries) =>
    JsonSerializer.Serialize(entries, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });

  /// <summary>Uma linha do script → o "reg delete" que desfaz, ou null se ela não grava valor de registro.</summary>
  private static string? Undo(string line, IReadOnlyDictionary<string, string> variables, bool userScript)
  {
    string? key = null, name = null;
    if (SetRegex().Match(line) is { Success: true } set)
    {
      key = set.Groups["k"].Success ? set.Groups["k"].Value : variables.GetValueOrDefault(set.Groups["kv"].Value);
      name = set.Groups["n"].Value;
      // Set-UserValue grava no perfil padrão (HKU\DefaultUser); no Windows instalado, é o HKCU de quem usa.
      if (set.Groups["fn"].Value == "Set-UserValue") key = key is null ? null : @"HKCU\" + key;
    }
    else if (RegAddRegex().Match(line) is { Success: true } add)
    {
      key = add.Groups["k"].Value;
      name = add.Groups["n"].Value;
    }
    if (key is null || name is null || key.Contains('$')) return null;
    if (key.StartsWith(@"HKU\DefaultUser\", StringComparison.OrdinalIgnoreCase)) key = @"HKCU\" + key[@"HKU\DefaultUser\".Length..];
    if (!userScript && !key.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return null;
    return $"delete \"{key}\" /v \"{name}\" /f";
  }

  [GeneratedRegex(@"^\$(?<n>\w+)\s*=\s*'(?<v>[^']+)'\s*$", RegexOptions.Multiline)]
  private static partial Regex VariableRegex();

  [GeneratedRegex(@"^\s*(?<fn>Set-Policy|Set-UserValue)\s+(?:'(?<k>[^']+)'|\$(?<kv>\w+))\s+'(?<n>[^']+)'")]
  private static partial Regex SetRegex();

  [GeneratedRegex(@"reg(?:\.exe)?\s+add\s+['""](?<k>[^'""]+)['""]\s+/v\s+['""]?(?<n>[^'""\s]+)", RegexOptions.IgnoreCase)]
  private static partial Regex RegAddRegex();

  [GeneratedRegex(@"^#region tweak:(?<id>[\w-]+)[ \t]*\r?\n.*?^#endregion", RegexOptions.Multiline | RegexOptions.Singleline)]
  private static partial Regex RegionRegex();
}
