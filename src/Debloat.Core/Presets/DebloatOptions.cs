namespace Debloat.Core.Presets;

public enum DnsChoice { Provider, Cloudflare, CloudflareFamily, AdGuard, Google, Quad9 }

/// <summary>
/// Tudo que o usuário escolhe na janela. Sem mexer em nada, isto É o preset Recomendado.
/// Tweaks/RemovedApps nulos = valores do preset para o hardware (TweakCatalog).
/// </summary>
public record DebloatOptions
{
  // Conta e região
  public string UserName { get; init; } = "Usuario";
  public string Password { get; init; } = "";              // decisão do usuário: sem senha
  public string Language { get; init; } = "pt-BR";          // idioma da imagem baixada
  public string Locale { get; init; } = "pt-BR";            // formatos (data, moeda)
  public string Keyboard { get; init; } = "00010416";       // ABNT2
  public string GeoId { get; init; } = "32";                // Brasil
  public string TimeZone { get; init; } = "E. South America Standard Time";
  public string Edition { get; init; } = "pro";             // chave genérica do Pro

  /// <summary>Apaga o disco 0 e instala sem perguntar. Só para máquina virtual/teste: o padrão é escolher o disco na tela.</summary>
  public bool WipeDisk0 { get; init; }

  public HardwareProfile Hardware { get; init; } = HardwareProfile.Desktop;

  public IReadOnlySet<string>? Tweaks { get; init; }
  public IReadOnlySet<string>? RemovedApps { get; init; }

  public DnsChoice Dns { get; init; } = DnsChoice.Cloudflare;
  public IReadOnlyList<string>? SelectedApps { get; init; }   // null = padrões do catálogo

  /// <summary>Programas deste PC que o winget conhece (aba Backup): instalados no Windows novo como os do catálogo.</summary>
  public IReadOnlyList<Catalog.ExtraApp> ExtraApps { get; init; } = [];

  public IReadOnlySet<string> EffectiveTweaks => Tweaks ?? TweakCatalog.DefaultsFor(Hardware);

  public bool Has(string tweak) => EffectiveTweaks.Contains(tweak);

  public IReadOnlySet<string> EffectiveRemovedApps =>
    RemovedApps ?? TweakCatalog.Bloatware.Where(b => b.Default(Hardware)).Select(b => b.Id).ToHashSet();
}
