namespace Jarvis.Native;

public sealed record RuntimePaths(string ProjectRoot, string CodexExecutable)
{
    public static RuntimePaths Discover(string? startingDirectory = null)
    {
        var start = new DirectoryInfo(startingDirectory ?? AppContext.BaseDirectory);
        for (var directory = start; directory is not null; directory = directory.Parent)
        {
            var codex = Path.Combine(
                directory.FullName,
                ".venv",
                "Lib",
                "site-packages",
                "codex_cli_bin",
                "bin",
                "codex.exe");

            if (File.Exists(codex))
            {
                return new RuntimePaths(directory.FullName, codex);
            }
        }

        throw new FileNotFoundException(
            "No encontré el entorno local de Jarvis. Conserva NativeApp junto a la carpeta .venv.");
    }
}
