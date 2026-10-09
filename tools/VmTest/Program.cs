// Teste de ponta a ponta numa VM do Hyper-V (precisa de administrador e do Hyper-V ligado):
// monta a mídia com o preset (apagando o disco 0 da VM sem perguntar), gera a ISO, cria a VM
// (Geração 2, Secure Boot, TPM), aperta a tecla do "boot pelo DVD" e grava um print da tela a cada 15 s.
// Saída em %LOCALAPPDATA%\DEBLOAT\vmtest (log.txt e telas\*.png). Uso: VmTest [minutos=90]
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Management;
using System.Runtime.InteropServices;
using Debloat.Core.Media;
using Debloat.Core.Presets;

const string VmName = "DEBLOAT-Teste";
string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DEBLOAT", "vmtest");
string shots = Path.Combine(root, "telas");
string log = Path.Combine(root, "log.txt");
int minutes = args.Select(a => int.TryParse(a, out int m) ? m : 0).FirstOrDefault(m => m > 0, 90);
bool reuse = args.Contains("--reusar");     // reaproveita a mídia já montada e só troca o autounattend.xml
Directory.CreateDirectory(shots);
File.WriteAllText(log, "");
var logLock = new object();
void Log(string text)
{
  // O Progress<T> chama de outra thread: sem trava, duas escritas simultâneas derrubavam o processo.
  lock (logLock)
  {
    try { File.AppendAllText(log, $"[{DateTime.Now:HH:mm:ss}] {text}\n"); } catch (IOException) { }
  }
}

