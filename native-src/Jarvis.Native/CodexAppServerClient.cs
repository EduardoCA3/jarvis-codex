using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Jarvis.Native;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    public string AssistantName { get; set; } = "Jarvis";

    private sealed class ActiveTurn
    {
        public StringBuilder Answer { get; } = new();
        public TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly RuntimePaths _paths;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _turnLock = new(1, 1);
    private readonly object _activeSync = new();
    private Process? _process;
    private CancellationTokenSource? _lifetime;
    private long _nextId;
    private string? _threadId;
    private ActiveTurn? _activeTurn;

    public CodexAppServerClient(RuntimePaths paths) => _paths = paths;

    public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        await _turnLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var active = new ActiveTurn();
            lock (_activeSync)
            {
                _activeTurn = active;
            }

            var prompt = $"""
                Eres {AssistantIdentity.Validate(AssistantName)}, un asistente personal en español latino. Responde siempre en español claro y breve, normalmente en menos de 120 palabras. No uses inglés salvo que el usuario pida explícitamente una traducción o necesite un término técnico sin equivalente. No ejecutes acciones ni modifiques archivos: el cliente ya controla las acciones locales. Si no sabes algo, dilo. Petición del usuario:

                {question}
                """;

            try
            {
                await SendRequestAsync(
                    "turn/start",
                    new
                    {
                        threadId = _threadId,
                        input = new[] { new { type = "text", text = prompt } },
                        model = AppConfig.Model,
                        effort = "low",
                        sandboxPolicy = new { type = "readOnly" }
                    },
                    cancellationToken).ConfigureAwait(false);

                return await active.Completion.Task
                    .WaitAsync(TimeSpan.FromMinutes(3), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                lock (_activeSync)
                {
                    if (ReferenceEquals(_activeTurn, active))
                    {
                        _activeTurn = null;
                    }
                }
            }
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("Codex tardó demasiado en responder.");
        }
        finally
        {
            _turnLock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && _threadId is not null)
        {
            return;
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await StopServerAsync().ConfigureAwait(false);
                await StartAndInitializeAsync(cancellationToken).ConfigureAwait(false);
                VoiceDiagnostics.Write("codex_initialized", $"attempt={attempt}; model={AppConfig.Model}");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                VoiceDiagnostics.Write(
                    "codex_initialize_failed",
                    $"attempt={attempt}; 0x{exception.HResult:X8}; {exception.Message}");
                await StopServerAsync(exception).ConfigureAwait(false);
                if (attempt < 2)
                {
                    await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new InvalidOperationException(
            "Codex no pudo iniciar después de dos intentos. Las órdenes locales siguen disponibles.",
            lastError);
    }

    private async Task StartAndInitializeAsync(CancellationToken cancellationToken)
    {
        _lifetime = new CancellationTokenSource();
        var startInfo = new ProcessStartInfo
        {
            FileName = _paths.CodexExecutable,
            WorkingDirectory = _paths.ProjectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // JSONL no admite el BOM que Encoding.UTF8 puede escribir al abrir stdin.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.Environment.Remove("OPENAI_API_KEY");
        startInfo.Environment.Remove("CODEX_API_KEY");
        startInfo.Environment["NO_COLOR"] = "1";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process = process;
        if (!process.Start())
        {
            throw new InvalidOperationException("No pude iniciar Codex app-server.");
        }

        VoiceDiagnostics.Write("codex_process_started", $"pid={process.Id}");
        _ = ReadOutputAsync(process, _lifetime.Token);
        _ = DrainErrorsAsync(process, _lifetime.Token);

        await SendRequestAsync(
            "initialize",
            new
            {
                clientInfo = new { name = "jarvis_native", title = "Jarvis Codex", version = "1.0.0" }
            },
            cancellationToken).ConfigureAwait(false);
        await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);

        var thread = await SendRequestAsync(
            "thread/start",
            new
            {
                model = AppConfig.Model,
                cwd = _paths.ProjectRoot,
                approvalPolicy = "never",
                sandbox = "read-only",
                personality = "friendly",
                serviceName = "jarvis_native"
            },
            cancellationToken).ConfigureAwait(false);

        if (!thread.TryGetProperty("thread", out var threadObject) ||
            !threadObject.TryGetProperty("id", out var idElement))
        {
            throw new InvalidOperationException("Codex no devolvió un identificador de conversación.");
        }

        _threadId = idElement.GetString();
    }

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        if (_process is null)
        {
            throw new InvalidOperationException("Codex app-server no está iniciado.");
        }

        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            await WriteAsync(new { method, id, @params = parameters }, cancellationToken).ConfigureAwait(false);
            var timeout = method == "initialize"
                ? TimeSpan.FromSeconds(20)
                : TimeSpan.FromSeconds(45);
            return await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException($"Codex no respondió a {method}.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken) =>
        WriteAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteAsync(object message, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
        {
            throw new InvalidOperationException("Codex app-server no está disponible.");
        }

        var json = JsonSerializer.Serialize(message);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadOutputAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                if (root.TryGetProperty("id", out var idElement) &&
                    idElement.ValueKind == JsonValueKind.Number &&
                    !root.TryGetProperty("method", out _))
                {
                    CompleteRequest(idElement.GetInt64(), root);
                    continue;
                }

                if (!root.TryGetProperty("method", out var methodElement))
                {
                    continue;
                }

                if (root.TryGetProperty("id", out var serverRequestId))
                {
                    await WriteAsync(
                        new
                        {
                            id = serverRequestId.GetInt64(),
                            error = new { code = -32601, message = "Jarvis no permite aprobaciones interactivas." }
                        },
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                HandleNotification(methodElement.GetString() ?? "", root);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                FailAll(new InvalidOperationException("Codex app-server cerró la conexión."));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            FailAll(new InvalidOperationException($"Error de comunicación con Codex: {exception.Message}", exception));
        }
    }

    private void CompleteRequest(long id, JsonElement root)
    {
        if (!_pending.TryRemove(id, out var completion))
        {
            return;
        }

        if (root.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : error.ToString();
            completion.TrySetException(new InvalidOperationException(message ?? "Codex devolvió un error."));
            return;
        }

        completion.TrySetResult(
            root.TryGetProperty("result", out var result) ? result.Clone() : default);
    }

    private void HandleNotification(string method, JsonElement root)
    {
        ActiveTurn? active;
        lock (_activeSync)
        {
            active = _activeTurn;
        }

        if (active is null || !root.TryGetProperty("params", out var parameters))
        {
            return;
        }

        if (method == "item/completed" && parameters.TryGetProperty("item", out var item) &&
            item.TryGetProperty("type", out var type) && type.GetString() == "agentMessage" &&
            item.TryGetProperty("text", out var text))
        {
            var phase = item.TryGetProperty("phase", out var phaseElement) ? phaseElement.GetString() : null;
            if (phase is null or "final_answer")
            {
                active.Answer.Clear();
                active.Answer.Append(RepairUtf8Mojibake(text.GetString()));
            }

            return;
        }

        if (method == "turn/completed")
        {
            var answer = active.Answer.ToString().Trim();
            if (parameters.TryGetProperty("turn", out var turn) &&
                turn.TryGetProperty("status", out var status) &&
                status.GetString() is "failed" or "interrupted")
            {
                var errorText = turn.TryGetProperty("error", out var error) ? error.ToString() : "La respuesta fue interrumpida.";
                active.Completion.TrySetException(new InvalidOperationException(errorText));
            }
            else if (answer.Length == 0)
            {
                active.Completion.TrySetException(new InvalidOperationException("Codex terminó sin texto de respuesta."));
            }
            else
            {
                active.Completion.TrySetResult(answer);
            }
        }
    }

    public static string RepairUtf8Mojibake(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            (!value.Contains('Ã') && !value.Contains('Â')))
        {
            return value ?? string.Empty;
        }

        try
        {
            var repaired = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(value));
            return repaired.Contains('\uFFFD') ? value : repaired;
        }
        catch
        {
            return value;
        }
    }

    private static async Task DrainErrorsAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                Debug.WriteLine("Codex app-server: " + line);
                VoiceDiagnostics.Write("codex_stderr", line);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void FailAll(Exception exception)
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }

        lock (_activeSync)
        {
            _activeTurn?.Completion.TrySetException(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopServerAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        _turnLock.Dispose();
    }

    private async Task StopServerAsync(Exception? reason = null)
    {
        _threadId = null;
        var lifetime = Interlocked.Exchange(ref _lifetime, null);
        var process = _process;
        _process = null;
        lifetime?.Cancel();
        if (reason is not null)
        {
            FailAll(reason);
        }

        if (process is { HasExited: false })
        {
            try
            {
                process.Kill(true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
                VoiceDiagnostics.Write("codex_stop_warning", exception.Message);
            }
        }

        process?.Dispose();
        lifetime?.Dispose();
    }
}
