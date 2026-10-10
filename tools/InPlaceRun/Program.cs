// Roda o modo "sem pendrive" (InPlaceInstaller) sem a janela, para o teste na VM: InPlaceRun <pasta da instalação>
// Não reinicia: quem chama decide (o VmTest reinicia a VM depois de conferir a saída).
using Debloat.Core.Media;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
  Console.WriteLine("Uso: InPlaceRun <pasta da instalação montada>");
  return 2;
}
try
{
  var plan = await InPlaceInstaller.CheckAsync(args[0]);
  Console.WriteLine($"Mídia {plan.MediaSize / 1e9:F1} GB, partição {plan.PartitionSize / 1e9:F1} GB, livre no C: {plan.FreeOnC / 1e9:F1} GB, criptografado={plan.Encrypted}, UEFI={plan.Uefi}");
  await InPlaceInstaller.PrepareAsync(args[0], new Step());
  Console.WriteLine("OK");
  return 0;
}
catch (Exception e)
{
  Console.WriteLine("ERRO: " + e);
  return 1;
}

sealed class Step : IProgress<WriteStep>
{
  private string last = "";

  public void Report(WriteStep value)
  {
    if (value.Text == last) return;
    last = value.Text;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {value.Fraction:P0} {value.Text}");
  }
}
