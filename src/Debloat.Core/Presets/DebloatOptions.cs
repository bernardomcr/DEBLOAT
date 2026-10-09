namespace Debloat.Core.Presets;

public enum DnsChoice { Provider, Cloudflare, CloudflareFamily, AdGuard, Google, Quad9 }

/// <summary>
/// Tudo que o usuário escolhe na janela. Os valores padrão SÃO o preset "Recomendado":
/// quem só clica em "Criar" recebe exatamente isto.
/// </summary>
public record DebloatOptions
{
  // Conta e região
  public string UserName { get; init; } = "Usuario";
  public string Password { get; init; } = "";              // decisão do usuário: sem senha
  public string Language { get; init; } = "pt-BR";
  public string Keyboard { get; init; } = "00010416";       // ABNT2
  public string GeoId { get; init; } = "32";                // Brasil
  public string TimeZone { get; init; } = "E. South America Standard Time";
  public string Edition { get; init; } = "pro";             // chave genérica do Pro

  // Hardware do PC (decide Hello, caneta, hibernação, Localizar Dispositivo)
  public HardwareProfile Hardware { get; init; } = HardwareProfile.Desktop;

  // Aparência
  public bool DarkMode { get; init; } = true;
  public bool LeftTaskbar { get; init; } = true;
  public bool ClassicContextMenu { get; init; } = true;
  public bool ClassicPhotoViewer { get; init; } = true;

  // Rede e apps
  public DnsChoice Dns { get; init; } = DnsChoice.Cloudflare;
  public IReadOnlyList<string>? SelectedApps { get; init; }   // null = padrões do catálogo

  /// <summary>Bloatware removido sempre (decidido na revisão do XML do 1155).</summary>
  public static readonly IReadOnlyList<string> AlwaysRemoved =
  [
    "Remove3DViewer", "RemoveBingSearch", "RemoveClipchamp", "RemoveCopilot", "RemoveCortana",
    "RemoveDevHome", "RemoveFamily", "RemoveFeedbackHub", "RemoveGetHelp", "RemoveMailCalendar",
    "RemoveMaps", "RemoveMixedReality", "RemoveZuneVideo", "RemoveNews", "RemoveOffice365",
    "RemoveOneDrive", "RemoveOneNote", "RemoveOneSync", "RemoveOutlook", "RemovePaint3D",
    "RemovePeople", "RemovePowerAutomate", "RemoveQuickAssist", "RemoveRecall", "RemoveSkype",
    "RemoveSolitaire", "RemoveStepsRecorder", "RemoveTeams", "RemoveGetStarted", "RemoveToDo",
    "RemoveWallet", "RemoveWeather", "RemoveYourPhone",
  ];

  /// <summary>Lista final de remoção, ajustada ao hardware e às escolhas.</summary>
  public IReadOnlyList<string> Bloatware
  {
    get
    {
      var list = new List<string>(AlwaysRemoved);
      if (ClassicPhotoViewer) list.Add("RemovePhotos");
      if (!Hardware.HasIrCamera) list.Add("RemoveWindowsHello");
      if (!Hardware.HasPenOrTouch) list.AddRange(["RemoveHandwriting", "RemoveMathInputPanel"]);
      // Mantidos de propósito: Bloco de Notas, Media Player (ZuneMusic), Fala/Narrador,
      // Conexão de Área de Trabalho Remota, Xbox, Loja, Calculadora, Ferramenta de Captura.
      return list;
    }
  }
}
