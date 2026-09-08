namespace Jarvis.Native;

internal static class VoiceDiagnostics
{
    private const long MaximumLogBytes = 128 * 1024;
    private static readonly object Sync = new();

    public static string LogPath => Path.Combine(AppConfig.DataDirectory, "voice.log");

    public static void Write(string eventName, string? detail = null)
    {
        try
        {
            var safeEvent = Sanitize(eventName);
            var safeDetail = Sanitize(detail ?? string.Empty);
            var line = $"{DateTimeOffset.Now:O}\t{safeEvent}\t{safeDetail}{Environment.NewLine}";

            lock (Sync)
            {
                Directory.CreateDirectory(AppConfig.DataDirectory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaximumLogBytes)
                {
                    File.WriteAllText(LogPath, line);
                }
                else
                {
                    File.AppendAllText(LogPath, line);
                }
            }
        }
        catch
        {
            // El diagnóstico nunca debe interrumpir al asistente.
        }
    }

    private static string Sanitize(string value)
    {
        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= 500 ? sanitized : sanitized[..500];
    }
}