try
{
  string esd = Directory.GetFiles(Path.Combine(root, "..", "cache"), "*.esd").Single();
  string media = Path.Combine(root, "midia");
  // ISO e disco ficam numa pasta do sistema: no 1º teste o Hyper-V disse "anexo não encontrado" com a ISO no AppData.
  string vmDir = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DEBLOAT-VM")).FullName;
  string iso = Path.Combine(vmDir, "teste.iso");
  string vhd = Path.Combine(vmDir, "disco.vhdx");

  Log("Removendo VM antiga, se houver");
  await Ps($"if( Get-VM -Name '{VmName}' -ErrorAction SilentlyContinue ) {{ Stop-VM -Name '{VmName}' -TurnOff -Force; Remove-VM -Name '{VmName}' -Force }}; Remove-Item -LiteralPath '{vhd}' -ErrorAction SilentlyContinue");

  var options = new DebloatOptions { WipeDisk0 = true };
  byte[] xml = new UnattendBuilder().BuildBytes(options);
  if (reuse && File.Exists(Path.Combine(media, "setup.exe")))
  {
    Log("Reaproveitando a mídia; só o autounattend.xml é novo");
    File.WriteAllBytes(Path.Combine(media, "autounattend.xml"), xml);
  }
  else
  {
    Log("Montando a mídia (preset padrão + apagar disco 0)");
    await MediaBuilder.BuildAsync(esd, media, "Professional", xml, new Progress<MediaStep>(s => Log($"  {s.Fraction:P0} {s.Text}")));
  }
  foreach (var stale in Directory.EnumerateFiles(shots)) File.Delete(stale);
  Log("Gerando a ISO");
  IsoWriter.Write(media, iso);

  Log("Criando a VM");
  await Ps($$"""
    New-VM -Name '{{VmName}}' -Generation 2 -MemoryStartupBytes 6GB -NewVHDPath '{{vhd}}' -NewVHDSizeBytes 80GB -SwitchName 'Default Switch' | Out-Null
    Set-VMMemory -VMName '{{VmName}}' -DynamicMemoryEnabled $false
    Set-VMProcessor -VMName '{{VmName}}' -Count 4
    Set-VMFirmware -VMName '{{VmName}}' -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows
    Set-VMKeyProtector -VMName '{{VmName}}' -NewLocalKeyProtector
    Enable-VMTPM -VMName '{{VmName}}'
    $vmId = (Get-VM -Name '{{VmName}}').Id
    icacls.exe '{{iso}}' /grant "NT VIRTUAL MACHINE\$($vmId):(R)" | Out-Null
    $dvd = $null
    foreach( $try in 1..5 ) {
      try { $dvd = Add-VMDvdDrive -VMName '{{VmName}}' -Path '{{iso}}' -Passthru; break }
      catch { if( $try -eq 5 ) { throw }; Start-Sleep -Seconds 10 }   # antivírus ainda lendo a ISO recém-criada
    }
    Set-VMFirmware -VMName '{{VmName}}' -FirstBootDevice $dvd
    Set-VM -Name '{{VmName}}' -AutomaticCheckpointsEnabled $false -CheckpointType Disabled
    Start-VM -Name '{{VmName}}'
    """);

  var scope = new ManagementScope(@"\\.\root\virtualization\v2");
  scope.Connect();
  var vm = Query(scope, $"SELECT * FROM Msvm_ComputerSystem WHERE ElementName='{VmName}'").First();
  string vmId = (string)vm["Name"];

  // "Press any key to boot from CD or DVD": aperta Enter algumas vezes nos primeiros segundos.
  var keyboard = Query(scope, $"SELECT * FROM Msvm_Keyboard WHERE SystemName='{vmId}'").First();
  for (int i = 0; i < 8; i++)
  {
    await Task.Delay(1000);
    try { keyboard.InvokeMethod("TypeKey", [13]); } catch (ManagementException) { }
  }
  Log("VM ligada; acompanhando a tela");

  var service = Query(scope, "SELECT * FROM Msvm_VirtualSystemManagementService").First();
  var clock = Stopwatch.StartNew();
  int n = 0;
  while (clock.Elapsed < TimeSpan.FromMinutes(minutes))
  {
    var settings = Query(scope, $"SELECT * FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemIdentifier='{vmId}' AND VirtualSystemType='Microsoft:Hyper-V:System:Realized'").First();
    var input = service.GetMethodParameters("GetVirtualSystemThumbnailImage");
    input["TargetSystem"] = settings.Path.Path;
    input["WidthPixels"] = 1024;
    input["HeightPixels"] = 768;
    var output = service.InvokeMethod("GetVirtualSystemThumbnailImage", input, null);
    if (output["ImageData"] is byte[] data && data.Length == 1024 * 768 * 2)
    {
      string file = Path.Combine(shots, $"{n++:D4}-{DateTime.Now:HHmmss}.png");
      SaveRgb565(data, 1024, 768, file);
      File.Copy(file, Path.Combine(shots, "atual.png"), overwrite: true);
    }
    var state = Query(scope, $"SELECT EnabledState FROM Msvm_ComputerSystem WHERE Name='{vmId}'").First()["EnabledState"];
    if (Convert.ToInt32(state) == 3) { Log("VM desligou"); break; }     // 3 = desligada
    await Task.Delay(15000);
  }
  Log($"Fim do acompanhamento ({clock.Elapsed.TotalMinutes:F0} min). A VM continua no Hyper-V para inspeção.");
}
catch (Exception e)
{
  Log("ERRO: " + e);
}

static IEnumerable<ManagementObject> Query(ManagementScope scope, string wql) =>
  new ManagementObjectSearcher(scope, new ObjectQuery(wql)).Get().Cast<ManagementObject>();

static void SaveRgb565(byte[] data, int width, int height, string file)
{
  using var bitmap = new Bitmap(width, height, PixelFormat.Format16bppRgb565);
  var bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format16bppRgb565);
  for (int y = 0; y < height; y++) Marshal.Copy(data, y * width * 2, bits.Scan0 + y * bits.Stride, width * 2);
  bitmap.UnlockBits(bits);
  bitmap.Save(file, ImageFormat.Png);
}

static async Task Ps(string script)
{
  var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
    "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'\n" + script + "\nexit 0")))
  { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
  using var p = Process.Start(psi)!;
  string err = await p.StandardError.ReadToEndAsync();
  await p.WaitForExitAsync();
  if (p.ExitCode != 0) throw new InvalidOperationException(err);
}
