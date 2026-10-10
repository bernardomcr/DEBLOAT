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

public class UsbLayoutTests
{
  [Theory]
  [InlineData(16L << 30, 16L << 30)]     // pendrive pequeno: tudo FAT32
  [InlineData(62L << 30, 31L << 30)]     // 64 GB: 31 GiB FAT32 + resto NTFS
  public void Particao_de_boot_respeita_o_limite_do_FAT32(long disk, long expected)
  {
    Assert.Equal(expected, Debloat.Core.Media.UsbWriter.BootPartitionSize(disk));
  }

  [Fact]
  public void Drivers_de_video_nunca_vao_para_o_pendrive()
  {
    Assert.DoesNotContain("Display", Debloat.Core.Media.DriverExporter.Classes);
    Assert.DoesNotContain("ROOT\\", Debloat.Core.Media.DriverExporter.PhysicalBuses);
  }
}

public class InPlaceScriptTests
{
  private static readonly string Script = Debloat.Core.Resources.Script("InPlace-instalar.cmd");

  [Fact]
  public void So_formata_a_particao_com_o_mesmo_token()
  {
    int tokenCheck = Script.IndexOf("if \"!T!\"==\"%TOKEN%\"", StringComparison.Ordinal);
    int notFound = Script.IndexOf("if not defined TGT", StringComparison.Ordinal);
    int format = Script.IndexOf("format %TGT%", StringComparison.Ordinal);
    Assert.True(tokenCheck > 0 && notFound > tokenCheck && format > notFound, "a checagem do token tem que vir antes do format");
    Assert.Contains("if /i \"%TGT%\"==\"%SRC%\" goto :abortar", Script);
  }

  [Fact]
  public void Formata_uma_vez_e_nunca_o_disco_inteiro()
  {
    Assert.Single(System.Text.RegularExpressions.Regex.Matches(Script, @"^format ", System.Text.RegularExpressions.RegexOptions.Multiline));
    Assert.DoesNotContain("diskpart", Script, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("clean", Script, StringComparison.OrdinalIgnoreCase);
  }
}

public class SaveTests
{
  [Fact]
  public void Caminhos_viram_variaveis_para_trocar_de_usuario()
  {
    string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    Assert.Equal(@"%APPDATA%\GSE Saves\1971870", Debloat.Core.Saves.SaveScanner.TokenizePath(Path.Combine(appdata, "GSE Saves", "1971870")));
  }

  [Fact]
  public void Raizes_de_emuladores_confirmadas_em_dados_reais()
  {
    var verified = Debloat.Core.Saves.SaveScanner.Roots().Where(r => r.Verified).Select(r => r.Label).ToList();
    Assert.Contains("OnlineFix", verified);
    Assert.Contains("Goldberg (gbe_fork)", verified);
    Assert.Contains("CODEX / PLAZA", verified);
  }
}

public class InPlaceSafetyTests
{
  private static readonly string Script = Debloat.Core.Resources.Script("InPlace-instalar.cmd");

  [Theory]
  [InlineData("if not exist \"%IMG%\" (")]
  [InlineData(@"if not exist ""%SRC%\autounattend.xml""")]
  [InlineData("bcdboot.exe\" (")]
  [InlineData("mountvol %ESP% /s")]
  [InlineData(@"if not exist %ESP%\EFI\Microsoft\Boot")]
  public void Tudo_e_conferido_antes_de_formatar(string check)
  {
    int at = Script.IndexOf(check, StringComparison.Ordinal);
    Assert.True(at > 0, check);
    Assert.True(at < Script.IndexOf("format %TGT%", StringComparison.Ordinal), $"'{check}' tem que vir antes do format");
  }

  [Fact]
  public void Menu_de_boot_so_perde_as_entradas_da_particao_formatada()
  {
    Assert.Contains("if /i \"%%b\"==\"partition=%TGT%\" if \"!ID:~9,1!\"==\"-\" bcdedit /delete !ID! /f", Script);
    Assert.DoesNotContain("/timeout", Script);
  }

  [Fact]
  public void Limpeza_no_primeiro_login_so_no_disco_do_C_e_depois_dos_apps()
  {
    string firstLogon = Debloat.Core.Resources.Script("FirstLogon.ps1");
    Assert.Contains("Get-Partition -ErrorAction SilentlyContinue | Where-Object DiskNumber -eq $systemPartition.DiskNumber", firstLogon);
    Assert.True(firstLogon.IndexOf("$setupPartition | Remove-Partition", StringComparison.Ordinal) > firstLogon.IndexOf("FIM da lista de apps", StringComparison.Ordinal));
  }
}

public class PowerShellErrorTests
{
  [Fact]
  public void Erro_em_CLIXML_vira_a_mensagem()
  {
    const string clixml = "#< CLIXML\r\n<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"><Obj S=\"progress\" RefId=\"0\"><TN RefId=\"0\"><T>X</T></TN></Obj>"
      + "<S S=\"Error\">New-Partition : Not enough available capacity_x000D__x000A_</S><S S=\"Error\">No linha:7 caractere:14_x000D__x000A_</S><S S=\"Error\">+ $p = New-Partition_x000D__x000A_</S></Objs>";
    Assert.Equal("New-Partition : Not enough available capacity", Debloat.Core.Media.PowerShell.ErrorText(clixml));
  }
}

public class InstalledProgramsTests
{
  [Fact]
  public void Le_a_tabela_do_winget_com_CRLF_e_progresso()
  {
    string output = "   -\r   \\r   |\r"
      + "Name                       Id                         Version     Available\r\n"
      + "-------------------------------------------------------------------------\r\n"
      + "AB Download Manager        amir1376.ABDownloadManager 1.8.7       1.10.4\r\n"
      + "PostgreSQL 17              PostgreSQL.PostgreSQL.17   17.6\r\n";
    var rows = Debloat.Core.Saves.InstalledPrograms.ParseList(output);
    Assert.Equal([("AB Download Manager", "amir1376.ABDownloadManager"), ("PostgreSQL 17", "PostgreSQL.PostgreSQL.17")], rows);
  }
}
