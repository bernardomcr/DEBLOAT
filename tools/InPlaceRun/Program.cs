// Roda o modo "sem pendrive" (InPlaceInstaller) sem a janela, para o teste na VM:
//   InPlaceRun <pasta da instalação> [--programas --senha <senha>]
// --programas: detecta os programas deste PC como a aba Backup (InstalledPrograms), põe os do winget como apps extras no
// autounattend.xml e leva a pasta dos outros no backup da partição temporária — o mesmo que o app faz.
// Não reinicia: quem chama decide (o VmTest reinicia a VM depois de conferir a saída).
using Debloat.Core.Catalog;
using Debloat.Core.Media;
using Debloat.Core.Presets;
using Debloat.Core.Saves;

if (args.Length < 1 || !Directory.Exists(args[0]))
{
  Console.WriteLine("Uso: InPlaceRun <pasta da instalação montada> [--programas --senha <senha>]");
  return 2;
}
string media = args[0];
try
{
  long backupBytes = 0;
  Func<string, Task>? writeBackup = null;
  if (args.Contains("--programas") && OperatingSystem.IsWindows())
  {
    var catalog = AppCatalog.Load();
    var found = await InstalledPrograms.DetectAsync(catalog.Apps.ToList());
    foreach (var p in found) Console.WriteLine($"programa: {p.Name} | winget={p.WingetId ?? "-"} | pasta={p.Folder ?? "-"} | exe={p.Exe ?? "-"}");
    var extras = found.Where(p => p.WingetId is not null).Select(p => new ExtraApp(p.WingetId!, p.Name)).ToList();
    var folders = found.Where(p => p.WingetId is null && p.Folder is not null).Select(p =>
      new MigrationItem("programa-" + string.Concat(p.Name.Where(char.IsLetterOrDigit)), MigrationKind.Program, p.Name, p.Folder!, p.Bytes, "", true) { Shortcut = p.Exe }).ToList();
    int passwordAt = Array.IndexOf(args, "--senha");
    var options = new DebloatOptions { Password = passwordAt >= 0 ? args[passwordAt + 1] : "", ExtraApps = extras };
    await File.WriteAllBytesAsync(Path.Combine(media, "autounattend.xml"), new UnattendBuilder(catalog).BuildBytes(options));
    Console.WriteLine($"{extras.Count} pelo winget, {folders.Count} pela pasta");
    backupBytes = folders.Sum(f => f.Bytes);
    writeBackup = root => Migration.BackupAsync(folders, root);
  }
  var plan = await InPlaceInstaller.CheckAsync(media, extraBytes: backupBytes);
  Console.WriteLine($"Mídia {plan.MediaSize / 1e9:F1} GB, partição {plan.PartitionSize / 1e9:F1} GB, livre no C: {plan.FreeOnC / 1e9:F1} GB, criptografado={plan.Encrypted}, UEFI={plan.Uefi}");
  await InPlaceInstaller.PrepareAsync(media, new Step(), backupBytes: backupBytes, writeBackup: writeBackup);
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
