using System.Text;
using Debloat.Core.Presets;

namespace Debloat.Core.Tests;

public class TweakTests
{
  private static string Generate(DebloatOptions options) => Encoding.UTF8.GetString(new UnattendBuilder().BuildBytes(options));

  [Fact]
  public void Ids_sao_unicos_e_todo_bloco_de_script_existe_no_catalogo()
  {
    Assert.Equal(TweakCatalog.All.Count, TweakCatalog.All.Select(t => t.Id).Distinct().Count());
    var ids = TweakCatalog.All.Select(t => t.Id).ToHashSet();
    foreach (string script in new[] { "SystemTweaks.ps1", "DefaultUserTweaks.ps1", "FirstLogon.ps1" })
    {
      foreach (string id in ScriptRegions.TweakIds(Resources.Script(script))) Assert.Contains(id, ids);
    }
  }

  [Fact]
  public void Agressivos_ficam_fora_do_preset()
  {
    var preset = TweakCatalog.DefaultsFor(HardwareProfile.Desktop);
    foreach (var t in TweakCatalog.All.Where(t => t.Aggressive)) Assert.DoesNotContain(t.Id, preset);
  }

  [Fact]
  public void Game_Bar_desligada_no_preset_e_volta_se_desmarcar()
  {
    Assert.Contains("Microsoft.XboxGamingOverlay", Generate(new DebloatOptions()));
    var semAjuste = TweakCatalog.DefaultsFor(HardwareProfile.Desktop).Where(t => t != "sem-game-bar").ToHashSet();
    Assert.DoesNotContain("Microsoft.XboxGamingOverlay", Generate(new DebloatOptions { Tweaks = semAjuste }));
  }

  [Fact]
  public void Hibernacao_e_Localizar_dependem_de_ser_notebook()
  {
    var desktop = TweakCatalog.DefaultsFor(HardwareProfile.Desktop);
    var notebook = TweakCatalog.DefaultsFor(new HardwareProfile(true, false, false));
    Assert.Contains("sem-hibernacao", desktop);
    Assert.DoesNotContain("sem-hibernacao", notebook);
    Assert.DoesNotContain("localizar-dispositivo", notebook);
  }

  [Fact]
  public void Opcoes_do_gerador_seguem_a_lista()
  {
    var tudoDesligado = new HashSet<string>();
    string xml = Generate(new DebloatOptions { Tweaks = tudoDesligado });
    Assert.DoesNotContain("DisableClickToDo", xml);
    Assert.DoesNotContain("TaskbarGlomLevel", xml);
    Assert.DoesNotContain("photoviewer.dll", xml);

    var agressivo = TweakCatalog.All.Select(t => t.Id).ToHashSet();
    string tudo = Generate(new DebloatOptions { Tweaks = agressivo });
    Assert.Contains("EnableLUA", tudo);                       // UAC desligado
  }
}

public class EveryTweakDoesSomethingTests
{
  private static readonly UnattendBuilder Builder = new();
  private static readonly string Baseline = Gen(new HashSet<string>());

  private static string Gen(HashSet<string> tweaks) =>
    Encoding.UTF8.GetString(Builder.BuildBytes(new DebloatOptions { Tweaks = tweaks, RemovedApps = new HashSet<string>(), SelectedApps = ["chrome"] }));

  public static IEnumerable<object[]> Ids => TweakCatalog.All.Select(t => new object[] { t.Id });

  [Theory]
  [MemberData(nameof(Ids))]
  public void Ligar_o_ajuste_muda_o_xml(string id) => Assert.NotEqual(Baseline, Gen([id]));

  [Fact]
  public void Todo_app_removivel_existe_no_gerador()
  {
    foreach (var (id, _, _) in TweakCatalog.Bloatware)
    {
      string xml = Encoding.UTF8.GetString(Builder.BuildBytes(new DebloatOptions { RemovedApps = new HashSet<string> { id }, Tweaks = new HashSet<string>(), SelectedApps = ["vlc"] }));
      Assert.NotEqual(Baseline, xml);
    }
  }
}
