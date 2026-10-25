using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using FileConverter.Native;

namespace FileConverter.Services;

/// <summary>
/// Erkennt per globalem Low-Level-Maus-Hook, dass eine Datei auf dem Desktop/im Explorer gezogen
/// und kurz still gehalten wird (Dwell) oder mit gehaltener Alt-Taste gezogen wird.
/// Der Hook-Callback bleibt bewusst minimal (Windows entfernt Hooks, die &gt; ~300 ms brauchen);
/// ausgelöst wird erst im UI-Timer.
/// </summary>
public sealed class DragWatcher : IDisposable
{
    private enum Phase { Idle, Pressed, Dragging, Shown }

    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;

    // Fenster, in denen Dateien liegen: Explorer, Desktop
    private static readonly string[] FileSurfaceClasses = { "CabinetWClass", "ExploreWClass", "XamlExplorerHostIslandWindow", "Progman", "WorkerW" };

    private readonly NativeMethods.LowLevelMouseProc _proc;   // Referenz halten, sonst räumt der GC den Delegate ab
    private readonly DispatcherTimer _timer;
    private readonly int _dragThreshold;
    private IntPtr _hook = IntPtr.Zero;
    private Phase _phase = Phase.Idle;
    private int _downX, _downY, _lastX, _lastY;
    private long _lastMoveTick;
    private bool _firePending;

    public int DwellMs { get; set; } = 500;
    public int StillTolerancePx { get; set; } = 6;
    public bool AltTriggersImmediately { get; set; } = true;

    /// <summary>Ring soll erscheinen; Bildschirmkoordinaten in Pixeln.</summary>
    public event Action<int, int>? DragHeld;

    /// <summary>Maustaste losgelassen, nachdem der Ring gezeigt wurde.</summary>
    public event Action? DragEnded;

    public DragWatcher(Dispatcher dispatcher)
    {
        _proc = HookCallback;
        _dragThreshold = Math.Max(NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDRAG), 4);
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => OnTick();
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"Maus-Hook fehlgeschlagen (Win32 {Marshal.GetLastWin32Error()})");
        _timer.Start();
        Log.Info($"Maus-Hook aktiv (Dwell {DwellMs} ms, Drag-Schwelle {_dragThreshold} px)");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                Handle((int)wParam, data.pt.x, data.pt.y);
            }
            catch (Exception ex)
            {
                Log.Error("Hook-Fehler", ex);
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void Handle(int message, int x, int y)
    {
        switch (message)
        {
            case WmLButtonDown:
                var candidate = IsFileSurface(x, y, out var className);
                Log.Info($"Klick auf '{className}' -> {(candidate ? "Datei-Fläche" : "ignoriert")}");
                _phase = candidate ? Phase.Pressed : Phase.Idle;
                _downX = x;
                _downY = y;
                _firePending = false;
                break;

            case WmMouseMove:
                if (_phase == Phase.Pressed && Distance(x, y, _downX, _downY) > _dragThreshold)
                {
                    _phase = Phase.Dragging;
                    Log.Info("Ziehen erkannt");
                    _lastX = x;
                    _lastY = y;
                    _lastMoveTick = Environment.TickCount64;
                }
                else if (_phase == Phase.Dragging && Distance(x, y, _lastX, _lastY) > StillTolerancePx)
                {
                    _lastX = x;
                    _lastY = y;
                    _lastMoveTick = Environment.TickCount64;
                }

                if (_phase == Phase.Dragging && AltTriggersImmediately && IsAltDown())
                    _firePending = true;
                break;

            case WmLButtonUp:
                var wasShown = _phase == Phase.Shown;
                Log.Info($"Maustaste los (Phase {_phase})");
                _phase = Phase.Idle;
                _firePending = false;
                if (wasShown) DragEnded?.Invoke();
                break;
        }
    }

    /// <summary>Läuft auf dem UI-Thread, außerhalb des Hook-Callbacks.</summary>
    private void OnTick()
    {
        if (_phase != Phase.Dragging) return;

        var dwellReached = Environment.TickCount64 - _lastMoveTick >= DwellMs;
        if (!_firePending && !dwellReached) return;

        _phase = Phase.Shown;
        Log.Info($"Ring ausgelöst ({(_firePending ? "Alt" : "Dwell")}) bei {_lastX},{_lastY}");
        _firePending = false;
        DragHeld?.Invoke(_lastX, _lastY);
    }

    private static bool IsFileSurface(int x, int y, out string className)
    {
        className = "?";
        var hwnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT { x = x, y = y });
        if (hwnd == IntPtr.Zero) return false;
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero) root = hwnd;

        var name = new StringBuilder(64);
        if (NativeMethods.GetClassName(root, name, name.Capacity) == 0) return false;
        className = name.ToString();
        return Array.IndexOf(FileSurfaceClasses, className) >= 0;
    }

    private static bool IsAltDown() => (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;

    private static double Distance(int x1, int y1, int x2, int y2)
    {
        double dx = x1 - x2, dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
