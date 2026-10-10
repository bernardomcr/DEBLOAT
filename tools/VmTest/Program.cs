// Teste de ponta a ponta numa VM do Hyper-V (precisa de administrador e do Hyper-V ligado):
// monta a mídia com o preset (apagando o disco 0 da VM sem perguntar), gera a ISO, cria a VM
// (Geração 2, Secure Boot, TPM), aperta a tecla do "boot pelo DVD" e grava um print da tela a cada 15 s.
// Saída em %LOCALAPPDATA%\DEBLOAT\vmtest (log.txt e telas\*.png). Uso: VmTest [minutos=90]
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Management;
using System.Runtime.InteropServices;
using Debloat.Core.Catalog;
using Debloat.Core.Media;
using Debloat.Core.Presets;

const string VmName = "DEBLOAT-Teste";
string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DEBLOAT", "vmtest");
string shots = Path.Combine(root, "telas");
string log = Path.Combine(root, "log.txt");
int minutes = args.Select(a => int.TryParse(a, out int m) ? m : 0).FirstOrDefault(m => m > 0, 90);
bool reuse = args.Contains("--reusar");
bool watchOnly = args.Contains("--acompanhar");  // só acompanha a VM que já está rodando (não recria nada)
if (watchOnly) log = Path.Combine(root, "log-acompanhar.txt");     // reaproveita a mídia já montada e só troca o autounattend.xml
Directory.CreateDirectory(shots);
if (!args.Contains("--teclar") && !args.Contains("--coletar") && !args.Contains("--rodar-apps") && !args.Contains("--sem-pendrive") && !watchOnly) File.WriteAllText(log, "");   // só o teste completo zera o log
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
  if (args.Contains("--coletar"))
  {
    // Desliga a VM, abre o disco virtual aqui em somente leitura, copia os logs e liga a VM de novo.
    string vhdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DEBLOAT-VM", "disco.vhdx");
    string dest = Path.Combine(root, "logs-vm", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    Directory.CreateDirectory(dest);
    await Ps($$"""
      Stop-VM -Name '{{VmName}}' -Force -ErrorAction SilentlyContinue
      $disk = Mount-VHD -Path '{{vhdPath}}' -ReadOnly -Passthru | Get-Disk
      try {
        $win = Get-Partition -DiskNumber $disk.Number | Where-Object { $_.Type -eq 'Basic' -and $_.Size -gt 20GB } | Select-Object -First 1
        if( -not $win.DriveLetter ) { $win | Add-PartitionAccessPath -AssignDriveLetter; $win = Get-Partition -DiskNumber $disk.Number -PartitionNumber $win.PartitionNumber }
        $r = "$($win.DriveLetter):"
        $copy = @{ 'debloat' = "$r\Debloat\logs"; 'scripts' = "$r\Windows\Setup\Scripts"; 'panther' = "$r\Windows\Panther" }
        foreach( $k in $copy.Keys ) {
          if( Test-Path -LiteralPath $copy[$k] ) { robocopy.exe $copy[$k] (Join-Path '{{dest}}' $k) *.log *.txt *.json /S /R:0 /W:0 /NFL /NDL /NJH /NJS | Out-Null }
        }
        Get-ChildItem "$r\Users\*\AppData\Local\Temp\UserOnce.log" -ErrorAction SilentlyContinue | Copy-Item -Destination '{{dest}}'
        Get-ChildItem "$r\ProgramData\Microsoft\Windows\Start Menu\Programs", "$r\Users\Usuario\AppData\Roaming\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter *.lnk -ErrorAction SilentlyContinue |
          Select-Object -ExpandProperty BaseName | Sort-Object -Unique | Out-File (Join-Path '{{dest}}' 'atalhos-no-iniciar.txt')
      } finally {
        Dismount-VHD -Path '{{vhdPath}}'
      }
      Start-VM -Name '{{VmName}}'
      """);
    Log($"Logs da VM copiados para {dest}");
    return;
  }
  int runAt = Array.IndexOf(args, "--rodar-apps");
  if (runAt >= 0)
  {
    // Roda o primeiro login só com alguns apps DENTRO da VM já instalada (PowerShell Direct), sem recriar nada.
    var catalog = Debloat.Core.Catalog.AppCatalog.Load();
    var apps = catalog.Resolve(args[runAt + 1].Split(','));
    string script = Debloat.Core.Resources.Script("FirstLogon.ps1")
      .Replace("@@DNS@@", "provider")
      .Replace("@@APPS@@", Debloat.Core.Catalog.AppCatalog.ToScriptJson(apps));
    string local = Path.Combine(root, "rodar.ps1");
    File.WriteAllText(local, script, new System.Text.UTF8Encoding(true));
    string output = Path.Combine(root, $"rodar-{DateTime.Now:HHmmss}.log");
    Log($"Rodando na VM: {string.Join(", ", apps.Select(a => a.Name))}");
    await Ps($$"""
      $cred = New-Object System.Management.Automation.PSCredential('Usuario', (New-Object System.Security.SecureString))
      $s = $null
      try { $s = New-PSSession -VMName '{{VmName}}' -Credential $cred -ErrorAction Stop } catch { }
      if( -not $s ) {
      # Só na VM de teste: libera conta sem senha no PowerShell Direct (o Windows bloqueia por padrão).
      # Edita o registro da VM com ela desligada, pelo disco virtual.
      $vhd = Join-Path $env:ProgramData 'DEBLOAT-VM\disco.vhdx'
      Stop-VM -Name '{{VmName}}' -Force
      $disk = Mount-VHD -Path $vhd -Passthru | Get-Disk
      try {
        $win = Get-Partition -DiskNumber $disk.Number | Where-Object { $_.Type -eq 'Basic' -and $_.Size -gt 20GB } | Select-Object -First 1
        if( -not $win.DriveLetter ) { $win | Add-PartitionAccessPath -AssignDriveLetter; $win = Get-Partition -DiskNumber $disk.Number -PartitionNumber $win.PartitionNumber }
        reg.exe load HKLM\DEBLOATVM "$($win.DriveLetter):\Windows\System32\config\SYSTEM" | Out-Null
        reg.exe add HKLM\DEBLOATVM\ControlSet001\Control\Lsa /v LimitBlankPasswordUse /t REG_DWORD /d 0 /f | Out-Null
        [gc]::Collect()
        reg.exe unload HKLM\DEBLOATVM | Out-Null
      } finally {
        Dismount-VHD -Path $vhd
      }
      Start-VM -Name '{{VmName}}'
      $s = $null
      foreach( $try in 1..40 ) {
        try { $s = New-PSSession -VMName '{{VmName}}' -Credential $cred -ErrorAction Stop; break } catch { Start-Sleep -Seconds 10 }
      }
      if( -not $s ) { throw 'A VM não aceitou o PowerShell Direct depois de 6 minutos.' }
      }
      try {
        Invoke-Command -Session $s -ScriptBlock { Remove-Item 'C:\Debloat\logs\apps.log' -ErrorAction SilentlyContinue }
        Invoke-Command -Session $s -FilePath '{{local}}' *>&1 | Out-File '{{output}}' -Encoding UTF8
        Invoke-Command -Session $s -ScriptBlock { Get-Content 'C:\Debloat\logs\apps.log' } | Out-File '{{output}}' -Append -Encoding UTF8
      } finally {
        Remove-PSSession $s
      }
      """);
    Log($"Resultado em {output}");
    return;
  }
  int inPlaceAt = Array.IndexOf(args, "--sem-pendrive");
  if (inPlaceAt >= 0)
  {
    // Testa o modo sem pendrive na VM já instalada (precisa ter sido criada com --senha, para o PowerShell Direct):
    // 1. disco extra com a instalação (autounattend SEM apagar disco) e o InPlaceRun.exe;
    // 2. roda o InPlaceRun dentro da VM; 3. se deu OK, reinicia a VM e acompanha a tela.
    string runner = args[inPlaceAt + 1];
    string password = args[Array.IndexOf(args, "--senha") + 1];
    string inPlaceMedia = Path.Combine(root, "midia");
    string dataVhd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DEBLOAT-VM", "dados.vhdx");
    string answer = Path.Combine(root, "autounattend-sem-pendrive.xml");
    File.WriteAllBytes(answer, new UnattendBuilder().BuildBytes(new DebloatOptions { Password = password }));
    string output = Path.Combine(root, $"sem-pendrive-{DateTime.Now:HHmmss}.log");
    Log("Montando o disco extra com a instalação e o InPlaceRun");
    await Ps($$"""
      Get-VMHardDiskDrive -VMName '{{VmName}}' | Where-Object Path -eq '{{dataVhd}}' | Remove-VMHardDiskDrive
      Remove-Item -LiteralPath '{{dataVhd}}' -ErrorAction SilentlyContinue
      $disk = New-VHD -Path '{{dataVhd}}' -SizeBytes 40GB -Dynamic | Mount-VHD -Passthru | Initialize-Disk -PartitionStyle GPT -Passthru
      try {
        $p = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter
        Format-Volume -Partition $p -FileSystem NTFS -NewFileSystemLabel 'DEBLOAT-TESTE' -Confirm:$false | Out-Null
        $l = (Get-Partition -DiskNumber $disk.Number -PartitionNumber $p.PartitionNumber).DriveLetter
        robocopy.exe '{{inPlaceMedia}}' "$($l):\midia" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XD '$WinPEDriver$' | Out-Null
        Copy-Item -LiteralPath '{{answer}}' -Destination "$($l):\midia\autounattend.xml" -Force
        Copy-Item -LiteralPath '{{runner}}' -Destination "$($l):\InPlaceRun.exe" -Force
      } finally {
        Dismount-VHD -Path '{{dataVhd}}'
      }
      Add-VMHardDiskDrive -VMName '{{VmName}}' -Path '{{dataVhd}}'
      """);
    Log("Rodando o modo sem pendrive dentro da VM (PowerShell Direct)");
    await Ps($$"""
      $cred = New-Object System.Management.Automation.PSCredential('Usuario', (ConvertTo-SecureString '{{password}}' -AsPlainText -Force))
      $s = New-PSSession -VMName '{{VmName}}' -Credential $cred -ErrorAction Stop
      try {
        $result = Invoke-Command -Session $s -ScriptBlock {
          Start-Sleep -Seconds 5   # o disco extra acabou de chegar
          $l = (Get-Volume -FileSystemLabel 'DEBLOAT-TESTE').DriveLetter
          & "$($l):\InPlaceRun.exe" "$($l):\midia" 2>&1
          "SAIDA=$LASTEXITCODE"
          Get-Partition | Format-Table DiskNumber, PartitionNumber, DriveLetter, Size, Type -AutoSize | Out-String
          bcdedit.exe /enum all | Out-String
        }
        $result | Out-File '{{output}}' -Encoding UTF8
        if( ($result -join "`n") -notmatch 'SAIDA=0' ) { throw "O InPlaceRun falhou; veja {{output}}" }
        # Reinício normal, por dentro do Windows: o Restart-VM -Force é um reset e as mudanças no menu de boot
        # (ainda só na memória) se perdiam (VM, 10/10/2026).
        Invoke-Command -Session $s -ScriptBlock { shutdown.exe /r /t 3 }
      } finally {
        Remove-PSSession $s
      }
      """);
    Log($"InPlaceRun OK (saída em {output}); VM reiniciada no WinPE");
    var inPlaceScope = new ManagementScope(@"\\.\root\virtualization\v2");
    inPlaceScope.Connect();
    await Watch(inPlaceScope, (string)Query(inPlaceScope, $"SELECT * FROM Msvm_ComputerSystem WHERE ElementName='{VmName}'").First()["Name"]);
    return;
  }
  int typeAt = Array.IndexOf(args, "--teclar");
  if (typeAt >= 0)
  {
    // Digita na VM: cada argumento depois de --teclar é um texto, ou {SHIFT+F10} / {WIN+R} / {SHIFT} / {ENTER} / {ESC}.
    var typeScope = new ManagementScope(@"\\.\root\virtualization\v2");
    typeScope.Connect();
    string id = (string)Query(typeScope, $"SELECT * FROM Msvm_ComputerSystem WHERE ElementName='{VmName}'").First()["Name"];
    var kb = Query(typeScope, $"SELECT * FROM Msvm_Keyboard WHERE SystemName='{id}'").First();
    foreach (string part in args.Skip(typeAt + 1))
    {
      switch (part)
      {
        case "{SHIFT+F10}":
          kb.InvokeMethod("PressKey", [0x10]); kb.InvokeMethod("TypeKey", [0x79]); kb.InvokeMethod("ReleaseKey", [0x10]);
          break;
        case "{WIN+R}":
          kb.InvokeMethod("PressKey", [0x5B]); kb.InvokeMethod("TypeKey", [0x52]); kb.InvokeMethod("ReleaseKey", [0x5B]);
          break;
        case "{SHIFT}": kb.InvokeMethod("TypeKey", [0x10]); break;     // acorda a tela
        case "{ENTER}": kb.InvokeMethod("TypeKey", [0x0D]); break;
        case "{ESC}": kb.InvokeMethod("TypeKey", [0x1B]); break;
        case "{WAIT}": await Task.Delay(5000); break;
        case "{CTRL+HOME}":
          kb.InvokeMethod("PressKey", [0x11]); kb.InvokeMethod("TypeKey", [0x24]); kb.InvokeMethod("ReleaseKey", [0x11]);
          break;
        case "{CTRL+END}":
          kb.InvokeMethod("PressKey", [0x11]); kb.InvokeMethod("TypeKey", [0x23]); kb.InvokeMethod("ReleaseKey", [0x11]);
          break;
        default:
          // TypeText com a frase inteira perdia os espaços: digita um caractere por vez e o espaço como tecla.
          foreach (char c in part)
          {
            if (c == ' ') kb.InvokeMethod("TypeScancodes", [new byte[] { 0x39, 0xB9 }]);   // TypeKey(VK_SPACE) também sumia
            else kb.InvokeMethod("TypeText", [c.ToString()]);
            await Task.Delay(40);
          }
          break;
      }
      await Task.Delay(1500);
    }
    await Task.Delay(6000);
    var typeService = Query(typeScope, "SELECT * FROM Msvm_VirtualSystemManagementService").First();
    Log("teclar: " + (Shot(typeScope, typeService, id, Path.Combine(shots, $"teclar-{DateTime.Now:HHmmss}.png")) ?? "print salvo"));
    return;
  }
  if (watchOnly)
  {
    var watchScope = new ManagementScope(@"\\.\root\virtualization\v2");
    watchScope.Connect();
    string id = (string)Query(watchScope, $"SELECT * FROM Msvm_ComputerSystem WHERE ElementName='{VmName}'").First()["Name"];
    await Watch(watchScope, id);
    return;
  }
  string esd = Directory.GetFiles(Path.Combine(root, "..", "cache"), "*.esd").Single();
  string media = Path.Combine(root, "midia");
  // ISO e disco ficam numa pasta do sistema: no 1º teste o Hyper-V disse "anexo não encontrado" com a ISO no AppData.
  string vmDir = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DEBLOAT-VM")).FullName;
  string iso = Path.Combine(vmDir, "teste.iso");
  string vhd = Path.Combine(vmDir, "disco.vhdx");

  Log("Removendo VM antiga, se houver");
  await Ps($"if( Get-VM -Name '{VmName}' -ErrorAction SilentlyContinue ) {{ Stop-VM -Name '{VmName}' -TurnOff -Force; Remove-VM -Name '{VmName}' -Force }}; Remove-Item -LiteralPath '{vhd}' -ErrorAction SilentlyContinue");

  // --senha: só para a VM de teste (o PowerShell Direct recusa conta sem senha); o preset de verdade não tem senha.
  int passwordAt = Array.IndexOf(args, "--senha");
  var options = new DebloatOptions { WipeDisk0 = true, Password = passwordAt >= 0 ? args[passwordAt + 1] : "" };
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
  Log("Baixando os instaladores dos apps para a mídia");
  var appCatalog = AppCatalog.Load();
  var presetApps = appCatalog.Resolve(appCatalog.Defaults.Select(a => a.Id));
  using (var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
  {
    var offline = await OfflineInstallers.PrepareAsync(http, presetApps, Path.Combine(root, "..", "cache", "apps"), media,
      new Progress<OfflineProgress>(p => { if (p.State != OfflineState.Downloading) Log($"  {p.Id}: {p.State} {p.Note}"); }));
    Log($"  {offline.Count} de {presetApps.Count} instaladores na mídia");
  }
  // --painel-exe <DEBLOAT.exe publicado>: vai na mídia, como o app faz, para o primeiro login abrir o painel.
  int panelAt = Array.IndexOf(args, "--painel-exe");
  if (panelAt >= 0)
  {
    Directory.CreateDirectory(Path.Combine(media, "DEBLOAT"));
    File.Copy(args[panelAt + 1], Path.Combine(media, "DEBLOAT", "DEBLOAT.exe"), overwrite: true);
    Log("DEBLOAT.exe (painel) na mídia");
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

  await Watch(scope, vmId);
}
catch (Exception e)
{
  Log("ERRO: " + e);
}

/// <summary>Salva um print da tela da VM (e copia para atual.png). Devolve o problema, ou null se deu certo.</summary>
string? Shot(ManagementScope scope, ManagementObject service, string vmId, string file)
{
  try
  {
    var settings = Query(scope, $"SELECT * FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemIdentifier='{vmId}' AND VirtualSystemType='Microsoft:Hyper-V:System:Realized'").First();
    var input = service.GetMethodParameters("GetVirtualSystemThumbnailImage");
    input["TargetSystem"] = settings.Path.Path;
    input["WidthPixels"] = (ushort)1024;     // UInt16: com int o Hyper-V devolvia vazio
    input["HeightPixels"] = (ushort)768;
    var output = service.InvokeMethod("GetVirtualSystemThumbnailImage", input, null);
    object? raw = output["ImageData"];
    if (raw is not byte[] data || data.Length < 1024 * 768 * 2)
    {
      return $"print não veio: ReturnValue={output["ReturnValue"]}, tipo={raw?.GetType().Name ?? "null"}, tamanho={(raw as Array)?.Length}";
    }
    SaveRgb565(data, 1024, 768, file);
    File.Copy(file, Path.Combine(shots, "atual.png"), overwrite: true);
    return null;
  }
  catch (Exception e)
  {
    return "erro no print: " + e.Message;
  }
}

async Task Watch(ManagementScope scope, string vmId)
{
  var service = Query(scope, "SELECT * FROM Msvm_VirtualSystemManagementService").First();
  var clock = Stopwatch.StartNew();
  int n = 0;
  string? lastProblem = null;
  while (clock.Elapsed < TimeSpan.FromMinutes(minutes))
  {
    string? problem = Shot(scope, service, vmId, Path.Combine(shots, $"{n:D4}-{DateTime.Now:HHmmss}.png"));
    if (problem is null) n++;
    else if (problem != lastProblem) Log(lastProblem = problem);
    var state = Query(scope, $"SELECT EnabledState FROM Msvm_ComputerSystem WHERE Name='{vmId}'").First()["EnabledState"];
    if (Convert.ToInt32(state) == 3) { Log("VM desligou"); break; }     // 3 = desligada
    await Task.Delay(15000);
  }
  Log($"Fim do acompanhamento ({clock.Elapsed.TotalMinutes:F0} min). A VM continua no Hyper-V para inspeção.");
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
