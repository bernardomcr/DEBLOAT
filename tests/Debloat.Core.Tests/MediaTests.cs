using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Debloat.Core.Media;

namespace Debloat.Core.Tests;

public class MediaTests
{
  private const string SampleCatalog = """
    <MCT><Catalogs><Catalog version="2.0"><PublishedMedia><Files>
      <File><FileName>26100.4349.250607-1500.ge_release_svc_refresh_CLIENTCONSUMER_RET_x64FRE_pt-br.esd</FileName>
        <LanguageCode>pt-br</LanguageCode><Edition>Professional</Edition><Architecture>x64</Architecture>
        <Size>100</Size><Sha1>aa</Sha1><FilePath>http://dl.example/old.esd</FilePath></File>
      <File><FileName>26200.6584.250915-1905.25h2_ge_release_svc_refresh_CLIENTCONSUMER_RET_x64FRE_pt-br.esd</FileName>
        <LanguageCode>pt-br</LanguageCode><Edition>Professional</Edition><Architecture>x64</Architecture>
        <Size>200</Size><Sha1></Sha1><Sha256>bb</Sha256><FilePath>http://dl.example/new.esd</FilePath></File>
      <File><FileName>26200.6584.250915-1905.25h2_ge_release_svc_refresh_CLIENTCONSUMER_RET_A64FRE_pt-br.esd</FileName>
        <LanguageCode>pt-br</LanguageCode><Edition>Professional</Edition><Architecture>ARM64</Architecture>
        <Size>300</Size><Sha256>cc</Sha256><FilePath>http://dl.example/arm.esd</FilePath></File>
    </Files></PublishedMedia></Catalog></Catalogs></MCT>
    """;

  [Fact]
  public void Catalogo_escolhe_o_build_mais_novo_do_idioma_e_arquitetura()
  {
    var pick = WindowsCatalog.Pick(WindowsCatalog.Parse(SampleCatalog));
    Assert.NotNull(pick);
    Assert.Equal(new Version(26200, 6584), pick.Build);
    Assert.Equal("bb", pick.Sha256);
    Assert.Null(pick.Sha1);
    Assert.Equal("http://dl.example/new.esd", pick.Url.ToString());
  }

  [Fact]
  public void Catalogo_com_BOM_e_lido()
  {
    Assert.Equal(3, WindowsCatalog.Parse((char)0xFEFF + SampleCatalog).Count);
  }

  /// <summary>Servidor falso que responde faixas de um array, às vezes cortando a conexão no meio.</summary>
  private sealed class RangeHandler(byte[] data, bool flaky) : HttpMessageHandler
  {
    private int calls;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      var range = request.Headers.Range!.Ranges.Single();
      long from = range.From!.Value, to = range.To!.Value;
      int length = (int)(to - from + 1);
      if (flaky && Interlocked.Increment(ref calls) % 3 == 1) length /= 2;    // conexão cai na metade
      var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
      {
        Content = new ByteArrayContent(data, (int)from, length),
      };
      response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, data.Length);
      return Task.FromResult(response);
    }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Download_por_partes_monta_o_arquivo_certo(bool flaky)
  {
    byte[] data = RandomNumberGenerator.GetBytes(80 << 20);   // 80 MB → 2 partes de 40 MB
    string sha256 = Convert.ToHexString(SHA256.HashData(data));
    string path = Path.Combine(Path.GetTempPath(), $"debloat-test-{Guid.NewGuid():N}.esd");
    try
    {
      using var http = new HttpClient(new RangeHandler(data, flaky));
      await new SegmentedDownloader(http).DownloadAsync(new Uri("http://falso/x.esd"), path, data.Length, sha256, null);
      Assert.Equal(data, await File.ReadAllBytesAsync(path));
      Assert.False(File.Exists(path + ".partes"));
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Fact]
  public async Task Hash_errado_apaga_o_arquivo()
  {
    byte[] data = RandomNumberGenerator.GetBytes(1 << 20);
    string path = Path.Combine(Path.GetTempPath(), $"debloat-test-{Guid.NewGuid():N}.esd");
    using var http = new HttpClient(new RangeHandler(data, false));
    await Assert.ThrowsAsync<InvalidDataException>(() =>
      new SegmentedDownloader(http).DownloadAsync(new Uri("http://falso/x.esd"), path, data.Length, new string('0', 64), null));
    Assert.False(File.Exists(path));
  }
}
