using System.Reflection;

namespace Debloat.Core;

/// <summary>Lê os arquivos embutidos em Data\ e Scripts\.</summary>
public static class Resources
{
  public static string Data(string fileName) => Read($"Debloat.Data.{fileName}");

  public static string Script(string fileName) => Read($"Debloat.Scripts.{fileName}");

  private static string Read(string name)
  {
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
      ?? throw new FileNotFoundException($"Recurso embutido '{name}' não existe.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
  }
}
