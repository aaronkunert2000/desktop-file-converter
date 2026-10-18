using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace FileConverter.Services;

/// <summary>Liest DOCX (Text, Überschriften, Listen, Tabellen, fett/kursiv) ohne Word.</summary>
internal static class DocxReader
{
    private static readonly Regex HeadingStyle = new(@"^(heading|überschrift|berschrift)\s*(\d)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Neu > Word-Dokument" im Explorer legt eine 0-Byte-Datei an, bis sie das erste Mal gespeichert wird
    private static bool IsEmpty(string path) => new FileInfo(path).Length == 0;

    public static string ToPlainText(string path)
    {
        if (IsEmpty(path)) return "";
        using var doc = WordprocessingDocument.Open(path, false);
        var body = GetBody(doc);
        return string.Join(Environment.NewLine, body.Descendants<Paragraph>().Select(p => p.InnerText));
    }

    /// <summary>HTML-Body-Inhalt (ohne html/head).</summary>
    public static string ToHtmlFragment(string path)
    {
        if (IsEmpty(path)) return "";
        using var doc = WordprocessingDocument.Open(path, false);
        var body = GetBody(doc);
        var sb = new StringBuilder();
        var inList = false;

        foreach (var element in body.ChildElements)
        {
            if (element is Paragraph p)
            {
                var isItem = p.ParagraphProperties?.NumberingProperties is not null;
                if (isItem && !inList) { sb.Append("<ul>"); inList = true; }
                if (!isItem && inList) { sb.Append("</ul>"); inList = false; }

                var inner = InlineHtml(p);
                if (isItem)
                {
                    sb.Append("<li>").Append(inner).Append("</li>");
                }
                else
                {
                    var level = HeadingLevel(p);
                    if (level > 0) sb.Append($"<h{level}>").Append(inner).Append($"</h{level}>");
                    else if (inner.Length > 0) sb.Append("<p>").Append(inner).Append("</p>");
                }
            }
            else if (element is Table table)
            {
                if (inList) { sb.Append("</ul>"); inList = false; }
                AppendTable(sb, table);
            }
        }

        if (inList) sb.Append("</ul>");
        return sb.ToString();
    }

    private static Body GetBody(WordprocessingDocument doc) =>
        doc.MainDocumentPart?.Document?.Body ?? throw new InvalidDataException("DOCX enthält keinen Inhalt");

    private static void AppendTable(StringBuilder sb, Table table)
    {
        sb.Append("<table>");
        foreach (var row in table.Elements<TableRow>())
        {
            sb.Append("<tr>");
            foreach (var cell in row.Elements<TableCell>())
            {
                sb.Append("<td>");
                sb.Append(string.Join("<br>", cell.Elements<Paragraph>().Select(InlineHtml)));
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table>");
    }

    private static string InlineHtml(Paragraph paragraph)
    {
        var sb = new StringBuilder();
        foreach (var run in paragraph.Descendants<Run>())
        {
            var text = new StringBuilder();
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case Text t: text.Append(WebUtility.HtmlEncode(t.Text)); break;
                    case Break: text.Append("<br>"); break;
                    case TabChar: text.Append("&emsp;"); break;
                }
            }
            if (text.Length == 0) continue;

            var props = run.RunProperties;
            var bold = props?.Bold is { } b && (b.Val is null || b.Val.Value);
            var italic = props?.Italic is { } i && (i.Val is null || i.Val.Value);

            var html = text.ToString();
            if (bold) html = $"<strong>{html}</strong>";
            if (italic) html = $"<em>{html}</em>";
            sb.Append(html);
        }
        return sb.ToString();
    }

    private static int HeadingLevel(Paragraph paragraph)
    {
        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrEmpty(style)) return 0;
        if (style.Equals("Title", System.StringComparison.OrdinalIgnoreCase)) return 1;

        var match = HeadingStyle.Match(style);
        return match.Success && int.TryParse(match.Groups[2].Value, out var level) ? System.Math.Clamp(level, 1, 6) : 0;
    }
}
