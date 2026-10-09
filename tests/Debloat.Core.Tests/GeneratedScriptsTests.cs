using System.Diagnostics;
using System.Xml;
using Debloat.Core.Presets;

namespace Debloat.Core.Tests;

/// <summary>
/// Extrai os scripts de dentro do autounattend.xml gerado (como a instalação faz) e passa no parser do
/// Windows PowerShell 5.1, o mesmo que roda na instalação. Pegou na VM: um comentário com o marcador
/// dos apps recebeu o JSON inteiro e o FirstLogon não rodava.
/// </summary>
public class GeneratedScriptsTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Todos_os_scripts_gerados_tem_sintaxe_valida_no_PowerShell_51(bool wipeDisk)
  {
    var xml = new XmlDocument();
    xml.Load(new MemoryStream(new UnattendBuilder().BuildBytes(new DebloatOptions { WipeDisk0 = wipeDisk, Dns = DnsChoice.AdGuard })));
    var ns = new XmlNamespaceManager(xml.NameTable);
    ns.AddNamespace("e", "https://schneegans.de/windows/unattend-generator/");

    string dir = Directory.CreateTempSubdirectory("debloat-scripts-").FullName;
    try
    {
      foreach (XmlElement file in xml.SelectNodes("//e:File", ns)!)
      {
        string name = Path.GetFileName(file.GetAttribute("path"));
        if (!name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) continue;
        string content = file.InnerText.Trim();
        Assert.DoesNotContain("@@", content);
        File.WriteAllText(Path.Combine(dir, name), content, new System.Text.UTF8Encoding(true));
      }

      string check = $$"""
        $bad = 0
        foreach( $f in Get-ChildItem -LiteralPath '{{dir}}' -Filter *.ps1 ) {
          $errors = $null
          [void][System.Management.Automation.Language.Parser]::ParseFile( $f.FullName, [ref]$null, [ref]$errors )
          foreach( $e in $errors ) { "$($f.Name):$($e.Extent.StartLineNumber): $($e.Message)"; $bad++ }
        }
        exit $bad
        """;
      var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
        "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(check)))
      { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
      using var p = Process.Start(psi)!;
      string output = p.StandardOutput.ReadToEnd();
      p.WaitForExit();
      Assert.True(p.ExitCode == 0, "Erros de sintaxe nos scripts gerados:\n" + output);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }
}

public class FirstLogonScriptTests
{
  private static readonly string Script = Resources.Script("FirstLogon.ps1");

  [Fact]
  public void Winget_recebe_os_argumentos_de_verdade()
  {
    // "& $winget @args" chamava o winget sem argumento nenhum (ajuda + código 0): achado na VM.
    Assert.DoesNotContain("@args", Script);
    Assert.Contains("Invoke-Winget $wingetArgs", Script);
  }

  [Fact]
  public void Internet_e_testada_por_HTTP_e_nao_por_ping()
  {
    Assert.DoesNotContain("Test-Connection", Script);
    Assert.Contains("msftconnecttest.com", Script);
  }
}
