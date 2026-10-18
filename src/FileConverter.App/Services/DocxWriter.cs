using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FileConverter.Services;

/// <summary>
/// Schreibt DOCX aus Markdown (Überschriften, Absätze, Listen, Zitate, Code, fett/kursiv) oder reinem Text.
/// Bewusst direkte Formatierung statt Formatvorlagen; Tabellen und Bilder folgen später.
/// </summary>
internal static class DocxWriter
{
    private const string BodyFont = "Calibri";
    private const string CodeFont = "Consolas";
    private const int BodySize = 22;   // Halbpunkte = 11 pt

    public static void FromMarkdown(string markdown, string target)
    {
        var body = new Body();
        foreach (var block in Markdown.Parse(markdown))
            AddBlock(block, body, indent: 0, quote: false);
        Save(target, body);
    }

    public static void FromPlainText(string text, string target)
    {
        var body = new Body();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            body.Append(MakeParagraph(new List<OpenXmlElement> { MakeRun(line, false, false, false, BodySize) }, 0, 0));
        Save(target, body);
    }

    private static void Save(string target, Body body)
    {
        using var doc = WordprocessingDocument.Create(target, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(body);
        main.Document.Save();
    }

    private static void AddBlock(Block block, Body body, int indent, bool quote)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                var size = heading.Level switch { 1 => 40, 2 => 32, 3 => 28, _ => 24 };
                var runs = new List<OpenXmlElement>();
                AddInlines(heading.Inline, runs, bold: true, italic: false, size);
                body.Append(MakeParagraph(runs, indent, after: 120, before: 240));
                break;
            }

            case ParagraphBlock paragraph:
            {
                var runs = new List<OpenXmlElement>();
                AddInlines(paragraph.Inline, runs, bold: false, italic: quote, BodySize);
                body.Append(MakeParagraph(runs, indent, after: 120));
                break;
            }

            case ListBlock list:
            {
                var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var first = true;
                    foreach (var child in item)
                    {
                        if (child is ParagraphBlock itemParagraph)
                        {
                            var runs = new List<OpenXmlElement>();
                            if (first) runs.Add(MakeRun(list.IsOrdered ? $"{number}. " : "• ", false, false, false, BodySize));
                            AddInlines(itemParagraph.Inline, runs, bold: false, italic: quote, BodySize);
                            body.Append(MakeParagraph(runs, indent + 1, after: 60));
                            first = false;
                        }
                        else
                        {
                            AddBlock(child, body, indent + 1, quote);
                        }
                    }
                    number++;
                }
                break;
            }

            case CodeBlock code:
            {
                foreach (var line in code.Lines.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
                    body.Append(MakeParagraph(new List<OpenXmlElement> { MakeRun(line, false, false, true, 20) }, indent + 1, after: 0));
                body.Append(MakeParagraph(new List<OpenXmlElement>(), indent, after: 60));
                break;
            }

            case QuoteBlock quoteBlock:
                foreach (var child in quoteBlock)
                    AddBlock(child, body, indent + 1, quote: true);
                break;

            case ThematicBreakBlock:
                body.Append(MakeParagraph(new List<OpenXmlElement> { MakeRun("────────────────────", false, false, false, BodySize) }, indent, after: 120));
                break;
        }
    }

    private static void AddInlines(ContainerInline? container, List<OpenXmlElement> runs, bool bold, bool italic, int size)
    {
        if (container is null) return;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    runs.Add(MakeRun(literal.Content.ToString(), bold, italic, false, size));
                    break;

                case CodeInline code:
                    runs.Add(MakeRun(code.Content, bold, italic, true, size));
                    break;

                case LineBreakInline lineBreak:
                    runs.Add(lineBreak.IsHard ? new Run(new Break()) : MakeRun(" ", bold, italic, false, size));
                    break;

                case EmphasisInline emphasis:
                    AddInlines(emphasis, runs,
                        bold || emphasis.DelimiterCount >= 2,
                        italic || emphasis.DelimiterCount is 1 or 3,
                        size);
                    break;

                case LinkInline link:
                    AddInlines(link, runs, bold, italic, size);
                    if (!link.IsImage && !string.IsNullOrEmpty(link.Url))
                        runs.Add(MakeRun($" ({link.Url})", bold, italic, false, size));
                    break;

                case ContainerInline other:
                    AddInlines(other, runs, bold, italic, size);
                    break;
            }
        }
    }

    private static Run MakeRun(string text, bool bold, bool italic, bool code, int size)
    {
        var font = code ? CodeFont : BodyFont;
        // Reihenfolge der Eigenschaften folgt dem OpenXML-Schema (rFonts, b, i, sz)
        var props = new RunProperties(new RunFonts { Ascii = font, HighAnsi = font });
        if (bold) props.Append(new Bold());
        if (italic) props.Append(new Italic());
        props.Append(new FontSize { Val = size.ToString() });
        return new Run(props, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static Paragraph MakeParagraph(List<OpenXmlElement> runs, int indent, int after, int before = 0)
    {
        var spacing = new SpacingBetweenLines { After = after.ToString() };
        if (before > 0) spacing.Before = before.ToString();

        var props = new ParagraphProperties(spacing);
        if (indent > 0) props.Append(new Indentation { Left = (indent * 360).ToString() });

        var paragraph = new Paragraph(props);
        foreach (var run in runs) paragraph.Append(run);
        return paragraph;
    }
}
