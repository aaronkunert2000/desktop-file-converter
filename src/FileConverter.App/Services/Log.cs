using System;
using System.IO;

namespace FileConverter.Services;

/// <summary>Minimales Datei-Log unter %APPDATA%\FileConverter\fileconverter.log.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileConverter");

    public static string FilePath => Path.Combine(Dir, "fileconverter.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging darf die App nie stören
        }
    }
}
