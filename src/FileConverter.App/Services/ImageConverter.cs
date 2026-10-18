using System;
using System.IO;
using ImageMagick;

namespace FileConverter.Services;

/// <summary>Bildkonvertierung über Magick.NET. Ergebnis landet neben der Originaldatei.</summary>
public static class ImageConverter
{
    /// <returns>Pfad der neuen Datei.</returns>
    public static string Convert(string source, string targetExtension)
    {
        var target = UniquePath(source, targetExtension);

        using var image = new MagickImage(source);   // Spike: bei Mehrbild-Dateien (GIF) nur das erste Bild
        image.AutoOrient();
        Save(image, target, targetExtension);
        return target;
    }

    /// <summary>Wie <see cref="Convert"/>, aber für Bilddaten im Speicher (z. B. gerenderte PDF-Seite).</summary>
    public static void ConvertBytes(byte[] data, string targetPath, string targetExtension)
    {
        using var image = new MagickImage(data);
        Save(image, targetPath, targetExtension);
    }

    private static void Save(MagickImage image, string target, string targetExtension)
    {
        MagickFormat format;
        switch (targetExtension)
        {
            case ".jpg":
                // JPG hat keine Transparenz: auf Weiß flachrechnen
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaOption.Remove);
                image.Quality = 90;
                format = MagickFormat.Jpeg;
                break;
            case ".webp":
                image.Quality = 90;
                format = MagickFormat.WebP;
                break;
            case ".png": format = MagickFormat.Png; break;
            case ".gif": format = MagickFormat.Gif; break;
            case ".bmp": format = MagickFormat.Bmp; break;
            case ".tiff": format = MagickFormat.Tiff; break;
            default: throw new NotSupportedException($"Zielformat nicht unterstützt: {targetExtension}");
        }

        image.Write(target, format);
    }

    /// <summary>name.ext, bei Kollision "name (1).ext", "name (2).ext", ...</summary>
    public static string UniquePath(string source, string extension) =>
        UniquePath(Path.GetDirectoryName(source) ?? ".", Path.GetFileNameWithoutExtension(source), extension);

    public static string UniquePath(string directory, string name, string extension)
    {
        var candidate = Path.Combine(directory, name + extension);
        for (var i = 1; File.Exists(candidate); i++)
            candidate = Path.Combine(directory, $"{name} ({i}){extension}");
        return candidate;
    }
}
