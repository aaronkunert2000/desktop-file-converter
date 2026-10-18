using System.IO;
using System.Threading.Tasks;

namespace FileConverter.Services;

/// <summary>Wählt anhand der Quelldatei den passenden Konverter.</summary>
public static class ConverterRegistry
{
    /// <returns>Pfad der neuen Datei. Muss vom UI-Thread aus aufgerufen werden (PDF-Ausgabe braucht WebView2).</returns>
    public static async Task<string> ConvertAsync(string source, string targetExtension)
    {
        if (FormatRegistry.IsImage(source))
            return await Task.Run(() => ImageConverter.Convert(source, targetExtension));

        var src = FormatRegistry.Normalize(Path.GetExtension(source));
        return await DocumentConverter.ConvertAsync(source, src, targetExtension);
    }
}
