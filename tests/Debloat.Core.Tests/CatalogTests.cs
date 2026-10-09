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
      "rustdesk", "tailscale", "sharex", "nanazip", "winrar", "7zip", "hydra", "wand"];
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
