using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Native;

public sealed partial class NativeActionEngine
{
    private const uint KeyEventKeyUp = 0x0002;
    private readonly LocalStateStore _store;

    private static readonly Dictionary<string, string> Apps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bloc de notas"] = "notepad.exe",
        ["notepad"] = "notepad.exe",
        ["calculadora"] = "calc.exe",
        ["explorador"] = "explorer.exe",
        ["explorador de archivos"] = "explorer.exe",
        ["administrador de tareas"] = "taskmgr.exe",
        ["paint"] = "mspaint.exe",
        ["pintura"] = "mspaint.exe",
        ["terminal"] = "wt.exe",
        ["powershell"] = "powershell.exe",
        ["spotify"] = "spotify:",
        ["configuracion"] = "ms-settings:",
        ["ajustes"] = "ms-settings:",
        ["camara"] = "microsoft.windows.camera:",
        ["recortes"] = "ms-screenclip:"
    };

    private static readonly Dictionary<string, string> Sites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["google"] = "https://www.google.com/",
        ["youtube"] = "https://www.youtube.com/",
        ["gmail"] = "https://mail.google.com/",
        ["chatgpt"] = "https://chatgpt.com/"
    };

    private static readonly string[] BlockedPrefixes =
    [
        "borra ", "borrar ", "elimina ", "eliminar ", "formatea ", "formatear ",
        "instala ", "instalar ", "desinstala ", "desinstalar ", "compra ", "comprar ",
        "paga ", "pagar ", "envia ", "enviar ", "manda un mensaje", "apaga ",
        "apagar ", "reinicia ", "reiniciar "
    ];

    public NativeActionEngine(LocalStateStore store) => _store = store;

    public ActionResult Execute(string original)
    {
        var clean = original.Trim();
        var text = VoiceCommandNormalizer.Normalize(clean);
        if (text.Length == 0)
        {
            return new ActionResult(true, "No escuché ninguna orden.", false);
        }

        if (BlockedPrefixes.Any(prefix =>
                text.StartsWith(prefix, StringComparison.Ordinal) ||
                text.Contains(" " + prefix, StringComparison.Ordinal)))
        {
            return new ActionResult(
                true,
                "Esa acción requiere confirmación y está bloqueada por voz. Una mala transcripción no podrá borrar, instalar, comprar, enviar mensajes, apagar ni reiniciar tu PC.");
        }

        if (text is "ayuda" or "comandos" or "que puedes hacer")
        {
            return new ActionResult(true, HelpText, false);
        }

        if (ConversationCommands.IsEnd(text))
        {
            return new ActionResult(
                true,
                "Conversación finalizada. Di mi nombre cuando quieras hablar otra vez.");
        }

        if (text is "hora" or "que hora es" or "dime la hora")
        {
            return new ActionResult(true, $"Son las {DateTime.Now:HH:mm}.");
        }

        if (text is "fecha" or "que fecha es" or "que dia es" or "dime la fecha")
        {
            var culture = CultureInfo.GetCultureInfo("es-PE");
            return new ActionResult(true, $"Hoy es {DateTime.Now.ToString("dddd d 'de' MMMM 'de' yyyy", culture)}.");
        }

        var windowAction = TryWindowAction(text);
        if (windowAction is not null)
        {
            return windowAction;
        }

        var contentSearch = ContentSearchRegex().Match(text);
        if (contentSearch.Success)
        {
            return FindAndRevealContent(contentSearch.Groups[1].Value);
        }

        var open = OpenRegex().Match(text);
        if (open.Success)
        {
            return OpenTarget(CleanOpenTarget(open.Groups[1].Value));
        }

        var search = SearchRegex().Match(clean);
        if (search.Success)
        {
            var query = search.Groups[1].Value.Trim();
            OpenShell("https://www.google.com/search?q=" + Uri.EscapeDataString(query));
            return new ActionResult(true, $"Buscando {query} en tu navegador.");
        }

        var note = NoteRegex().Match(clean);
        if (note.Success)
        {
            var content = note.Groups[1].Value.Trim();
            var saved = _store.AddNote(content);
            return new ActionResult(true, $"Nota {saved.Id} guardada localmente: {content}");
        }

        if (text is "mis notas" or "muestra mis notas" or "lista mis notas")
        {
            var notes = _store.GetNotes();
            return notes.Count == 0
                ? new ActionResult(true, "Todavía no tienes notas guardadas.")
                : new ActionResult(true, "Tus últimas notas:\r\n" + string.Join("\r\n", notes.Select(n => $"{n.Id}. {n.Content}")), false);
        }

        var memory = MemoryRegex().Match(clean);
        if (memory.Success)
        {
            var content = memory.Groups[1].Value.Trim();
            var added = _store.AddMemory(content);
            return new ActionResult(true, $"{(added ? "Lo recordaré localmente" : "Ya tenía guardado ese dato")}: {content}");
        }

        if (text is "que recuerdas" or "que sabes de mi" or "muestra tus recuerdos")
        {
            var memories = _store.GetMemories();
            return memories.Count == 0
                ? new ActionResult(true, "Todavía no me has pedido recordar nada.")
                : new ActionResult(true, "Recuerdo esto:\r\n• " + string.Join("\r\n• ", memories), false);
        }

        var media = MediaAction(text);
        if (media is not null)
        {
            return media;
        }

        if (text is "estado del sistema" or "estado del pc" or "como esta mi pc" or "sistema")
        {
            return SystemStatus();
        }

        return new ActionResult(false);
    }

    public static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static ActionResult OpenTarget(string target)
    {
        if (Apps.TryGetValue(target, out var executable))
        {
            OpenShell(executable);
            return new ActionResult(true, $"Abriendo {target}.");
        }

        if (Sites.TryGetValue(target, out var url))
        {
            OpenShell(url);
            return new ActionResult(true, $"Abriendo {target} en tu navegador.");
        }

        var folder = ResolveKnownFolder(target);
        if (folder is not null)
        {
            OpenShell(folder);
            return new ActionResult(true, $"Abriendo {FriendlyTargetName(target)}.");
        }

        var settings = ResolveSettingsUri(target);
        if (settings is not null)
        {
            OpenShell(settings);
            return new ActionResult(true, $"Abriendo la configuración de {target}.");
        }

        var installedApp = PcCatalog.FindInstalledApp(target);
        if (installedApp is not null)
        {
            PcCatalog.LaunchInstalledApp(installedApp);
            return new ActionResult(true, $"Abriendo {installedApp.Name}.");
        }

        var shortcut = FindInstalledShortcut(target);
        if (shortcut is not null)
        {
            OpenShell(shortcut);
            return new ActionResult(true, $"Abriendo {Path.GetFileNameWithoutExtension(shortcut)}.");
        }

        var userContent = PcCatalog.FindUserContent(target);
        if (userContent is not null)
        {
            OpenShell(userContent);
            return new ActionResult(true, $"Abriendo {Path.GetFileName(userContent)}.");
        }

        return new ActionResult(
            true,
            $"No encontré una aplicación o carpeta llamada {target}. Puedes decir, por ejemplo: abre Descargas, abre calculadora o abre Google.");
    }

    private static string CleanOpenTarget(string target)
    {
        var cleaned = target.Trim();
        cleaned = Regex.Replace(cleaned, @"\s+(?:por favor|ahora)$", string.Empty);
        cleaned = Regex.Replace(cleaned, @"^(?:el|la|los|las|un|una|mi|mis)\s+", string.Empty);
        return cleaned.Trim();
    }

    private static string? ResolveKnownFolder(string target)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return target switch
        {
            "descargas" or "carpeta de descargas" => Path.Combine(userProfile, "Downloads"),
            "documentos" or "carpeta de documentos" =>
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "escritorio" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "imagenes" or "fotos" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "musica" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "archivos" or "mis archivos" or "carpeta personal" => userProfile,
            "pc" or "mi pc" or "este equipo" => "shell:MyComputerFolder",
            _ => null
        };
    }

    private static string FriendlyTargetName(string target) => target switch
    {
        "pc" or "mi pc" or "este equipo" => "Este equipo",
        _ => target
    };

    private static string? ResolveSettingsUri(string target) => target switch
    {
        "bluetooth" or "configuracion de bluetooth" => "ms-settings:bluetooth",
        "wifi" or "wi fi" or "configuracion de wifi" => "ms-settings:network-wifi",
        "internet" or "red" or "configuracion de red" => "ms-settings:network",
        "pantalla" or "configuracion de pantalla" => "ms-settings:display",
        "sonido" or "audio" or "configuracion de sonido" => "ms-settings:sound",
        "microfono" or "configuracion del microfono" => "ms-settings:privacy-microphone",
        "notificaciones" => "ms-settings:notifications",
        "aplicaciones" or "aplicaciones instaladas" => "ms-settings:appsfeatures",
        "windows update" or "actualizaciones" => "ms-settings:windowsupdate",
        "energia" or "bateria" => "ms-settings:powersleep",
        _ => null
    };

    private static ActionResult FindAndRevealContent(string requestedName)
    {
        var path = PcCatalog.FindUserContent(requestedName);
        if (path is null)
        {
            return new ActionResult(
                true,
                $"No encontré {requestedName} en Escritorio, Documentos, Descargas, Imágenes, Música ni Vídeos.");
        }

        PcCatalog.RevealInExplorer(path);
        return new ActionResult(true, $"Encontré {Path.GetFileName(path)} y lo mostré en el Explorador.");
    }

    private static ActionResult? TryWindowAction(string text)
    {
        var command = text.Replace("por favor", string.Empty, StringComparison.Ordinal).Trim();
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }

        if (command is "minimiza la ventana" or "minimiza esta ventana" or "minimizar ventana")
        {
            ShowWindow(window, 6);
            return new ActionResult(true, "Ventana minimizada.");
        }

        if (command is "maximiza la ventana" or "maximiza esta ventana" or "maximizar ventana")
        {
            ShowWindow(window, 3);
            return new ActionResult(true, "Ventana maximizada.");
        }

        if (command is "restaura la ventana" or "restaurar ventana")
        {
            ShowWindow(window, 9);
            return new ActionResult(true, "Ventana restaurada.");
        }

        return null;
    }

    private static string? FindInstalledShortcut(string target)
    {
        if (target.Length < 3)
        {
            return null;
        }

        var roots = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs")
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                var shortcuts = Directory
                    .EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories)
                    .ToArray();
                var exact = shortcuts.FirstOrDefault(path =>
                    Normalize(Path.GetFileNameWithoutExtension(path)) == target);
                if (exact is not null)
                {
                    return exact;
                }

                var partial = shortcuts.FirstOrDefault(path =>
                    Normalize(Path.GetFileNameWithoutExtension(path)).Contains(
                        target,
                        StringComparison.Ordinal));
                if (partial is not null)
                {
                    return partial;
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static void OpenShell(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private static ActionResult? MediaAction(string text)
    {
        if (text is "sube el volumen" or "subir volumen" or "volumen arriba")
        {
            PressMediaKey(0xAF, 3);
            return new ActionResult(true, "Subí el volumen.");
        }

        if (text is "baja el volumen" or "bajar volumen" or "volumen abajo")
        {
            PressMediaKey(0xAE, 3);
            return new ActionResult(true, "Bajé el volumen.");
        }

        if (text is "silencio" or "silenciar" or "quita el sonido")
        {
            PressMediaKey(0xAD);
            return new ActionResult(true, "Cambié el estado de silencio.");
        }

        if (text is "pausa" or "continuar musica" or "reproducir" or "play")
        {
            PressMediaKey(0xB3);
            return new ActionResult(true, "Cambié la reproducción.");
        }

        if (text is "siguiente cancion" or "siguiente musica" or "pasa la cancion")
        {
            PressMediaKey(0xB0);
            return new ActionResult(true, "Pasé a la siguiente canción.");
        }

        if (text is "cancion anterior" or "musica anterior" or "vuelve a la cancion")
        {
            PressMediaKey(0xB1);
            return new ActionResult(true, "Volví a la canción anterior.");
        }

        return null;
    }

    private static void PressMediaKey(byte key, int count = 1)
    {
        for (var index = 0; index < count; index++)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KeyEventKeyUp, UIntPtr.Zero);
        }
    }

    private static ActionResult SystemStatus()
    {
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        var memory = new MemoryStatusEx();
        GlobalMemoryStatusEx(memory);
        var availableRam = memory.AvailablePhysical / 1024d / 1024d / 1024d;
        var totalRam = memory.TotalPhysical / 1024d / 1024d / 1024d;
        var usedPercent = totalRam <= 0 ? 0 : (1 - availableRam / totalRam) * 100;
        return new ActionResult(
            true,
            $"Windows de 64 bits; disco C con {drive.AvailableFreeSpace / 1024d / 1024d / 1024d:F1} GB libres; " +
            $"RAM al {usedPercent:F0} por ciento, con {availableRam:F1} GB disponibles.");
    }

    public const string HelpText = """
        Puedo ejecutar estas órdenes localmente:
        • qué hora es / qué fecha es
        • abre aplicaciones: calculadora, Paint, terminal o accesos del menú Inicio
        • abre carpetas: Descargas, Documentos, Escritorio, Imágenes o Este equipo
        • abre cualquier aplicación instalada por su nombre
        • abre <archivo> / busca el archivo <nombre>
        • abre configuración de Bluetooth, Wi-Fi, pantalla, sonido o Windows Update
        • minimiza, maximiza o restaura la ventana actual
        • abre Google, YouTube o Gmail
        • busca <algo>
        • anota <texto> / mis notas
        • recuerda que <dato> / qué recuerdas
        • sube volumen / baja volumen / silencio / pausa
        • siguiente canción / canción anterior
        • estado del sistema
        • termina la conversación / eso es todo

        Después de activarme una vez, puedes seguir hablando sin repetir mi nombre
        mientras aparezca “Conversación activa”. Para una pregunta normal usaré
        Codex Terra en modo de solo lectura.
        """;

    [GeneratedRegex(@"^(?:busca|buscar)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SearchRegex();

    [GeneratedRegex(@"^(?:anota|apunta|nota)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NoteRegex();

    [GeneratedRegex(@"^recuerda(?: que)?\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MemoryRegex();

    [GeneratedRegex(@"^(?:busca|encuentra|localiza|muestra|muestrame)\s+(?:(?:el|la|un|una)\s+)?(?:archivo|carpeta|documento|foto)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ContentSearchRegex();

    [GeneratedRegex(@"^(?:(?:oye|por|favor|puedes|podrias|quiero|quisiera|necesito|que|me|tu|lo|la|el)\s+)*(?:abre|abras|abreme|abrir|abrirme|inicia|iniciar|ejecuta|ejecutar|lanza|haber|a\s+ver|labras)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex OpenRegex();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
