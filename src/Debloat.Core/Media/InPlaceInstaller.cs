using System.Text.RegularExpressions;

namespace Debloat.Core.Media;

/// <summary>
/// Modo "sem pendrive": cria uma partição temporária DEBLOAT-SETUP (encolhendo o C:), copia a instalação para
/// ela, prepara um WinPE com o nosso script e agenda UM boot nele. Fluxo completo em PLAN.md.
/// A partição temporária é apagada no fim do primeiro login (FirstLogon.ps1), depois dos apps — que vêm dela.
/// NÃO TESTADO EM VM AINDA — a janela não expõe este modo até o teste.
/// </summary>
public static partial class InPlaceInstaller
{
  public const string SetupLabel = "DEBLOAT-SETUP";
  public const string BootDescription = "DEBLOAT - reinstalar o Windows";

  /// <summary>Folga além do tamanho da mídia: WinPE, drivers, log.</summary>
  public const long Slack = 2L << 30;

  public record Plan(long MediaSize, long PartitionSize, long FreeOnC, bool Encrypted, bool Uefi);

  public static async Task<Plan> CheckAsync(string mediaDir, CancellationToken ct = default)
  {
    long media = Directory.EnumerateFiles(mediaDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    string json = (await PowerShell.RunAsync("""
      $c = Get-Partition -DriveLetter $env:SystemDrive[0]
      $s = $c | Get-PartitionSupportedSize
      $bl = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction SilentlyContinue
      # Qualquer coisa diferente de "totalmente descriptografado" (inclusive a criptografia automática do Windows 11).
      $encrypted = [int]($bl -and $bl.VolumeStatus -ne 'FullyDecrypted')
      "{0}|{1}|{2}|{3}" -f ($c.Size - $s.SizeMin), $encrypted, $env:firmware_type, $c.DiskNumber
      """, ct)).Trim();
    string[] p = json.Split('|');
    return new Plan(media, media + Slack, long.Parse(p[0]), p[1] == "1", p[2] == "UEFI");
  }

  public static async Task PrepareAsync(string mediaDir, IProgress<WriteStep>? progress = null, CancellationToken ct = default)
  {
    if (!File.Exists(Path.Combine(mediaDir, "boot", "boot.sdi")) || !File.Exists(Path.Combine(mediaDir, "sources", "boot.wim")))
    {
      throw new InvalidOperationException("A instalação montada está incompleta (boot.sdi/boot.wim).");
    }
    // Drivers de rede, disco e chipset DESTE PC: sem eles o WinPE pode nem ver o SSD (Intel VMD/RST).
    if (!Directory.Exists(Path.Combine(mediaDir, DriverExporter.FolderName)))
    {
      progress?.Report(new("Copiando drivers de rede, disco e chipset", 0.01));
      await DriverExporter.ExportAsync(mediaDir, ct);
    }
    var plan = await CheckAsync(mediaDir, ct);
    if (!plan.Uefi) throw new InvalidOperationException("O modo sem pendrive só funciona em PCs com UEFI. Use o pendrive.");
    // Com o disco criptografado o WinPE não lê o C: (não acharia o Windows antigo). Melhor avisar agora.
    if (plan.Encrypted) throw new InvalidOperationException("O disco C: está criptografado (BitLocker/criptografia do dispositivo). Desligue a criptografia ou use o pendrive.");
    if (plan.FreeOnC < plan.PartitionSize + (10L << 30)) throw new InvalidOperationException("Pouco espaço livre no C: para a partição temporária.");

    string token = System.Guid.NewGuid().ToString("N");
    progress?.Report(new("Criando a partição temporária", 0.02));
    // Marcador no C: atual: o WinPE só formata a partição que tem este token.
    await File.WriteAllTextAsync(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "DEBLOAT-ALVO.txt"), token, ct);
    char s = (await PowerShell.RunAsync($$"""
      $c = Get-Partition -DriveLetter $env:SystemDrive[0]
      # Sobra de uma tentativa anterior: apaga e devolve o espaço ao C: antes de encolher de novo.
      $old = Get-Volume -FileSystemLabel '{{SetupLabel}}' -ErrorAction SilentlyContinue | Get-Partition -ErrorAction SilentlyContinue | Where-Object DiskNumber -eq $c.DiskNumber
      if( $old ) {
        $old | Remove-Partition -Confirm:$false
        $max = ($c | Get-PartitionSupportedSize).SizeMax
        if( $max -gt $c.Size ) { $c | Resize-Partition -Size $max }
        $c = Get-Partition -DriveLetter $env:SystemDrive[0]
      }
      # 64 MB de folga: com o alinhamento do disco, o espaço livre sai um pouco menor que o encolhido.
      $c | Resize-Partition -Size ($c.Size - {{plan.PartitionSize}} - 64MB)
      $p = $null
      try {
        $p = New-Partition -DiskNumber $c.DiskNumber -Size {{plan.PartitionSize}} -AssignDriveLetter
        Format-Volume -Partition $p -FileSystem NTFS -NewFileSystemLabel '{{SetupLabel}}' -Confirm:$false | Out-Null
      } catch {
        # Deu errado: devolve o espaço ao C: antes de avisar (o C: não pode ficar encolhido à toa).
        if( $p ) { $p | Remove-Partition -Confirm:$false -ErrorAction SilentlyContinue }
        $c = Get-Partition -DriveLetter $env:SystemDrive[0]
        $max = ($c | Get-PartitionSupportedSize).SizeMax
        if( $max -gt $c.Size ) { $c | Resize-Partition -Size $max -ErrorAction SilentlyContinue }
        throw
      }
      (Get-Partition -DiskNumber $c.DiskNumber -PartitionNumber $p.PartitionNumber).DriveLetter
      """, ct)).Trim()[0];
    string root = $"{s}:\\";
    try
    {
      progress?.Report(new("Copiando a instalação para a partição temporária", 0.05));
      await UsbWriter.CopyTreeAsync(mediaDir, root, plan.MediaSize, f => progress?.Report(new("Copiando a instalação para a partição temporária", 0.05 + f * 0.6)), ct);

      string debloat = Directory.CreateDirectory(Path.Combine(root, "debloat")).FullName;
      await File.WriteAllTextAsync(Path.Combine(debloat, "alvo.txt"), token, ct);

      progress?.Report(new("Preparando o ambiente de instalação (WinPE)", 0.7));
      await BuildWinPeAsync(root, ct);

      progress?.Report(new("Agendando o boot de instalação", 0.9));
      await AddBootEntryAsync(s, ct);
    }
    catch
    {
      // Deu errado antes de reiniciar: o PC volta exatamente como estava (sem partição, sem entrada de boot).
      progress?.Report(new("Desfazendo: devolvendo o espaço ao C:", 0.95));
      await UndoAsync(CancellationToken.None);
      throw;
    }
    progress?.Report(new("Pronto: reinicie para formatar e instalar", 1));
  }

