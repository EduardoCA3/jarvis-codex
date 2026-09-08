using System.Text.Json;
using NAudio.Wave;
using Vosk;

namespace Jarvis.Native;

public sealed class NativeVoiceService : IDisposable
{
    private const int SampleRate = 16000;
    private const int MaxUtteranceBytes = SampleRate * 2 * 30;
    private static readonly TimeSpan ConversationWindow = TimeSpan.FromMinutes(2);
    private readonly object _sync = new();
    private readonly SpeechService _speech = new();
    private readonly MemoryStream _utterancePcm = new();
    private readonly string _wakeName;
    private Model? _model;
    private VoskRecognizer? _recognizer;
    private WhisperTranscriber? _whisper;
    private WaveInEvent? _microphone;
    private CancellationTokenSource? _commandTimeout;
    private bool _enabled;
    private bool _recording;
    private bool _suppressAudio;
    private bool _awaitingCommand;
    private long _lastPartialUpdate;
    private long _conversationUntilUtcTicks;
    private int _dispatching;
    private int _disposed;

    public event Action<string>? StatusChanged;
    public event Action<double>? WakeDetected;
    public event Action<string>? TranscriptReceived;
    public event Action<string>? NoCommandRecognized;
    public event Action<string>? Faulted;

    public bool IsRunning => _enabled && _recording;
    public string LanguageTag => "es · Whisper local";
    public string WakeName => _wakeName;
    public bool ConversationActive =>
        DateTime.UtcNow.Ticks < Interlocked.Read(ref _conversationUntilUtcTicks);

    public NativeVoiceService(string wakeName = "Jarvis")
    {
        _wakeName = AssistantIdentity.Validate(wakeName);
        VoiceDiagnostics.Write("offline_voice_created", $"name={_wakeName}");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        StatusChanged?.Invoke("Cargando Whisper local en español…");

        var modelPath = Environment.GetEnvironmentVariable("JARVIS_VOICE_MODEL");
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            modelPath = Path.Combine(
                AppContext.BaseDirectory,
                "models",
                "vosk-model-small-es-0.42");
        }
        if (!Directory.Exists(modelPath))
        {
            throw new DirectoryNotFoundException(
                $"Falta el modelo de voz local en {modelPath}.");
        }

        var whisperPath = Environment.GetEnvironmentVariable("JARVIS_WHISPER_MODEL");
        if (string.IsNullOrWhiteSpace(whisperPath))
        {
            whisperPath = Path.Combine(
                AppContext.BaseDirectory,
                "models",
                "whisper",
                "ggml-small.bin");
        }
        if (!File.Exists(whisperPath))
        {
            throw new FileNotFoundException(
                $"Falta el modelo Whisper local en {whisperPath}.",
                whisperPath);
        }

