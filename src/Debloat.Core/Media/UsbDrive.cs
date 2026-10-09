using System.Text.Json;

namespace Debloat.Core.Media;

/// <summary>Um pendrive candidato. Só discos no barramento USB que não são de boot nem de sistema.</summary>
public record UsbDrive(int Number, string FriendlyName, string SerialNumber, long Size)
{
  public string Label => $"{FriendlyName.Trim()} — {Size / 1e9:F1} GB (disco {Number})";
}

public record WriteStep(string Text, double Fraction);

/// <summary>
/// Apaga e grava o pendrive. Regras de segurança (ver PLAN.md): só BusType USB, nunca disco de boot/sistema,
/// e o disco é conferido de novo (número + nome + série + tamanho) imediatamente antes de apagar.
/// </summary>
public static class UsbWriter
{
  /// <summary>O Windows não formata FAT32 acima de 32 GB (31 GiB dá folga); o resto do pendrive vira uma partição NTFS de dados.</summary>
  public const long BootPartitionMax = 31L * 1024 * 1024 * 1024;

  public const string BootLabel = "DEBLOAT";
  public const string DataLabel = "DEBLOAT-DADOS";

  private const string ListScript = """
    Get-Disk | Where-Object { $_.BusType -eq 'USB' -and -not $_.IsBoot -and -not $_.IsSystem -and $_.Size -gt 0 } |
      ForEach-Object { [pscustomobject]@{ Number = $_.Number; FriendlyName = [string]$_.FriendlyName; SerialNumber = [string]$_.SerialNumber; Size = [long]$_.Size } } |
      ConvertTo-Json -Compress
    """;

  public static async Task<IReadOnlyList<UsbDrive>> ListAsync(CancellationToken ct = default)
  {
    string json = (await PowerShell.RunAsync(ListScript, ct)).Trim();
    if (json.Length == 0) return [];
    if (!json.StartsWith('[')) json = $"[{json}]";     // PowerShell 5 devolve objeto solto quando há um só
    return JsonSerializer.Deserialize<List<UsbDrive>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
  }

  /// <summary>Tamanho da partição de boot: o pendrive inteiro até 32 GB.</summary>
  public static long BootPartitionSize(long diskSize) => Math.Min(diskSize, BootPartitionMax);

  /// <summary>Apaga o pendrive e grava a mídia. Devolve as letras (boot, dados ou null).</summary>
  public static async Task<(char Boot, char? Data)> WriteAsync(UsbDrive drive, string mediaDir,
    IProgress<WriteStep>? progress = null, CancellationToken ct = default)
  {
    long mediaSize = Directory.EnumerateFiles(mediaDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    long bootSize = BootPartitionSize(drive.Size);
    if (mediaSize > bootSize - (256L << 20)) throw new InvalidOperationException("O pendrive é pequeno demais para a instalação.");

    progress?.Report(new("Conferindo o pendrive", 0));
    var current = (await ListAsync(ct)).FirstOrDefault(d => d.Number == drive.Number);
    if (current is null || current.FriendlyName != drive.FriendlyName || current.SerialNumber != drive.SerialNumber || current.Size != drive.Size)
    {
      throw new InvalidOperationException("O pendrive mudou desde que foi escolhido. Escolha de novo.");
    }

    progress?.Report(new("Apagando e particionando o pendrive", 0.02));
    // MBR com partição FAT32 ativa: boota em UEFI (efi\boot\bootx64.efi) e em BIOS antiga (bootsect).
    string script = $$"""
      $n = {{drive.Number}}
      $d = Get-Disk -Number $n
      if( $d.BusType -ne 'USB' -or $d.IsBoot -or $d.IsSystem ) { throw 'Disco recusado: não é um pendrive USB.' }
      if( $d.IsOffline ) { Set-Disk -Number $n -IsOffline $false }
      if( $d.IsReadOnly ) { Set-Disk -Number $n -IsReadOnly $false }
      if( $d.PartitionStyle -ne 'RAW' ) { Clear-Disk -Number $n -RemoveData -RemoveOEM -Confirm:$false }
      if( (Get-Disk -Number $n).PartitionStyle -eq 'RAW' ) { Initialize-Disk -Number $n -PartitionStyle MBR }
      else { Set-Disk -Number $n -PartitionStyle MBR }
      $boot = New-Partition -DiskNumber $n -Size {{bootSize}} -IsActive -AssignDriveLetter
      Format-Volume -Partition $boot -FileSystem FAT32 -NewFileSystemLabel '{{BootLabel}}' -Confirm:$false | Out-Null
      $data = $null
      $free = (Get-Disk -Number $n).LargestFreeExtent
      if( $free -gt 1GB ) {
        $data = New-Partition -DiskNumber $n -UseMaximumSize -AssignDriveLetter
        Format-Volume -Partition $data -FileSystem NTFS -NewFileSystemLabel '{{DataLabel}}' -Confirm:$false | Out-Null
      }
      $b = (Get-Partition -DiskNumber $n -PartitionNumber $boot.PartitionNumber).DriveLetter
      $t = if( $data ) { (Get-Partition -DiskNumber $n -PartitionNumber $data.PartitionNumber).DriveLetter } else { '' }
      "$b|$t"
      """;
    string[] letters = (await PowerShell.RunAsync(script, ct)).Trim().Split('|');
    char boot = letters[0][0];
    char? data = letters.Length > 1 && letters[1].Length > 0 ? letters[1][0] : null;

    await CopyTreeAsync(mediaDir, $"{boot}:\\", mediaSize, p => progress?.Report(new("Copiando a instalação para o pendrive", 0.05 + p * 0.9)), ct);

    progress?.Report(new("Gravando o setor de boot", 0.96));
    string bootsect = Path.Combine(mediaDir, "boot", "bootsect.exe");
    if (File.Exists(bootsect))
    {
      await PowerShell.RunAsync($"& {PowerShell.Quote(bootsect)} /nt60 {boot}: /force /mbr | Out-Null; if( $LASTEXITCODE -ne 0 ) {{ throw 'bootsect falhou' }}", ct);
    }
    progress?.Report(new("Pendrive pronto", 1));
    return (boot, data);
  }

  private static async Task CopyTreeAsync(string from, string to, long total, Action<double> progress, CancellationToken ct)
  {
    long done = 0;
    byte[] buffer = new byte[4 << 20];
    foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
    {
      string target = Path.Combine(to, Path.GetRelativePath(from, file));
      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
      await using var dest = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
      int read;
      while ((read = await source.ReadAsync(buffer, ct)) > 0)
      {
        await dest.WriteAsync(buffer.AsMemory(0, read), ct);
        done += read;
        progress((double)done / total);
      }
    }
  }
}
