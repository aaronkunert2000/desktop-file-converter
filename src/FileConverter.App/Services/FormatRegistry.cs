using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileConverter.Services;

/// <summary>Ein Segment im Ring. Position ist pro Quelltyp fest (Muskelgedächtnis), nicht verfügbare Ziele bleiben als graues Segment stehen.</summary>
public sealed record FormatTarget(string Label, string Extension, bool Enabled);

/// <summary>Welche Dateitypen in welche Ziele konvertierbar sind. Spike: nur Bilder.</summary>
public static class FormatRegistry
{
    private static readonly HashSet<string> ImageInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".gif", ".heic", ".ico",
    };

    // Feste Slot-Reihenfolge im Ring (im Uhrzeigersinn ab 12 Uhr)
    private static readonly string[] ImageOutputs = { ".png", ".jpg", ".webp", ".gif", ".bmp", ".tiff" };

    public static string Normalize(string ext)
    {
        ext = ext.ToLowerInvariant();
        return ext switch
        {
            ".jpeg" => ".jpg",
            ".tif" => ".tiff",
            _ => ext,
        };
    }

    /// <summary>
    /// Ziele für alle Dateien oder null, wenn die Auswahl nicht unterstützt wird
    /// (gemischte Typfamilien, Ordner, unbekannte Endungen).
    /// </summary>
    public static IReadOnlyList<FormatTarget>? TargetsFor(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return null;
        if (files.Any(f => !File.Exists(f) || !ImageInputs.Contains(Path.GetExtension(f)))) return null;

        var sourceExts = files.Select(f => Normalize(Path.GetExtension(f))).Distinct().ToList();
        var onlyOneKind = sourceExts.Count == 1 ? sourceExts[0] : null;

        return ImageOutputs
            .Select(ext => new FormatTarget(ext.TrimStart('.').ToUpperInvariant(), ext, Enabled: ext != onlyOneKind))
            .ToList();
    }
}
