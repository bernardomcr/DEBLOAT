using Debloat.Core.Catalog;

namespace Debloat.Core.Tests;

public class CatalogTests
{
  private readonly AppCatalog catalog = AppCatalog.Load();

  [Fact]
  public void Ids_sao_unicos()
  {
    var duplicated = catalog.Apps.GroupBy(a => a.Id).Where(g => g.Count() > 1).Select(g => g.Key);
    Assert.Empty(duplicated);
  }

  [Fact]
  public void Toda_entrada_tem_categoria_e_fonte_validas()
  {
    var categories = catalog.Categories.Select(c => c.Id).ToHashSet();
    foreach (var app in catalog.Apps)
    {
      Assert.Contains(app.Category, categories);
      Assert.Contains(app.Source, AppCatalog.KnownSources);
      Assert.False(string.IsNullOrWhiteSpace(app.Package), app.Id);
      if (app.Source == "github") Assert.False(string.IsNullOrWhiteSpace(app.Asset), app.Id);
    }
  }

  [Fact]
  public void Dependencias_existem_e_sao_resolvidas()
  {
    var resolved = catalog.Resolve(["everything-toolbar"]).Select(a => a.Id).ToList();
    Assert.Equal(["everything", "everything-toolbar"], resolved);
  }

  [Fact]
  public void DotNet_vem_em_todas_as_versoes_e_arquiteturas()
  {
    foreach (var version in new[] { "3.1", "5", "6", "7", "8", "9", "10" })
    {
      foreach (var arch in new[] { "x64", "x86" })
      {
        var app = catalog.Apps.Single(a => a.Id == $"dotnet-{version}-{arch}");
        Assert.True(app.Default);
        Assert.Equal(arch, app.Architecture);
      }
    }
  }

  [Fact]
  public void Apps_do_usuario_vem_marcados()
  {
    string[] mine = ["discord", "chrome", "steam", "firefox", "whatsapp", "claude", "chatgpt", "parsec",
      "rustdesk", "tailscale", "sharex", "nanazip", "hydra", "wand", "telegram", "vlc", "python"];
    foreach (var id in mine) Assert.True(catalog.Apps.Single(a => a.Id == id).Default, id);
  }
}

public class FallbackTests
{
  [Fact]
  public void Plano_B_sempre_exige_assinador()
  {
    foreach (var app in AppCatalog.Load().Apps.Where(a => a.FallbackUrl is not null))
    {
      Assert.False(string.IsNullOrWhiteSpace(app.Signer), app.Id);
      Assert.StartsWith("https://", app.FallbackUrl);
    }
  }
}

public class OfflineInstallerTests
{
  [Fact]
  public void Burn_usa_a_opcao_silenciosa_e_os_codigos_de_sucesso_do_manifesto()
  {
    const string yaml = """
      PackageIdentifier: Microsoft.VCRedist.2015+.x86
      Installers:
      - Architecture: x86
        InstallerType: burn
        InstallerSwitches:
          Silent: /quiet /norestart
          SilentWithProgress: /passive /norestart
        InstallerSuccessCodes:
        - 3010
        - 1638
      ManifestType: merged
      """;
    var plan = OfflineInstallers.ParseManifest(yaml)!.Value;
    Assert.Equal("exe", plan.Kind);
    Assert.Equal("/quiet /norestart", plan.Args);
    Assert.Equal([3010, 1638], plan.SuccessCodes);
  }

  [Theory]
  [InlineData("wix", "msi", "/qn /norestart")]
  [InlineData("nullsoft", "exe", "/S")]
  [InlineData("inno", "exe", "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-")]
  [InlineData("msix", "msix", null)]
  public void Sem_opcao_no_manifesto_usa_a_padrao_do_tipo(string type, string kind, string? args)
  {
    var plan = OfflineInstallers.ParseManifest($"Installers:\n- Architecture: x64\n  InstallerType: {type}\n")!.Value;
    Assert.Equal(kind, plan.Kind);
    Assert.Equal(args, plan.Args);
  }

  [Fact]
  public void Custom_e_somado_e_SilentWithProgress_nao_conta_como_Silent()
  {
    var plan = OfflineInstallers.ParseManifest("Installers:\n- InstallerType: inno\n  InstallerSwitches:\n    SilentWithProgress: /SILENT\n    Custom: '/MERGETASKS=!runcode'\n")!.Value;
    Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /MERGETASKS=!runcode", plan.Args);
  }

  [Theory]
  [InlineData("Installers:\n- InstallerType: zip\n  NestedInstallerType: portable\n")]
  [InlineData("Installers:\n- InstallerType: portable\n")]
  public void Sem_jeito_de_instalar_sozinho_fica_pela_internet(string yaml) => Assert.Null(OfflineInstallers.ParseManifest(yaml));
}

public class OfflineInstallerCrlfTests
{
  [Fact]
  public void Manifesto_com_CRLF_como_o_winget_grava()
  {
    var plan = OfflineInstallers.ParseManifest("Installers:\r\n- Architecture: x64\r\n  InstallerType: wix\r\n  InstallerSuccessCodes:\r\n  - 3010\r\n")!.Value;
    Assert.Equal("msi", plan.Kind);
    Assert.Equal([3010], plan.SuccessCodes);
  }
}

public class OfflineInstallerParallelTests
{
  [Theory]
  [InlineData("nullsoft", true)]
  [InlineData("inno", true)]
  [InlineData("msix", true)]
  [InlineData("wix", false)]
  [InlineData("burn", false)]
  [InlineData("exe", false)]
  public void So_quem_nao_usa_o_Windows_Installer_roda_em_paralelo(string type, bool parallel) =>
    Assert.Equal(parallel, OfflineInstallers.ParseManifest($"Installers:\n- InstallerType: {type}\n")!.Value.Parallel);
}

public class UpdatesTests
{
  [Theory]
  [InlineData("v1.0.1", true)]
  [InlineData("v1.1.0", true)]
  [InlineData("v1.0.0", false)]
  [InlineData("v0.9.0", false)]
  [InlineData("lixo", false)]
  public void Avisa_so_quando_a_release_e_mais_nova(string tag, bool newer) =>
    Assert.Equal(newer, Debloat.Core.Updates.Parse(tag, "https://github.com/x", new Version(1, 0, 0, 0)) is not null);
}
