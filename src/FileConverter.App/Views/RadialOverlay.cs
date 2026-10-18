using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FileConverter.Native;
using FileConverter.Services;

namespace FileConverter.Views;

/// <summary>
/// Durchsichtiges Fenster unter dem Cursor, das selbst Drop-Ziel ist. Zeigt erst nach DragEnter
/// den Ring (der hängt vom Dateityp ab). Der Ring ist eingeblendet, solange der Cursor darüber ist.
/// Mitte (Totzone) und Bereich außerhalb = Abbrechen.
/// </summary>
public sealed class RadialOverlay : Window
{
    private const double RingSize = 320;
    private const double InnerRadius = 46;
    private const double OuterRadius = 150;
    private const double GapDegrees = 2;

    private static readonly Brush HitBrush = Frozen(Color.FromArgb(1, 0, 0, 0));          // Alpha 1: sonst reicht Windows Klicks/Drops durch
    private static readonly Brush NormalBrush = Frozen(Color.FromArgb(0xD8, 0x1E, 0x1E, 0x24));
    private static readonly Brush HoverBrush = Frozen(Color.FromArgb(0xF2, 0x2F, 0x7D, 0xF6));
    private static readonly Brush DisabledBrush = Frozen(Color.FromArgb(0x70, 0x30, 0x30, 0x36));
    private static readonly Brush LabelBrush = Frozen(Colors.White);
    private static readonly Brush LabelDisabledBrush = Frozen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));

    private sealed record SegmentVisual(Path Shape, TextBlock Label, FormatTarget Target);

    private readonly Grid _root = new();
    private readonly Canvas _ring = new() { Width = RingSize, Height = RingSize, IsHitTestVisible = false };
    private readonly List<SegmentVisual> _segments = new();
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private IReadOnlyList<string> _files = Array.Empty<string>();
    private bool _entered;
    private int _hover = -1;

    /// <summary>Dateien wurden auf ein aktives Segment gelegt.</summary>
    public event Action<IReadOnlyList<string>, FormatTarget>? Dropped;

    public RadialOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Width = RingSize;
        Height = RingSize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        AllowDrop = true;

        // Unsichtbare, aber trefferfähige Fläche: das Drop-Ziel für Windows
        _root.Children.Add(new Ellipse
        {
            Width = OuterRadius * 2,
            Height = OuterRadius * 2,
            Fill = HitBrush,
            IsHitTestVisible = false,
        });
        _root.Children.Add(_ring);
        Content = _root;

        DragEnter += OnDragEnterOrOver;
        DragOver += OnDragEnterOrOver;
        DragLeave += (_, _) => HideRing();
        Drop += OnDrop;

        // Kam innerhalb kurzer Zeit kein DragEnter, war es gar kein Datei-Drag (z. B. Fenster verschieben)
        _watchdog.Tick += (_, _) =>
        {
            _watchdog.Stop();
            if (!_entered) HideRing();
        };
    }

    /// <summary>Zeigt den (noch leeren) Ring mit dem Mittelpunkt bei den Bildschirmkoordinaten (Pixel).</summary>
    public void ShowAt(int px, int py)
    {
        if (IsVisible) return;

        _entered = false;
        _hover = -1;
        ClearRing();

        var point = new NativeMethods.POINT { x = px, y = py };
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        uint dpi = 96;
        if (NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0) dpi = dpiX;
        var sizePx = (int)Math.Round(RingSize * dpi / 96.0);

        var left = px - sizePx / 2;
        var top = py - sizePx / 2;
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            left = Math.Clamp(left, info.rcWork.left, Math.Max(info.rcWork.left, info.rcWork.right - sizePx));
            top = Math.Clamp(top, info.rcWork.top, Math.Max(info.rcWork.top, info.rcWork.bottom - sizePx));
        }

        Left = -32000;
        Top = -32000;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        Show();
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, left, top, sizePx, sizePx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        _watchdog.Start();
        Nudge(px, py);
    }

    /// <summary>
    /// Windows meldet einem neuen Drop-Ziel den Drag erst bei der nächsten Mausbewegung.
    /// Der Cursor steht aber still (Dwell) - also kurz um 2 px bewegen und zurück.
    /// </summary>
    private static void Nudge(int px, int py)
    {
        NativeMethods.SetCursorPos(px + 2, py);
        var back = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        back.Tick += (_, _) =>
        {
            back.Stop();
            if (NativeMethods.GetCursorPos(out var now) && now.x == px + 2 && now.y == py)
                NativeMethods.SetCursorPos(px, py);
        };
        back.Start();
    }

    /// <summary>
    /// Sicherheitsnetz nach dem Loslassen. Nicht sofort ausblenden: Windows ruft Drop erst nach dem
    /// Maus-Hook auf, das Fenster muss dann noch da sein.
    /// </summary>
    public void HideSoon()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            HideRing();
        };
        timer.Start();
    }

    public void HideRing()
    {
        _watchdog.Stop();
        if (IsVisible) Hide();
        ClearRing();
    }

    // ---- Drag & Drop -----------------------------------------------------------

    private void OnDragEnterOrOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;

        if (!_entered)
        {
            _entered = true;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var targets = files is null ? null : FormatRegistry.TargetsFor(files);
            if (files is null || targets is null)
            {
                Dispatcher.BeginInvoke(new Action(HideRing));   // nicht unterstützt: Ring gar nicht erst zeigen
                return;
            }
            _files = files;
            BuildRing(targets);
        }

        var index = HitTest(e.GetPosition(_root));
        SetHover(index);
        if (index >= 0 && _segments[index].Target.Enabled) e.Effects = DragDropEffects.Copy;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var index = HitTest(e.GetPosition(_root));
        var files = _files;
        var target = index >= 0 ? _segments[index].Target : null;
        HideRing();

        if (target is { Enabled: true })
        {
            e.Effects = DragDropEffects.Copy;
            Dropped?.Invoke(files, target);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    // ---- Ring ------------------------------------------------------------------

    private void ClearRing()
    {
        _ring.Children.Clear();
        _segments.Clear();
        _ring.BeginAnimation(OpacityProperty, null);
        _hover = -1;
    }

    private void BuildRing(IReadOnlyList<FormatTarget> targets)
    {
        ClearRing();
        var center = RingSize / 2;
        var step = 360.0 / targets.Count;

        for (var i = 0; i < targets.Count; i++)
        {
            var start = -90 + i * step + GapDegrees / 2;
            var end = -90 + (i + 1) * step - GapDegrees / 2;

            var shape = new Path { Data = Sector(center, center, InnerRadius, OuterRadius, start, end), IsHitTestVisible = false };
            var label = new TextBlock
            {
                Text = targets[i].Label,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false,
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var mid = (start + end) / 2 * Math.PI / 180;
            var radius = (InnerRadius + OuterRadius) / 2;
            Canvas.SetLeft(label, center + radius * Math.Cos(mid) - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, center + radius * Math.Sin(mid) - label.DesiredSize.Height / 2);

            _ring.Children.Add(shape);
            _ring.Children.Add(label);
            _segments.Add(new SegmentVisual(shape, label, targets[i]));
        }

        ApplyColors();
        _ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }

    private void SetHover(int index)
    {
        if (index == _hover) return;
        _hover = index;
        ApplyColors();
    }

    private void ApplyColors()
    {
        for (var i = 0; i < _segments.Count; i++)
        {
            var seg = _segments[i];
            var enabled = seg.Target.Enabled;
            seg.Shape.Fill = !enabled ? DisabledBrush : i == _hover ? HoverBrush : NormalBrush;
            seg.Label.Foreground = enabled ? LabelBrush : LabelDisabledBrush;
        }
    }

    /// <summary>Segment-Index unter dem Punkt oder -1 (Totzone, außerhalb).</summary>
    private int HitTest(Point p)
    {
        if (_segments.Count == 0) return -1;
        var center = RingSize / 2;
        double dx = p.X - center, dy = p.Y - center;
        var radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius < InnerRadius || radius > OuterRadius) return -1;

        var degrees = Math.Atan2(dy, dx) * 180 / Math.PI;     // 0° = rechts, im Uhrzeigersinn
        var fromTop = (degrees + 90 + 360) % 360;
        return Math.Min((int)(fromTop / (360.0 / _segments.Count)), _segments.Count - 1);
    }

    private static Geometry Sector(double cx, double cy, double inner, double outer, double startDeg, double endDeg)
    {
        static Point At(double cx, double cy, double r, double deg) =>
            new(cx + r * Math.Cos(deg * Math.PI / 180), cy + r * Math.Sin(deg * Math.PI / 180));

        var large = endDeg - startDeg > 180;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(cx, cy, outer, startDeg), isFilled: true, isClosed: true);
            ctx.ArcTo(At(cx, cy, outer, endDeg), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(At(cx, cy, inner, endDeg), true, false);
            ctx.ArcTo(At(cx, cy, inner, startDeg), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