        var voskLoad = Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Vosk.Vosk.SetLogLevel(-1);
            var model = new Model(modelPath);
            var recognizer = CreateRecognizer(model);
            lock (_sync)
            {
                _model = model;
                _recognizer = recognizer;
            }
        }, cancellationToken);
        var whisperLoad = WhisperTranscriber.LoadAsync(
            whisperPath,
            _wakeName,
            cancellationToken);
        await Task.WhenAll(voskLoad, whisperLoad).ConfigureAwait(false);
        _whisper = await whisperLoad.ConfigureAwait(false);
        VoiceDiagnostics.Write(
            "whisper_loaded",
            $"model={Path.GetFileName(whisperPath)}; language=es; threads={Math.Clamp(Environment.ProcessorCount / 2, 4, 8)}");

        _enabled = true;
        StartMicrophone();
    }

    public Task PauseAsync()
    {
        _enabled = false;
        _suppressAudio = true;
        _awaitingCommand = false;
        CancelCommandTimeout();
        StopMicrophone();
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_sync)
        {
            _recognizer?.Reset();
            _utterancePcm.SetLength(0);
        }

        Interlocked.Exchange(ref _dispatching, 0);
        _awaitingCommand = false;
        _suppressAudio = false;
        _enabled = true;
        StartMicrophone();
        return Task.CompletedTask;
    }

    public void KeepConversationOpen()
    {
        var until = DateTime.UtcNow.Add(ConversationWindow).Ticks;
        Interlocked.Exchange(
            ref _conversationUntilUtcTicks,
            until);
        _ = Task.Run(async () =>
        {
            await Task.Delay(ConversationWindow).ConfigureAwait(false);
            if (_enabled && Interlocked.Read(ref _conversationUntilUtcTicks) == until)
            {
                Interlocked.Exchange(ref _conversationUntilUtcTicks, 0);
                StatusChanged?.Invoke($"Escuchando \"{_wakeName}\" · di mi nombre para comenzar");
            }
        });
    }

    public void EndConversation() =>
        Interlocked.Exchange(ref _conversationUntilUtcTicks, 0);

    private static VoskRecognizer CreateRecognizer(Model model)
    {
        var recognizer = new VoskRecognizer(model, SampleRate);
        recognizer.SetMaxAlternatives(0);
        recognizer.SetWords(false);
        return recognizer;
    }

    private void StartMicrophone()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        try
        {
            lock (_sync)
            {
                if (_recording)
                {
                    return;
                }

                if (WaveIn.DeviceCount == 0)
                {
                    throw new InvalidOperationException("Windows no encontró ningún micrófono activo.");
                }

                _microphone ??= CreateMicrophone();
                _microphone.StartRecording();
                _recording = true;
            }

            var deviceName = WaveIn.GetCapabilities(0).ProductName;
            VoiceDiagnostics.Write("offline_mic_started", $"device={deviceName}; rate={SampleRate}");
            StatusChanged?.Invoke(ConversationActive
                ? "Conversación activa · habla sin repetir el nombre"
                : $"Escuchando \"{_wakeName}\" · {deviceName} · offline");
        }
        catch (Exception exception)
        {
            _recording = false;
            VoiceDiagnostics.Write("offline_mic_error", $"0x{exception.HResult:X8}; {exception.Message}");
            Faulted?.Invoke(FriendlyVoiceError(exception));
            throw;
        }
    }

    private WaveInEvent CreateMicrophone()
    {
        var microphone = new WaveInEvent
        {
            DeviceNumber = 0,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 80,
            NumberOfBuffers = 4
        };
        microphone.DataAvailable += OnAudioAvailable;
        microphone.RecordingStopped += OnRecordingStopped;
        return microphone;
    }

    private void StopMicrophone()
    {
        WaveInEvent? microphone;
        lock (_sync)
        {
            if (!_recording)
            {
                return;
            }

            _recording = false;
            microphone = _microphone;
        }

        try
        {
            microphone?.StopRecording();
        }
        catch (Exception exception)
        {
            VoiceDiagnostics.Write("offline_mic_stop_warning", exception.Message);
        }
    }

    private void OnAudioAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (!_enabled || _suppressAudio || eventArgs.BytesRecorded == 0)
        {
            return;
        }

        try
        {
            string json;
            var isFinal = false;
            byte[] utterance = [];
            lock (_sync)
            {
                if (_recognizer is null || !_enabled || _suppressAudio)
                {
                    return;
                }

                _utterancePcm.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
                if (_utterancePcm.Length > MaxUtteranceBytes)
                {
                    _utterancePcm.SetLength(0);
                    _utterancePcm.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
                }

                isFinal = _recognizer.AcceptWaveform(eventArgs.Buffer, eventArgs.BytesRecorded);
                json = isFinal ? _recognizer.Result() : _recognizer.PartialResult();
                if (isFinal)
                {
                    utterance = _utterancePcm.ToArray();
                    _utterancePcm.SetLength(0);
                }
            }

            var text = ReadVoskText(json, isFinal ? "text" : "partial");
            if (isFinal)
            {
                HandleFinalText(text.Trim(), utterance);
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                ShowPartialText(text.Trim());
            }
        }
        catch (Exception exception)
        {
            VoiceDiagnostics.Write("offline_recognition_error", $"0x{exception.HResult:X8}; {exception.Message}");
            Faulted?.Invoke(FriendlyVoiceError(exception));
        }
    }

    private void ShowPartialText(string text)
    {
        if (!_awaitingCommand && !ConversationActive)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastPartialUpdate) < 350)
        {
            return;
        }

        Interlocked.Exchange(ref _lastPartialUpdate, now);
        StatusChanged?.Invoke("Escuchando tu voz…");
    }

    private void HandleFinalText(string text, byte[] utterance)
    {
        var normalized = NormalizeSpeechText(text);
        VoiceDiagnostics.Write(
            "offline_final",
            $"words={normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length}");

        if (_awaitingCommand)
        {
            BeginWhisperTranscription(
                utterance,
                includesWakeName: false,
                fallbackCommand: normalized,
                signalWake: false);
            return;
        }

        if (TryExtractCommand(normalized, out var command))
        {
            if (command.Length == 0)
            {
                BeginCommandPrompt();
            }
            else
            {
                BeginWhisperTranscription(
                    utterance,
                    includesWakeName: true,
                    fallbackCommand: command,
                    signalWake: true);
            }

            return;
        }

        if (ConversationActive && (normalized.Length > 0 || HasVoice(utterance)))
        {
            BeginWhisperTranscription(
                utterance,
                includesWakeName: false,
                fallbackCommand: normalized,
                signalWake: false);
            return;
        }

        // Vosk es rápido, pero puede deformar incluso el nombre de activación.
        // Whisper vuelve a comprobar localmente cualquier frase con voz real.
        if (normalized.Length > 0 || HasVoice(utterance))
        {
            BeginWhisperWakeCheck(utterance);
            return;
        }

        StatusChanged?.Invoke($"Escuchando \"{_wakeName}\" · offline");
    }

    private void BeginWhisperWakeCheck(byte[] utterance)
    {
        if (_whisper is null ||
            Interlocked.CompareExchange(ref _dispatching, 1, 0) != 0)
        {
            return;
        }

        _suppressAudio = true;
        StatusChanged?.Invoke("Verificando si dijiste mi nombre…");
        _ = TranscribeWakeCheckAsync(utterance);
    }

    private async Task TranscribeWakeCheckAsync(byte[] utterance)
    {
        var started = Environment.TickCount64;
        try
        {
            var transcript = await _whisper!
                .TranscribePcm16Async(utterance)
                .ConfigureAwait(false);
            var normalized = NormalizeSpeechText(transcript);
            if (!TryExtractCommand(normalized, out var command))
            {
                VoiceDiagnostics.Write(
                    "whisper_wake_not_found",
                    $"words={normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length}; ms={Environment.TickCount64 - started}");
                ReleaseIgnoredSpeech();
                return;
            }

            VoiceDiagnostics.Write(
                "whisper_wake_accepted",
                $"mode={(command.Length == 0 ? "two_step" : "single_phrase")}; ms={Environment.TickCount64 - started}");
            if (command.Length == 0)
            {
                Interlocked.Exchange(ref _dispatching, 0);
                BeginCommandPrompt();
                return;
            }

            WakeDetected?.Invoke(0.98);
            EmitReservedCommand(command, transcript);
        }
        catch (Exception exception)
        {
            VoiceDiagnostics.Write(
                "whisper_wake_check_error",
                $"0x{exception.HResult:X8}; {exception.Message}");
            ReleaseIgnoredSpeech();
        }
    }

    private void ReleaseIgnoredSpeech()
    {
        lock (_sync)
        {
            _recognizer?.Reset();
            _utterancePcm.SetLength(0);
        }

        Interlocked.Exchange(ref _dispatching, 0);
        _suppressAudio = false;
        StatusChanged?.Invoke($"Escuchando \"{_wakeName}\" · offline");
    }

    private void BeginWhisperTranscription(
        byte[] utterance,
        bool includesWakeName,
        string fallbackCommand,
        bool signalWake)
    {
        if (utterance.Length == 0 ||
            Interlocked.CompareExchange(ref _dispatching, 1, 0) != 0)
        {
            return;
        }

        _suppressAudio = true;
        _awaitingCommand = false;
        CancelCommandTimeout();
        if (signalWake)
        {
            WakeDetected?.Invoke(0.95);
        }

        StatusChanged?.Invoke("Entendiendo tu voz con Whisper…");
        _ = TranscribeAndDispatchAsync(utterance, includesWakeName, fallbackCommand);
    }

    private async Task TranscribeAndDispatchAsync(
        byte[] utterance,
        bool includesWakeName,
        string fallbackCommand)
    {
        try
        {
            var transcript = _whisper is null
                ? fallbackCommand
                : await _whisper.TranscribePcm16Async(utterance).ConfigureAwait(false);
            var normalized = NormalizeSpeechText(transcript);
            var command = normalized;
            if (includesWakeName && !TryExtractCommand(normalized, out command))
            {
                command = RemoveLikelyWakePrefix(normalized, fallbackCommand);
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                command = fallbackCommand;
            }

            EmitReservedCommand(command, transcript);
        }
        catch (Exception exception)
        {
            VoiceDiagnostics.Write(
                "whisper_transcription_error",
                $"0x{exception.HResult:X8}; {exception.Message}");
            if (!string.IsNullOrWhiteSpace(fallbackCommand))
            {
                EmitReservedCommand(fallbackCommand, fallbackCommand);
                return;
            }

            Interlocked.Exchange(ref _dispatching, 0);
            _suppressAudio = false;
            NoCommandRecognized?.Invoke("No entendí esa frase. Puedes repetirla.");
        }
    }

    private void EmitReservedCommand(string command, string transcript)
    {
        var correctedCommand = VoiceCommandNormalizer.Normalize(command);
        if (string.IsNullOrWhiteSpace(correctedCommand))
        {
            Interlocked.Exchange(ref _dispatching, 0);
            _suppressAudio = false;
            NoCommandRecognized?.Invoke("No entendí esa frase. Puedes repetirla.");
            return;
        }

        VoiceDiagnostics.Write(
            "whisper_command_recognized",
            $"words={correctedCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length}; corrected={!string.Equals(NormalizeSpeechText(transcript), correctedCommand, StringComparison.Ordinal)}");
        StatusChanged?.Invoke("Orden entendida con Whisper");
        TranscriptReceived?.Invoke(correctedCommand);
    }

    private static string RemoveLikelyWakePrefix(string transcript, string fallbackCommand)
    {
        if (transcript.Length == 0)
        {
            return fallbackCommand;
        }

        var commandStarts = new[]
        {
            "abre ", "abrir ", "inicia ", "ejecuta ", "busca ", "anota ",
            "recuerda ", "sube ", "baja ", "pausa", "siguiente ", "que ",
            "como ", "cuando ", "donde ", "por que ", "dime "
        };
        if (commandStarts.Any(transcript.StartsWith))
        {
            return transcript;
        }

        var firstSpace = transcript.IndexOf(' ');
        return firstSpace > 0 ? transcript[(firstSpace + 1)..].Trim() : fallbackCommand;
    }

    private static string NormalizeSpeechText(string text)
    {
        var normalized = NativeActionEngine.Normalize(text);
        var characters = normalized.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or ' '
                ? character
                : ' ');
        return string.Join(
            ' ',
            new string(characters.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool HasVoice(byte[] pcm16)
    {
        if (pcm16.Length < SampleRate / 2)
        {
            return false;
        }

        long total = 0;
        var samples = pcm16.Length / 2;
        for (var index = 0; index < samples; index += 4)
        {
            var offset = index * 2;
            var sample = (short)(pcm16[offset] | pcm16[offset + 1] << 8);
            total += Math.Abs((int)sample);
        }

        return total / Math.Max(1, samples / 4) > 220;
    }

    private void BeginCommandPrompt()
    {
        if (Interlocked.CompareExchange(ref _dispatching, 1, 0) != 0)
        {
            return;
        }

        _suppressAudio = true;
        lock (_sync)
        {
            _recognizer?.Reset();
        }

        VoiceDiagnostics.Write("offline_wake_accepted", "mode=two_step");
        _ = PromptForCommandAsync();
    }

    private async Task PromptForCommandAsync()
    {
        try
        {
            WakeDetected?.Invoke(0.9);
            StatusChanged?.Invoke("Activado · responderé y después escucharé tu orden");
            await _speech.SpeakAsync("Dime.").ConfigureAwait(false);

            lock (_sync)
            {
                _recognizer?.Reset();
                _utterancePcm.SetLength(0);
            }

            _awaitingCommand = true;
            _suppressAudio = false;
            Interlocked.Exchange(ref _dispatching, 0);
            StatusChanged?.Invoke("Escuchando tu orden…");
            StartCommandTimeout();
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _dispatching, 0);
            _suppressAudio = false;
            Faulted?.Invoke("No pude reproducir la confirmación de voz: " + exception.Message);
        }
    }

    private bool TryExtractCommand(string text, out string command)
    {
        return TryExtractWakeCommand(_wakeName, text, out command);
    }

    public static bool TryExtractWakeCommand(string wakeName, string transcript, out string command)
    {
        var normalizedTranscript = NormalizeSpeechText(transcript);
        var variants = BuildWakeVariants(AssistantIdentity.Validate(wakeName));
        foreach (var variant in variants.OrderByDescending(value => value.Length))
        {
            foreach (var prefix in new[] { variant, "oye " + variant, "hey " + variant })
            {
                if (normalizedTranscript.Equals(prefix, StringComparison.Ordinal))
                {
                    command = string.Empty;
                    return true;
                }

                if (normalizedTranscript.StartsWith(prefix + " ", StringComparison.Ordinal))
                {
                    command = normalizedTranscript[(prefix.Length + 1)..].Trim();
                    return true;
                }
            }
        }

        var words = normalizedTranscript.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var offset = words.Length > 0 && words[0] is "oye" or "hey" ? 1 : 0;
        foreach (var variant in variants)
        {
            var wakeWords = variant.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < offset + wakeWords.Length)
            {
                continue;
            }

            var heardName = string.Join(' ', words.Skip(offset).Take(wakeWords.Length));
            if (heardName.Length < 4 || TextSimilarity(heardName, variant) < 0.67)
            {
                continue;
            }

            command = string.Join(' ', words.Skip(offset + wakeWords.Length));
            return true;
        }

        command = string.Empty;
        return false;
    }

    private static double TextSimilarity(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitution);
            }

            (previous, current) = (current, previous);
        }

        return 1d - previous[right.Length] / (double)Math.Max(left.Length, right.Length);
    }

    private void StartCommandTimeout()
    {
        CancelCommandTimeout();
        var timeout = new CancellationTokenSource();
        _commandTimeout = timeout;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(9), timeout.Token).ConfigureAwait(false);
                if (!_enabled || !_awaitingCommand)
                {
                    return;
                }

                _awaitingCommand = false;
                NoCommandRecognized?.Invoke($"No oí una orden. Di {_wakeName} para intentarlo otra vez.");
                StatusChanged?.Invoke($"Escuchando \"{_wakeName}\" · offline");
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void CancelCommandTimeout()
    {
        var timeout = Interlocked.Exchange(ref _commandTimeout, null);
        if (timeout is null)
        {
            return;
        }

        timeout.Cancel();
        timeout.Dispose();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        _recording = false;
        if (eventArgs.Exception is null || !_enabled)
        {
            return;
        }

        VoiceDiagnostics.Write("offline_recording_stopped", eventArgs.Exception.Message);
        Faulted?.Invoke(FriendlyVoiceError(eventArgs.Exception));
    }

    private static string ReadVoskText(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(property, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string[] BuildWakeVariants(string wakeName)
    {
        var normalized = NativeActionEngine.Normalize(wakeName);
        if (normalized != "jarvis")
        {
            return [normalized];
        }

        return [
            "jarvis", "yarvis", "harvis", "charvis", "jervis", "darvis",
            "yarbis", "jarbis", "ya ves", "llaves", "servis"
        ];
    }

    private static string FriendlyVoiceError(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("BadDeviceId", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("NoDriver", StringComparison.OrdinalIgnoreCase))
        {
            return "No pude abrir el micrófono seleccionado. Comprueba que siga conectado.";
        }

        if (message.Contains("Allocated", StringComparison.OrdinalIgnoreCase))
        {
            return "Otra aplicación está usando el micrófono en modo exclusivo.";
        }

        return "El reconocimiento local de voz falló: " + message;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _enabled = false;
        _suppressAudio = true;
        CancelCommandTimeout();
        StopMicrophone();

        lock (_sync)
        {
            if (_microphone is not null)
            {
                _microphone.DataAvailable -= OnAudioAvailable;
                _microphone.RecordingStopped -= OnRecordingStopped;
                _microphone.Dispose();
                _microphone = null;
            }

            _recognizer?.Dispose();
            _recognizer = null;
            _model?.Dispose();
            _model = null;
        }

        _whisper?.Dispose();
        _whisper = null;
        _utterancePcm.Dispose();
    }
}
