using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FileConverter.Services;

/// <summary>PDF-Seiten als PNG/JPG über die in Windows eingebaute PDF-Engine (Windows.Data.Pdf).</summary>
internal static class PdfRasterizer
{
    private const double Scale = 2.0;   // Seitenbreite in 96-dpi-Einheiten x 2 = ca. 192 dpi

    /// <returns>Pfade der erzeugten Dateien (eine pro Seite).</returns>
    public static async Task<IReadOnlyList<string>> RenderAsync(string source, string targetExtension)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source));
        var document = await PdfDocument.LoadFromFileAsync(file);

        var directory = Path.GetDirectoryName(source) ?? ".";
        var name = Path.GetFileNameWithoutExtension(source);
        var outputs = new List<string>();

        for (uint index = 0; index < document.PageCount; index++)
        {
            using var page = document.GetPage(index);
            using var stream = new InMemoryRandomAccessStream();

            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Round(page.Size.Width * Scale),
                BackgroundColor = Windows.UI.Colors.White,
            };
            await page.RenderToStreamAsync(stream, options);

            stream.Seek(0);
            using var memory = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(memory);

            var pageName = document.PageCount > 1 ? $"{name}_s{index + 1}" : name;
            var target = ImageConverter.UniquePath(directory, pageName, targetExtension);
            ImageConverter.ConvertBytes(memory.ToArray(), target, targetExtension);
            outputs.Add(target);
        }

        return outputs;
    }
}