  /// <summary>Apaga as entradas de boot DEBLOAT, a partição temporária e o marcador, e devolve o espaço ao C:.</summary>
  public static async Task UndoAsync(CancellationToken ct = default)
  {
    await DeleteBootEntriesAsync(ct);
    await PowerShell.RunAsync($$"""
      $c = Get-Partition -DriveLetter $env:SystemDrive[0]
      Get-Volume -FileSystemLabel '{{SetupLabel}}' -ErrorAction SilentlyContinue | Get-Partition -ErrorAction SilentlyContinue |
        Where-Object DiskNumber -eq $c.DiskNumber | Remove-Partition -Confirm:$false
      $c = Get-Partition -DriveLetter $env:SystemDrive[0]
      $max = ($c | Get-PartitionSupportedSize).SizeMax
      if( $max -gt $c.Size ) { $c | Resize-Partition -Size $max }
      Remove-Item -LiteralPath "$env:SystemDrive\DEBLOAT-ALVO.txt" -ErrorAction SilentlyContinue
      """, ct);
  }

  /// <summary>Entradas com descrição começando por DEBLOAT (de uma tentativa anterior ou desta).</summary>
  private static async Task DeleteBootEntriesAsync(CancellationToken ct)
  {
    string? id = null;
    foreach (string line in (await Bcd("/enum all /v", ct)).Split('\n', StringSplitOptions.TrimEntries))
    {
      if (IdentifierLineRegex().Match(line) is { Success: true } m) id = m.Groups[1].Value;
      else if (id is not null && DescriptionLineRegex().IsMatch(line)) await Bcd($"/delete {id} /f", ct);
    }
  }

