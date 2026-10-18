using System.IO;
using System.Threading.Tasks;

namespace FileConverter.Services;

/// <summary>Ergebnis einer Konvertierung: erste erzeugte Datei und Anzahl (mehrseitige PDFs ergeben mehrere Bilder).</summary>
public sealed record ConversionOutput(string Path, int Files);

/// <summary>Wählt anhand der Quelldatei den passenden Konverter.</summary>
public static class ConverterRegistry
{
    /// <remarks>Muss vom UI-Thread aus aufgerufen werden (PDF-Ausgabe braucht WebView2, PDF-Seiten brauchen WinRT).</remarks>
    public static async Task<ConversionOutput> ConvertAsync(string source, string targetExtension)
    {
        if (FormatRegistry.IsImage(source))
            return new ConversionOutput(await Task.Run(() => ImageConverter.Convert(source, targetExtension)), 1);

        if (FormatRegistry.IsPdf(source) && targetExtension is (".png" or ".jpg"))
        {
            var pages = await PdfRasterizer.RenderAsync(source, targetExtension);
            return new ConversionOutput(pages[0], pages.Count);
        }

        var src = FormatRegistry.Normalize(System.IO.Path.GetExtension(source));
        return new ConversionOutput(await DocumentConverter.ConvertAsync(source, src, targetExtension), 1);
    }
}
