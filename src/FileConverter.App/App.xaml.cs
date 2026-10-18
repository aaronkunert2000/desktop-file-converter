using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FileConverter.Services;
using FileConverter.Views;

namespace FileConverter;

public partial class App : Application
{
    private sealed record ConversionResult(string Source, string? Output, string? Error);

    private Mutex? _mutex;
    private bool _ownsMutex;
    private DragWatcher? _watcher;
    private RadialOverlay? _overlay;
    private System.Windows.Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, @"Local\FileConverter.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unbehandelter UI-Fehler", ex.Exception);
            ex.Handled = true;
        };

        CreateTray();

        _overlay = new RadialOverlay();
        _overlay.Dropped += OnDropped;

        _watcher = new DragWatcher(Dispatcher);
        _watcher.DragHeld += (x, y) => _overlay.ShowAt(x, y);
        _watcher.DragEnded += () => _overlay.HideSoon();

        try
        {
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Log.Error("DragWatcher konnte nicht starten", ex);
            Balloon("FileConverter", "Maus-Hook konnte nicht gestartet werden (Details im Log).", error: true);
        }
    }

    private async void OnDropped(IReadOnlyList<string> files, FormatTarget target)
    {
        Log.Info($"Drop: {files.Count} Datei(en) -> {target.Extension}");
        var results = new List<ConversionResult>();
        foreach (var file in files)
        {
            try
            {
                results.Add(new ConversionResult(file, await ConverterRegistry.ConvertAsync(file, target.Extension), null));
            }
            catch (Exception ex)
            {
                results.Add(new ConversionResult(file, null, ex.Message));
            }
        }

        var failed = results.Where(r => r.Error is not null).ToList();
        foreach (var r in results)
            Log.Info(r.Error is null ? $"OK   {r.Source} -> {r.Output}" : $"FAIL {r.Source}: {r.Error}");

        if (failed.Count == 0)
        {
            var text = results.Count == 1
                ? $"{Path.GetFileName(results[0].Output)}"
                : $"{results.Count} Dateien konvertiert";
            Balloon($"Konvertiert → {target.Label}", text, error: false);
        }
        else
        {
            Balloon("Konvertierung fehlgeschlagen", $"{failed.Count} von {results.Count}: {failed[0].Error}", error: true);
        }
    }

    // ---- Tray ------------------------------------------------------------------

    private void CreateTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("FileConverter 0.1 (Spike)").Enabled = false;
        menu.Items.Add("Log öffnen", null, (_, _) => OpenLog());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => Shutdown());

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "FileConverter",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    private void Balloon(string title, string text, bool error) =>
        _tray?.ShowBalloonTip(3000, title, text,
            error ? System.Windows.Forms.ToolTipIcon.Error : System.Windows.Forms.ToolTipIcon.Info);

    private static void OpenLog()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Log öffnen fehlgeschlagen", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _overlay?.Close();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