  /// <summary>WinPE puro (índice 1 do boot.wim) com o nosso script no lugar do Setup e os drivers de disco injetados.</summary>
  private static async Task BuildWinPeAsync(string root, CancellationToken ct)
  {
    string sourceWim = Path.Combine(root, "sources", "boot.wim");
    string wim = Path.Combine(root, "debloat", "winpe.wim");
    string mount = Directory.CreateTempSubdirectory("debloat-winpe-").FullName;
    await Dism($"/Export-Image /SourceImageFile:\"{sourceWim}\" /SourceIndex:1 /DestinationImageFile:\"{wim}\"", ct);
    await Dism($"/Mount-Wim /WimFile:\"{wim}\" /Index:1 /MountDir:\"{mount}\"", ct);
    bool commit = false;
    try
    {
      Directory.CreateDirectory(Path.Combine(mount, "debloat"));
      await File.WriteAllTextAsync(Path.Combine(mount, "debloat", "instalar.cmd"), Resources.Script("InPlace-instalar.cmd").Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"), ct);
      await File.WriteAllTextAsync(Path.Combine(mount, "Windows", "System32", "winpeshl.ini"),
        "[LaunchApps]\r\n%SYSTEMROOT%\\System32\\cmd.exe, /c %SYSTEMDRIVE%\\debloat\\instalar.cmd\r\n", ct);
      string drivers = Path.Combine(root, DriverExporter.FolderName);
      if (Directory.Exists(drivers))
      {
        await Dism($"/Image:\"{mount}\" /Add-Driver /Driver:\"{drivers}\" /Recurse", ct, allowFailure: true);
      }
      commit = true;
    }
    finally
    {
      await Dism($"/Unmount-Wim /MountDir:\"{mount}\" /{(commit ? "Commit" : "Discard")}", ct, allowFailure: !commit);
      Directory.Delete(mount, recursive: true);
    }
  }

  /// <summary>Entrada de ramdisk de uso único (bootsequence): se o WinPE abortar, o próximo boot volta ao Windows atual.</summary>
  private static async Task AddBootEntryAsync(char s, CancellationToken ct)
  {
    await DeleteBootEntriesAsync(ct);
    string ramdisk = ParseGuid(await Bcd($"/create /d \"DEBLOAT ramdisk\" /device", ct));
    await Bcd($"/set {ramdisk} ramdisksdidevice partition={s}:", ct);
    await Bcd($"/set {ramdisk} ramdisksdipath \\boot\\boot.sdi", ct);
    string entry = ParseGuid(await Bcd($"/create /d \"{BootDescription}\" /application osloader", ct));
    string device = $"ramdisk=[{s}:]\\debloat\\winpe.wim,{ramdisk}";
    await Bcd($"/set {entry} device {device}", ct);
    await Bcd($"/set {entry} osdevice {device}", ct);
    await Bcd($"/set {entry} path \\windows\\system32\\boot\\winload.efi", ct);
    await Bcd($"/set {entry} systemroot \\windows", ct);
    await Bcd($"/set {entry} winpe yes", ct);
    await Bcd($"/set {entry} detecthal yes", ct);
    await Bcd($"/bootsequence {entry}", ct);
  }

  private static string ParseGuid(string bcdOutput) =>
    GuidRegex().Match(bcdOutput) is { Success: true } m ? m.Value : throw new InvalidOperationException("bcdedit não devolveu um identificador: " + bcdOutput);

  /// <summary>bcdedit direto, sem o PowerShell: lá "{guid}" vira bloco de código e "a,b" vira lista (VM, 10/10/2026).</summary>
  private static async Task<string> Bcd(string args, CancellationToken ct)
  {
    var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "bcdedit.exe"), args)
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("bcdedit não abriu.");
    var stdout = p.StandardOutput.ReadToEndAsync(ct);
    var stderr = p.StandardError.ReadToEndAsync(ct);
    await p.WaitForExitAsync(ct);
    string output = await stdout + await stderr;
    if (p.ExitCode != 0) throw new InvalidOperationException($"bcdedit {args}: {output.Trim()}");
    return output;
  }

  private static Task<string> Dism(string args, CancellationToken ct, bool allowFailure = false) =>
    PowerShell.RunAsync($"$o = & dism.exe /English {args} 2>&1 | Out-String; if( $LASTEXITCODE -ne 0 -and -not ${allowFailure.ToString().ToLowerInvariant()} ) {{ throw \"DISM: $o\" }}; $o", ct);

  [GeneratedRegex(@"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}")]
  private static partial Regex GuidRegex();

  [GeneratedRegex(@"^(?:identifier|identificador)\s+(\{[0-9a-fA-F-]{36}\})")]
  private static partial Regex IdentifierLineRegex();

  [GeneratedRegex(@"^descri\S*\s+DEBLOAT")]
  private static partial Regex DescriptionLineRegex();
}
