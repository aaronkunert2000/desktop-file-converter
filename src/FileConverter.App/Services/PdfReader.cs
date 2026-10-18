using System;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FileConverter.Services;

/// <summary>Text aus PDF (PdfPig). Layout bleibt nicht erhalten; gescannte PDFs enthalten keinen Text.</summary>
internal static class PdfReader
{
    public static string ToPlainText(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page).Trim();
            if (text.Length > 0) sb.Append(text).Append("\n\n");
        }

        var result = sb.ToString().TrimEnd();
        if (result.Length == 0)
            throw new InvalidOperationException("Kein Text im PDF (evtl. gescannt, dafür wäre OCR nötig)");
        return result;
    }
}
