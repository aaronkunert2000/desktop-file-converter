using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FileConverter.Services;

/// <summary>
/// HTML nach PDF über WebView2 (Edge-Engine, auf Windows 10/11 meist vorinstalliert).
/// Läuft in einem unsichtbar außerhalb des Bildschirms platzierten Fenster, braucht den UI-Thread.
/// </summary>
internal static class HtmlPdfRenderer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static async Task RenderAsync(string htmlPath, string pdfPath)
    {
        var window = new Window
        {
            Width = 1000,
            Height = 800,
            Left = -32000,
            Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        var web = new WebView2();
        window.Content = web;
        window.Show();

        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Log.Dir, "webview2"));
            await web.EnsureCoreWebView2Async(env).WaitAsync(Timeout);

            var loaded = new TaskCompletionSource();
            web.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess) loaded.TrySetResult();
                else loaded.TrySetException(new IOException($"HTML konnte nicht geladen werden ({e.WebErrorStatus})"));
            };
            web.CoreWebView2.Navigate(new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri);
            await loaded.Task.WaitAsync(Timeout);

            var settings = env.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.PageWidth = 8.27;     // A4 in Zoll
            settings.PageHeight = 11.69;
            settings.MarginTop = settings.MarginBottom = 0.6;
            settings.MarginLeft = settings.MarginRight = 0.6;

            var ok = await web.CoreWebView2.PrintToPdfAsync(pdfPath, settings).WaitAsync(Timeout);
            if (!ok) throw new IOException("PDF konnte nicht erzeugt werden");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            throw new InvalidOperationException("WebView2-Runtime fehlt (Microsoft Edge WebView2 installieren)");
        }
        finally
        {
            web.Dispose();
            window.Close();
        }
    }
}
