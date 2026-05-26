namespace EDIRouter;

public sealed class Logger(string logDirectory)
{
    private readonly string _logDirectory = logDirectory;
    private const int RetentionDays = 30;

    public void Log(string message)
    {
        Directory.CreateDirectory(_logDirectory);
        string logFile = Path.Combine(_logDirectory, $"edirouter-{DateTime.Today:yyyy-MM-dd}.log");
        string entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
        File.AppendAllLines(logFile, [entry]);
    }

    public void PurgeOldLogs()
    {
        if (!Directory.Exists(_logDirectory))
            return;

        DateTime cutoff = DateTime.Today.AddDays(-RetentionDays);
        foreach (string file in Directory.GetFiles(_logDirectory, "edirouter-*.log"))
        {
            string name = Path.GetFileNameWithoutExtension(file); // edirouter-YYYY-MM-DD
            string datePart = name["edirouter-".Length..];
            if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out DateTime fileDate)
                && fileDate < cutoff)
            {
                try { File.Delete(file); }
                catch { /* leave it if deletion fails */ }
            }
        }
    }
}
