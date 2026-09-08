using Jarvis.Native;
using NAudio.Wave;
using Whisper.net.Ggml;

if (Environment.GetEnvironmentVariable("JARVIS_INSTALL_WHISPER") == "1")
{
    var target = Environment.GetEnvironmentVariable("JARVIS_WHISPER_MODEL");
    if (string.IsNullOrWhiteSpace(target))
    {
        throw new InvalidOperationException("Define JARVIS_WHISPER_MODEL para instalar el modelo.");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    var temporary = target + ".download";
    try
    {
        Console.WriteLine("Descargando Whisper Small multilingüe (una sola vez)…");
        await using var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Small);
        await using (var destination = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None))
        {
            await source.CopyToAsync(destination);
        }

        File.Move(temporary, target, overwrite: true);
        Console.WriteLine($"Modelo instalado: {target}");
    }
    finally
    {
        if (File.Exists(temporary))
        {
            File.Delete(temporary);
        }
    }
}

var failures = new List<string>();

void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "OK" : "FALLO")}  {name}");
    if (!condition)
    {
        failures.Add(name);
    }
}

var whisperTestWave = Environment.GetEnvironmentVariable("JARVIS_TEST_WHISPER_WAV");
if (!string.IsNullOrWhiteSpace(whisperTestWave))
{
    var whisperModel = Environment.GetEnvironmentVariable("JARVIS_WHISPER_MODEL")!;
    using var reader = new WaveFileReader(whisperTestWave);
    Check(
        reader.WaveFormat.SampleRate == 16000 &&
        reader.WaveFormat.BitsPerSample == 16 &&
        reader.WaveFormat.Channels == 1,
        "Audio de prueba compatible con el micrófono");
    var pcm = new byte[reader.Length];
    await reader.ReadExactlyAsync(pcm);
    using var transcriber = await WhisperTranscriber.LoadAsync(whisperModel, "Jarvis");
    var whisperTimer = System.Diagnostics.Stopwatch.StartNew();
    var whisperText = await transcriber.TranscribePcm16Async(pcm);
    whisperTimer.Stop();
    Console.WriteLine($"WHISPER  {whisperText} ({whisperTimer.ElapsedMilliseconds} ms)");
    whisperTimer.Restart();
    var repeatedWhisperText = await transcriber.TranscribePcm16Async(pcm);
    whisperTimer.Stop();
    Console.WriteLine($"WHISPER  segunda frase: {whisperTimer.ElapsedMilliseconds} ms");
    Check(
        NativeActionEngine.Normalize(whisperText).Contains("abre spotify", StringComparison.Ordinal),
        "Whisper entiende 'abre Spotify' en español");
    Check(
        NativeActionEngine.Normalize(repeatedWhisperText).Contains("abre spotify", StringComparison.Ordinal),
        "Whisper mantiene la precisión en conversación continua");
    Check(
        NativeVoiceService.TryExtractWakeCommand("Jarvis", whisperText, out var extracted) &&
        extracted.StartsWith("abre spotify", StringComparison.Ordinal),
        "Whisper activa el nombre y extrae la orden completa");
}

RuntimePaths? paths = null;
try
{
    paths = RuntimePaths.Discover();
    Check(File.Exists(paths.CodexExecutable), "Codex app-server localizado");
}
catch (Exception exception)
{
    failures.Add("Rutas: " + exception.Message);
}

Check(AppConfig.Model == "gpt-5.6-terra", "Modelo fijado a Terra");
Check(!AppConfig.Model.Contains("luna", StringComparison.OrdinalIgnoreCase), "Luna está bloqueado");
Check(NativeActionEngine.Normalize("¿QUÉ día es?") == "¿que dia es?", "Normalización de órdenes en español");
Check(AssistantIdentity.Validate("  Alex  ") == "Alex", "Nombre personalizado validado");
Check(
    NativeVoiceService.TryExtractWakeCommand("Jarvis", "Jardis, abre Spotify", out var fuzzyWakeCommand) &&
    fuzzyWakeCommand == "abre spotify",
    "La activación tolera una transcripción parecida del nombre");
Check(ConversationCommands.IsEnd("Jarvis, finalizar", "Jarvis"),
    "'Jarvis finalizar' cierra la conversación");
Check(ConversationCommands.IsEnd("Alex finaliza la conversación", "Alex"),
    "El cierre verbal respeta el nombre personalizado");
Check(!ConversationCommands.IsEnd("Jarvis abre Spotify", "Jarvis"),
    "Una orden normal no se confunde con el cierre");
Check(CodexAppServerClient.RepairUtf8Mojibake("menÃº y aquÃ­") == "menú y aquí",
    "Texto UTF-8 en español reparado");
Check(VoiceCommandNormalizer.Normalize("puedes abrir esposa y fay") == "abre spotify",
    "Spotify se corrige desde 'esposa y fay'");
Check(VoiceCommandNormalizer.Normalize("puedas por favor haber spotifai") == "abre spotify",
    "Spotify se corrige desde 'haber spotifai'");
Check(VoiceCommandNormalizer.Normalize("podría por favor explota y ray") == "abre spotify",
    "Spotify se corrige desde 'explota y ray'");
