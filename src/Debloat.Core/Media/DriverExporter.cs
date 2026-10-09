using System.Text.Json;

namespace Debloat.Core.Media;

public record ExportedDriver(string ClassName, string Provider, string Inf, string Device);

/// <summary>
/// Copia do PC atual SÓ os drivers que evitam ficar sem rede/disco/USB depois de formatar, para a pasta
/// $WinPEDriver$ do pendrive (o Setup carrega sozinho). Regras:
/// - só drivers de terceiros (oem*.inf) em uso por hardware físico presente (PCI, USB, ACPI);
///   drivers de dispositivos virtuais criados por programas (ROOT\, SWD\: Parsec, VPNs, ViGEm, Xbox) ficam fora;
/// - vídeo nunca vai: é a "limpeza profunda" — o Windows novo parte sem resto do driver antigo.
/// </summary>
public static class DriverExporter
{
  /// <summary>Classes levadas. Net = rede/Wi-Fi; SCSIAdapter/HDC = NVMe, RAID, SATA; USB = controladoras; System = chipset.</summary>
  public static readonly IReadOnlyList<string> Classes = ["Net", "SCSIAdapter", "HDC", "USB", "System"];

  /// <summary>Barramentos de hardware real.</summary>
  public static readonly IReadOnlyList<string> PhysicalBuses = ["PCI\\", "USB\\", "ACPI\\"];

  public const string FolderName = "$WinPEDriver$";

  public static async Task<IReadOnlyList<ExportedDriver>> ExportAsync(string destinationRoot, CancellationToken ct = default)
  {
    string dest = Path.Combine(destinationRoot, FolderName);
    string classes = string.Join(",", Classes.Select(PowerShell.Quote));
    string buses = string.Join(" -or ", PhysicalBuses.Select(b => $"$_.InstanceId.StartsWith({PowerShell.Quote(b)})"));
    string script = $$"""
      $dest = {{PowerShell.Quote(dest)}}
      if( Test-Path -LiteralPath $dest ) { Remove-Item -LiteralPath $dest -Recurse -Force }
      New-Item -ItemType Directory -Force -Path $dest | Out-Null
      $store = @{}
      Get-WindowsDriver -Online | ForEach-Object { $store[$_.Driver.ToLowerInvariant()] = $_ }
      $seen = @{}
      # Da classe USB só entram controladoras (PCI\); aparelhos ligados nelas (webcam, receptor) não fazem falta na instalação.
      Get-PnpDevice -PresentOnly | Where-Object { ($_.Class -in @({{classes}})) -and ({{buses}}) -and -not ($_.Class -eq 'USB' -and -not $_.InstanceId.StartsWith('PCI\')) } | ForEach-Object {
        $inf = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName 'DEVPKEY_Device_DriverInfPath' -ErrorAction SilentlyContinue).Data
        if( -not $inf -or -not $inf.StartsWith('oem') -or $seen.ContainsKey($inf) ) { return }
        $pkg = $store[$inf.ToLowerInvariant()]
        if( -not $pkg ) { return }
        $seen[$inf] = $true
        $folder = Split-Path -Parent $pkg.OriginalFileName
        Copy-Item -LiteralPath $folder -Destination (Join-Path $dest ($_.Class + '\' + (Split-Path -Leaf $folder))) -Recurse -Force
        [pscustomobject]@{ ClassName = [string]$_.Class; Provider = [string]$pkg.ProviderName; Inf = [string](Split-Path -Leaf $pkg.OriginalFileName); Device = [string]$_.FriendlyName }
      } | ConvertTo-Json -Compress
      """;
    string json = (await PowerShell.RunAsync(script, ct)).Trim();
    if (json.Length == 0) return [];
    if (!json.StartsWith('[')) json = $"[{json}]";
    return JsonSerializer.Deserialize<List<ExportedDriver>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
  }
}
