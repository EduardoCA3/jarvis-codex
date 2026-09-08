namespace Jarvis.Native;

public static class AppConfig
{
    public const string Model = "gpt-5.6-terra";
    public const string WakePhrase = "Hey Jarvis";
    public const string AppName = "Jarvis Codex";

    public static string DataDirectory
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "JarvisCodexNative");
        }
    }
}
