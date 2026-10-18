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
        return target;
    }

    /// <summary>name.ext, bei Kollision "name (1).ext", "name (2).ext", ...</summary>
    public static string UniquePath(string source, string extension)
    {
        var dir = Path.GetDirectoryName(source) ?? ".";
        var name = Path.GetFileNameWithoutExtension(source);
        var candidate = Path.Combine(dir, name + extension);
        for (var i = 1; File.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{name} ({i}){extension}");
        return candidate;
    }
}
