using System.Text;
using System.Xml;
using Debloat.Core.Presets;

namespace Debloat.Core.Tests;

public class UnattendBuilderTests
{
  private static string Generate(DebloatOptions options) =>
    Encoding.UTF8.GetString(new UnattendBuilder().BuildBytes(options));

  [Fact]
  public void Preset_padrao_gera_xml_valido()
  {
    var xml = new XmlDocument();
    xml.LoadXml(Generate(new DebloatOptions()));
    Assert.Equal("unattend", xml.DocumentElement!.LocalName);
  }

  [Fact]
  public void Usa_chave_generica_do_Pro_e_conta_sem_senha()
  {
    string xml = Generate(new DebloatOptions());
    Assert.Contains("VK7JG-NPHTM-C97JM-9MPGT-3V66T", xml);
    Assert.Contains("<Name>Usuario</Name>", xml);
    Assert.Contains("pt-BR", xml);
    Assert.Contains("E. South America Standard Time", xml);
  }

  [Fact]
  public void Mantem_bloco_de_notas_media_player_e_fala()
  {
    string xml = Generate(new DebloatOptions());
    Assert.Contains("Microsoft.BingNews", xml);              // removido
    Assert.DoesNotContain("'Microsoft.WindowsNotepad'", xml);
    Assert.DoesNotContain("'Microsoft.ZuneMusic'", xml);
    Assert.DoesNotContain("'Language.Speech'", xml);
  }

  [Fact]
  public void Hello_e_caneta_dependem_do_hardware()
  {
    string desktop = Generate(new DebloatOptions { Hardware = HardwareProfile.Desktop });
    Assert.Contains("Hello.Face", desktop);
    Assert.Contains("Language.Handwriting", desktop);

    string notebook = Generate(new DebloatOptions { Hardware = new HardwareProfile(true, true, true) });
    Assert.DoesNotContain("Hello.Face", notebook);
    Assert.DoesNotContain("Language.Handwriting", notebook);
  }

  [Fact]
  public void Scripts_do_debloat_entram_com_apps_e_dns()
  {
    string xml = Generate(new DebloatOptions { Dns = DnsChoice.AdGuard, SelectedApps = ["steam", "everything-toolbar"] });
    Assert.Contains("DisableClickToDo", xml);                 // SystemTweaks
    Assert.Contains("HistoricalCaptureEnabled", xml);         // DefaultUserTweaks
    Assert.Contains("$dnsChoice = 'adguard'", xml);
    Assert.Contains("Valve.Steam", xml);
    Assert.Contains("voidtools.Everything", xml);             // dependência do Toolbar
    Assert.DoesNotContain("@@APPS@@", xml);
    Assert.Contains("photoviewer.dll", xml);
  }

  [Fact]
  public void Politicas_rejeitadas_na_revisao_nao_entram()
  {
    string xml = Generate(new DebloatOptions());
    Assert.DoesNotContain("LetAppsRunInBackground", xml);     // apps em segundo plano: liberado
    Assert.DoesNotContain("AutoDownload", xml);               // atualização da Loja: liberada
    Assert.DoesNotContain("DisableEngine", xml);              // mecanismo de compatibilidade: ligado
    Assert.DoesNotContain("SbEnable", xml);                   // SwitchBack: ligado
    Assert.DoesNotContain("NoRecentDocsHistory", xml);        // documentos recentes: mantidos
    Assert.DoesNotContain("e9a42b02-d5df-448d-aa00-03f14749eb61", xml); // sem "Desempenho Máximo"
  }
}

public class WipeDiskTests
{
  [Fact]
  public void Padrao_pergunta_o_disco_e_o_modo_teste_apaga_o_disco_0()
  {
    string normal = System.Text.Encoding.UTF8.GetString(new UnattendBuilder().BuildBytes(new DebloatOptions()));
    Assert.DoesNotContain("diskpart", normal, StringComparison.OrdinalIgnoreCase);

    string wipe = System.Text.Encoding.UTF8.GetString(new UnattendBuilder().BuildBytes(new DebloatOptions { WipeDisk0 = true }));
    Assert.Contains("diskpart", wipe, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("install.swm", wipe);
  }
}
