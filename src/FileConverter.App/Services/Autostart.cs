using System;
using Microsoft.Win32;

namespace FileConverter.Services;

/// <summary>Autostart über HKCU\...\Run (kein Admin nötig).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FileConverter";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex)
        {
            Log.Error("Autostart lesen fehlgeschlagen", ex);
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            Log.Info($"Autostart {(enabled ? "an" : "aus")}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Autostart setzen fehlgeschlagen", ex);
            return false;
        }
    }
}