Check(VoiceCommandNormalizer.Normalize("puedes decirme qué es spotifai") == "puedes decirme que es spotify",
    "Una pregunta sobre Spotify no se convierte en apertura");
Check(VoiceCommandNormalizer.Normalize("abre discor") == "abre discord",
    "Nombres frecuentes de otras aplicaciones también se corrigen");
Check(PcCatalog.FindInstalledApp("firefox")?.Name.Contains(
        "Firefox",
        StringComparison.OrdinalIgnoreCase) == true,
    "Catálogo general encuentra Firefox");
Check(PcCatalog.FindInstalledApp("discord")?.Name.Contains(
        "Discord",
        StringComparison.OrdinalIgnoreCase) == true,
    "Catálogo general encuentra Discord");
Check(PcCatalog.FindInstalledApp("diskord")?.Name.Contains(
        "Discord",
        StringComparison.OrdinalIgnoreCase) == true,
    "Catálogo tolera un nombre de aplicación parecido");
Check(PcCatalog.FindUserContent("README") is not null,
    "Catálogo encuentra archivos del usuario por nombre");

var testData = Path.Combine(Path.GetTempPath(), "JarvisNativeSmoke-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new LocalStateStore(testData);
    var engine = new NativeActionEngine(store);
    Check(engine.Execute("qué hora es").Handled, "Orden local reconocida");
    Check(engine.Execute("borra todos mis archivos").Message.Contains("bloqueada", StringComparison.OrdinalIgnoreCase),
        "Acciones destructivas bloqueadas");
    Check(engine.Execute("anota prueba nativa").Message.Contains("guardada", StringComparison.OrdinalIgnoreCase),
        "Notas locales operativas");
    Check(engine.Execute("quiero que me abras una aplicacion inexistente").Message.Contains(
            "No encontré",
            StringComparison.OrdinalIgnoreCase),
        "Órdenes naturales para abrir aplicaciones");
    Check(engine.Execute("puedes por favor abrir una aplicacion inexistente").Message.Contains(
            "No encontré",
            StringComparison.OrdinalIgnoreCase),
        "Órdenes amables para abrir aplicaciones");
    if (Environment.GetEnvironmentVariable("JARVIS_TEST_SPOTIFY") == "1")
    {
        Check(engine.Execute("puedas por favor haber spotifai").Message.Contains(
                "Abriendo spotify",
                StringComparison.OrdinalIgnoreCase),
            "Spotify abierto aun con la transcripción fonética incorrecta");
    }
    store.SetAssistantName("Alex");
    Check(new LocalStateStore(testData).GetAssistantName() == "Alex", "Nombre personalizado persistente");
}
finally
{
    if (Directory.Exists(testData))
    {
        Directory.Delete(testData, true);
    }
}

if (paths is not null)
{
    try
    {
        using var liveVoice = new NativeVoiceService("Alex");
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        liveVoice.StatusChanged += status =>
        {
            Console.WriteLine("VOZ  " + status);
            if (status.StartsWith("Escuchando", StringComparison.OrdinalIgnoreCase))
            {
                listening.TrySetResult();
            }
        };
        liveVoice.Faulted += message => Console.WriteLine("VOZ ERROR  " + message);
        await liveVoice.StartAsync();
        await listening.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Check(liveVoice.LanguageTag.StartsWith("es", StringComparison.OrdinalIgnoreCase),
            "C# abre el micrófono con reconocimiento español");
        Check(liveVoice.WakeName == "Alex", "Wake word reemplazado exclusivamente por el nuevo nombre");
        liveVoice.KeepConversationOpen();
        Check(liveVoice.ConversationActive, "La conversación continúa sin repetir el nombre");
        liveVoice.EndConversation();
        Check(!liveVoice.ConversationActive, "La conversación puede finalizarse explícitamente");
        await liveVoice.PauseAsync();
    }
    catch (Exception exception)
    {
        Console.WriteLine("FALLO  Escucha continua real: " + exception.Message);
        failures.Add("Escucha continua real: " + exception.Message);
    }

    if (Environment.GetEnvironmentVariable("JARVIS_TEST_TTS") == "1")
    {
        try
        {
            await new SpeechService().SpeakAsync(
                "Hola. Soy Jarvis. Ahora hablaré siempre en español.");
            Check(true, "Voz española reproducida");
        }
        catch (Exception exception)
        {
            Console.WriteLine("FALLO  Voz española: " + exception.Message);
            failures.Add("Voz española: " + exception.Message);
        }
    }

    if (Environment.GetEnvironmentVariable("JARVIS_SKIP_CODEX_TEST") != "1")
    {
        try
        {
            await using var codex = new CodexAppServerClient(paths);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var response = await codex.AskAsync("Responde únicamente con: NATIVE_CODEX_OK", timeout.Token);
            Check(response.Contains("NATIVE_CODEX_OK", StringComparison.Ordinal), "Codex app-server responde desde C#");
        }
        catch (Exception exception)
        {
            Console.WriteLine("FALLO  Integración Codex: " + exception.Message);
            failures.Add("Integración Codex: " + exception.Message);
        }
    }
}

if (failures.Count > 0)
{
    Console.WriteLine($"\n{failures.Count} prueba(s) fallaron.");
    return 1;
}

Console.WriteLine("\nTodas las pruebas nativas pasaron.");
return 0;
