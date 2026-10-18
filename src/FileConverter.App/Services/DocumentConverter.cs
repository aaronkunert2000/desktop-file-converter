using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Markdig;

namespace FileConverter.Services;

/// <summary>
/// Dokumente: MD, TXT, HTML, DOCX untereinander und nach PDF.
/// Zwischenformat ist HTML (Anzeige/PDF) bzw. Markdown (Text). DOCX nach PDF läuft über HTML, ist also
/// eine vereinfachte Darstellung (Text, Überschriften, Listen, Tabellen).
/// </summary>
internal static class DocumentConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    private static readonly UTF8Encoding Utf8 = new(false);

    public static async Task<string> ConvertAsync(string source, string src, string targetExt)
    {
        var target = ImageConverter.UniquePath(source, targetExt);

        switch (targetExt)
        {
            case ".pdf":
            {
                var temp = src == ".html" ? null : Path.Combine(Path.GetTempPath(), $"fc-{Guid.NewGuid():N}.html");
                try
                {
                    if (temp is not null) File.WriteAllText(temp, ToHtml(source, src), Utf8);
                    await HtmlPdfRenderer.RenderAsync(temp ?? source, target);
                }
                finally
                {
                    if (temp is not null) TryDelete(temp);
                }
                break;
            }

            case ".html":
                File.WriteAllText(target, ToHtml(source, src), Utf8);
                break;

            case ".md":
                File.WriteAllText(target, ToMarkdown(source, src), Utf8);
                break;

            case ".txt":
                File.WriteAllText(target, ToPlainText(source, src), Utf8);
                break;

            case ".docx":
                if (src == ".md") DocxWriter.FromMarkdown(File.ReadAllText(source), target);
                else if (src == ".txt") DocxWriter.FromPlainText(File.ReadAllText(source), target);
                else if (src == ".pdf") DocxWriter.FromPlainText(PdfReader.ToPlainText(source), target);
                else throw Unsupported(src, targetExt);
                break;

            default:
                throw Unsupported(src, targetExt);
        }

        return target;
    }

    private static string ToHtml(string source, string src) => src switch
    {
        ".md" => Wrap(Markdown.ToHtml(File.ReadAllText(source), Pipeline), source),
        ".txt" => Wrap($"<div class=\"txt\">{WebUtility.HtmlEncode(File.ReadAllText(source))}</div>", source),
        ".docx" => Wrap(DocxReader.ToHtmlFragment(source), source),
        ".pdf" => Wrap($"<div class=\"txt\">{WebUtility.HtmlEncode(PdfReader.ToPlainText(source))}</div>", source),
        ".html" => File.ReadAllText(source),
        _ => throw Unsupported(src, ".html"),
    };

    private static string ToMarkdown(string source, string src) => src switch
    {
        ".txt" => File.ReadAllText(source),
        ".pdf" => PdfReader.ToPlainText(source),
        ".html" => HtmlToMarkdown(File.ReadAllText(source)),
        ".docx" => HtmlToMarkdown(DocxReader.ToHtmlFragment(source)),
        _ => throw Unsupported(src, ".md"),
    };

    private static string ToPlainText(string source, string src) => src switch
    {
        ".md" => Markdown.ToPlainText(File.ReadAllText(source), Pipeline),
        ".html" => Markdown.ToPlainText(HtmlToMarkdown(File.ReadAllText(source)), Pipeline),
        ".docx" => DocxReader.ToPlainText(source),
        ".pdf" => PdfReader.ToPlainText(source),
        _ => throw Unsupported(src, ".txt"),
    };

    private static string HtmlToMarkdown(string html)
    {
        var converter = new ReverseMarkdown.Converter(new ReverseMarkdown.Config
        {
            UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.Bypass,
            GithubFlavored = true,
            RemoveComments = true,
        });
        return converter.Convert(html);
    }

    private static string Wrap(string bodyHtml, string source) => $$"""
        <!DOCTYPE html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <title>{{WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(source))}}</title>
        <style>
          body { font-family: "Segoe UI", Calibri, Arial, sans-serif; font-size: 11pt; line-height: 1.5; color: #1b1b1b; max-width: 46em; margin: 2em auto; padding: 0 1em; }
          h1, h2, h3, h4 { line-height: 1.25; margin-top: 1.4em; }
          pre, code { font-family: Consolas, "Courier New", monospace; font-size: 0.95em; }
          pre { background: #f4f4f6; padding: 0.8em 1em; border-radius: 6px; overflow-x: auto; white-space: pre-wrap; }
          code { background: #f4f4f6; padding: 0.1em 0.3em; border-radius: 4px; }
          pre code { background: none; padding: 0; }
          blockquote { margin-left: 0; padding-left: 1em; border-left: 3px solid #c9c9d1; color: #4a4a55; }
          table { border-collapse: collapse; margin: 1em 0; }
          th, td { border: 1px solid #c9c9d1; padding: 0.35em 0.7em; text-align: left; }
          img { max-width: 100%; }
          .txt { white-space: pre-wrap; font-family: Consolas, "Courier New", monospace; font-size: 10pt; }
        </style>
        </head>
        <body>
        {{bodyHtml}}
        </body>
        </html>
        """;

    private static NotSupportedException Unsupported(string src, string target) =>
        new($"{src} → {target} wird nicht unterstützt");

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* Temp-Datei, egal */ }
    }
}
