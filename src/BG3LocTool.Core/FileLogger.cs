namespace BG3LocTool.Core;

public static class FileLogger
{
    private static readonly object _lock = new();
    private static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BG3LocTool", "logs");

    public static void Append(string source, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var path = Path.Combine(LogDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            var line = $"[{DateTime.Now:HH:mm:ss}] [{source}] {message}{Environment.NewLine}";
            lock (_lock) File.AppendAllText(path, line);
        }
        catch { /* logging must never throw */ }
    }
}
