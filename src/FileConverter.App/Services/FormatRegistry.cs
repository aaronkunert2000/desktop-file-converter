using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileConverter.Services;

/// <summary>Ein Segment im Ring. Position ist pro Quelltyp fest (Muskelgedächtnis), nicht verfügbare Ziele bleiben als graues Segment stehen.</summary>
public sealed record FormatTarget(string Label, string Extension, bool Enabled);

/// <summary>Welche Dateitypen in welche Ziele konvertierbar sind. Typfamilien: Bilder, Dokumente.</summary>
public static class FormatRegistry
{
    private static readonly HashSet<string> ImageInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".gif", ".heic", ".ico",
    };

    // Feste Slot-Reihenfolge im Ring (im Uhrzeigersinn ab 12 Uhr)
    private static readonly string[] ImageOutputs = { ".png", ".jpg", ".webp", ".gif", ".bmp", ".tiff" };
    private static readonly string[] DocumentOutputs = { ".pdf", ".docx", ".md", ".html", ".txt" };

    // Quelle (normalisiert) -> mögliche Ziele
    private static readonly Dictionary<string, HashSet<string>> DocumentMatrix = new(StringComparer.OrdinalIgnoreCase)
    {
        [".md"] = new() { ".pdf", ".docx", ".html", ".txt" },
        [".txt"] = new() { ".pdf", ".docx", ".md", ".html" },
        [".html"] = new() { ".pdf", ".md", ".txt" },
        [".docx"] = new() { ".pdf", ".md", ".html", ".txt" },
    };

    // Reine Textdateien werden wie .txt behandelt (Quelle für PDF, DOCX, MD, HTML)
    private static readonly HashSet<string> PlainTextInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".log", ".csv", ".tsv", ".json", ".xml", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".conf", ".env",
        ".py", ".js", ".ts", ".cs", ".java", ".c", ".cpp", ".h", ".css", ".sql", ".ps1", ".bat", ".cmd", ".sh",
    };

    public static string Normalize(string ext)
    {
        ext = ext.ToLowerInvariant();
        if (PlainTextInputs.Contains(ext)) return ".txt";
        return ext switch
        {
            ".jpeg" => ".jpg",
            ".tif" => ".tiff",
            ".markdown" => ".md",
            ".htm" => ".html",
            _ => ext,
        };
    }

    public static bool IsImage(string path) => ImageInputs.Contains(Path.GetExtension(path));

    public static bool IsDocument(string path) => DocumentMatrix.ContainsKey(Normalize(Path.GetExtension(path)));

    /// <summary>
    /// Ziele für alle Dateien oder null, wenn die Auswahl nicht unterstützt wird
    /// (gemischte Typfamilien, Ordner, unbekannte Endungen).
    /// </summary>
    public static IReadOnlyList<FormatTarget>? TargetsFor(IReadOnlyList<string> files)
    {
        if (files.Count == 0 || files.Any(f => !File.Exists(f))) return null;

        var sourceExts = files.Select(f => Normalize(Path.GetExtension(f))).Distinct().ToList();

        if (files.All(IsImage))
        {
            var only = sourceExts.Count == 1 ? sourceExts[0] : null;
            return ImageOutputs.Select(ext => Target(ext, ext != only)).ToList();
        }

        if (files.All(IsDocument))
        {
            return DocumentOutputs
                .Select(ext => Target(ext, sourceExts.All(src => DocumentMatrix[src].Contains(ext))))
                .ToList();
        }

        return null;
    }

    private static FormatTarget Target(string ext, bool enabled) =>
        new(ext.TrimStart('.').ToUpperInvariant(), ext, enabled);
}
