using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Debloat.Core.Media;

public record DownloadProgress(long Done, long Total, double BytesPerSecond)
{
  public double Fraction => Total > 0 ? (double)Done / Total : 0;
}

/// <summary>
/// Download em várias conexões (faixas HTTP), com retomada e verificação de hash.
/// Cada conexão escreve na sua faixa do arquivo; o progresso fica em "&lt;arquivo&gt;.partes".
/// </summary>
public sealed class SegmentedDownloader(HttpClient http, int connections = 8)
{
  private sealed class Segment
  {
    public long Start;
    public long End;       // inclusivo
    public long Done;      // atualizado com Interlocked pelas conexões

    public long Position => Start + Interlocked.Read(ref Done);
  }

  private static readonly JsonSerializerOptions StateJson = new() { IncludeFields = true };

  public async Task DownloadAsync(Uri url, string path, long size, string? sha256, string? sha1,
    IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
  {
    string statePath = path + ".partes";
    if (File.Exists(path) && !File.Exists(statePath) && new FileInfo(path).Length == size)
    {
      if (await VerifyAsync(path, sha256, sha1, ct)) return;     // já baixado e íntegro
      File.Delete(path);
    }

    var segments = LoadState(statePath, size) ?? Plan(size);
    await using (var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
    {
      file.SetLength(size);
    }

    var clock = Stopwatch.StartNew();
    long startDone = segments.Sum(s => s.Done);
    using var reporter = new CancellationTokenSource();
    var report = Task.Run(async () =>
    {
      while (!reporter.IsCancellationRequested)
      {
        await Task.Delay(250, CancellationToken.None);
        long done = segments.Sum(s => Interlocked.Read(ref s.Done));
        progress?.Report(new DownloadProgress(done, size, (done - startDone) / Math.Max(clock.Elapsed.TotalSeconds, 0.001)));
        SaveState(statePath, segments);
      }
    }, CancellationToken.None);

    try
    {
      await Task.WhenAll(segments.Where(s => s.Position <= s.End).Select(s => RunSegmentAsync(url, path, s, ct)));
    }
    finally
    {
      reporter.Cancel();
      await report;
      SaveState(statePath, segments);
    }

    progress?.Report(new DownloadProgress(size, size, 0));
    if (!await VerifyAsync(path, sha256, sha1, ct))
    {
      File.Delete(path);
      File.Delete(statePath);
      throw new InvalidDataException("O arquivo baixado não bate com o hash da Microsoft. Tente de novo.");
    }
    File.Delete(statePath);
  }

  private async Task RunSegmentAsync(Uri url, string path, Segment segment, CancellationToken ct)
  {
    for (int attempt = 1; ; attempt++)
    {
      try
      {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(segment.Position, segment.End);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
          throw new HttpRequestException($"O servidor não aceitou download por partes ({(int)response.StatusCode}).");
        }
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1 << 16, useAsync: true);
        file.Position = segment.Position;
        byte[] buffer = new byte[1 << 20];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
          int keep = (int)Math.Min(read, segment.End - (segment.Position) + 1);
          await file.WriteAsync(buffer.AsMemory(0, keep), ct);
          Interlocked.Add(ref segment.Done, keep);
          if (segment.Position > segment.End) return;
        }
        if (segment.Position > segment.End) return;
        throw new IOException("Conexão fechada antes do fim da parte.");
      }
      catch (Exception e) when (attempt < 6 && !ct.IsCancellationRequested && e is HttpRequestException or IOException)
      {
        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
      }
    }
  }

  private List<Segment> Plan(long size)
  {
    int n = (int)Math.Clamp(size / (32L << 20), 1, connections);   // partes de pelo menos 32 MB
    long chunk = size / n;
    return Enumerable.Range(0, n).Select(i => new Segment
    {
      Start = i * chunk,
      End = i == n - 1 ? size - 1 : (i + 1) * chunk - 1,
    }).ToList();
  }

  private static List<Segment>? LoadState(string statePath, long size)
  {
    try
    {
      if (!File.Exists(statePath)) return null;
      var segments = JsonSerializer.Deserialize<List<Segment>>(File.ReadAllText(statePath), StateJson);
      return segments is { Count: > 0 } && segments[^1].End == size - 1 ? segments : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private static void SaveState(string statePath, List<Segment> segments)
  {
    try
    {
      File.WriteAllText(statePath, JsonSerializer.Serialize(segments, StateJson));
    }
    catch (IOException)
    {
      // Na próxima volta grava de novo; perder um salvamento só faz rebaixar alguns MB.
    }
  }

  public static async Task<bool> VerifyAsync(string path, string? sha256, string? sha1, CancellationToken ct = default)
  {
    if (sha256 is null && sha1 is null) return true;
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
    byte[] hash = sha256 is not null ? await SHA256.HashDataAsync(stream, ct) : await SHA1.HashDataAsync(stream, ct);
    return Convert.ToHexString(hash).Equals(sha256 ?? sha1, StringComparison.OrdinalIgnoreCase);
  }
}
