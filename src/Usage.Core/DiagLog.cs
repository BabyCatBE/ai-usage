using System.Text;

namespace Usage.Core;

/// <summary>
/// Tiny best-effort diagnostic log, kept in a hidden folder next to the app data (never shown in the UI).
/// Records only codes, timings and exception types - never CLI output, tokens or account data.
/// </summary>
public static class DiagLog
{
    const long MaxBytes = 512 * 1024;
    static readonly object gate = new();
    public static string Dir => Path.Combine(AppPaths.Data, "logs");
    static string FilePath => Path.Combine(Dir, "diag.log");

    public static void Write(string area, string message)
    {
        try
        {
            lock (gate)
            {
                var dir = Directory.CreateDirectory(Dir);
                if ((dir.Attributes & FileAttributes.Hidden) == 0) dir.Attributes |= FileAttributes.Hidden;
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var old = FilePath + ".1";
                    File.Move(FilePath, old, overwrite: true);
                    File.SetAttributes(old, File.GetAttributes(old) | FileAttributes.Hidden);
                }
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{area}] {message}{Environment.NewLine}", new UTF8Encoding(false));
                File.SetAttributes(FilePath, File.GetAttributes(FilePath) | FileAttributes.Hidden);
            }
        }
        catch { /* logging must never break the app */ }
    }
}
