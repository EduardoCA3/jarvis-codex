using System.Text;
using Whisper.net;

namespace Jarvis.Native;

public sealed class WhisperTranscriber : IDisposable
{
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperTranscriber(string modelPath, string assistantName)
    {
        _factory = WhisperFactory.FromPath(modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage("es")
            .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 4, 8))
            .WithPrompt(
                $"El nombre exacto del asistente es {assistantName}. " +
                $"El usuario comienza diciendo {assistantName} y habla español latino. " +
                "Aplicaciones: Spotify, Google Chrome, Discord, Firefox, WhatsApp, Steam, " +
                "CapCut, calculadora, Explorador de archivos.")
            .Build();
    }

    public static Task<WhisperTranscriber> LoadAsync(
        string modelPath,
        string assistantName,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new WhisperTranscriber(modelPath, assistantName);
        }, cancellationToken);

    public async Task<string> TranscribePcm16Async(
        byte[] pcm16,
        CancellationToken cancellationToken = default)
    {
        if (pcm16.Length < 2)
        {
            return string.Empty;
        }

        var samples = new float[pcm16.Length / 2];
        var peak = 0;
        for (var index = 0; index < samples.Length; index++)
        {
            var offset = index * 2;
            var value = Math.Abs((int)(short)(pcm16[offset] | pcm16[offset + 1] << 8));
            peak = Math.Max(peak, value);
        }

        // Algunos micrófonos entregan frases a muy poco volumen. Normalizamos
        // solamente para Whisper, sin modificar ni guardar el audio original.
        var gain = peak > 0
            ? Math.Clamp(24500f / peak, 1f, 8f)
            : 1f;
        for (var index = 0; index < samples.Length; index++)
        {
            var offset = index * 2;
            var sample = (short)(pcm16[offset] | pcm16[offset + 1] << 8);
            samples[index] = Math.Clamp(sample / 32768f * gain, -1f, 1f);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var text = new StringBuilder();
            await foreach (var segment in _processor
                               .ProcessAsync(samples, cancellationToken)
                               .ConfigureAwait(false))
            {
                text.Append(' ').Append(segment.Text);
            }

            return Clean(text.ToString());
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Clean(string text)
    {
        var cleaned = text
            .Replace("[MÚSICA]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("[MUSIC]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(música)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return cleaned.Trim(' ', '.', ',', '¿', '?', '¡', '!');
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
        _gate.Dispose();
    }
}
