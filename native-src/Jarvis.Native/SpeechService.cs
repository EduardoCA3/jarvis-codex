using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Jarvis.Native;

public sealed class SpeechService
{
    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var synthesizer = new SpeechSynthesizer();
        var voice = SelectSpanishVoice();
        synthesizer.Voice = voice;
        VoiceDiagnostics.Write(
            "tts_voice_selected",
            $"name={voice.DisplayName}; language={voice.Language}; gender={voice.Gender}");

        var spokenText = text.Length > 4000 ? text[..4000] : text;
        using var stream = await synthesizer.SynthesizeTextToStreamAsync(spokenText);
        using var source = MediaSource.CreateFromStream(stream, stream.ContentType);
        using var player = new MediaPlayer
        {
            Source = source,
            Volume = 1.0,
            AudioCategory = MediaPlayerAudioCategory.Speech
        };

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnEnded(MediaPlayer sender, object args) => completion.TrySetResult();
        void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
            completion.TrySetException(
                new InvalidOperationException("Windows no pudo reproducir la voz española: " + args.ErrorMessage));

        player.MediaEnded += OnEnded;
        player.MediaFailed += OnFailed;
        using var registration = cancellationToken.Register(() =>
        {
            player.Pause();
            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            player.Play();
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            player.MediaEnded -= OnEnded;
            player.MediaFailed -= OnFailed;
        }
    }

    private static VoiceInformation SelectSpanishVoice()
    {
        var spanishVoices = SpeechSynthesizer.AllVoices
            .Where(voice => voice.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return spanishVoices.FirstOrDefault(voice =>
                   voice.DisplayName.Contains("Raul", StringComparison.OrdinalIgnoreCase) ||
                   voice.Description.Contains("Raul", StringComparison.OrdinalIgnoreCase))
               ?? spanishVoices.FirstOrDefault()
               ?? throw new InvalidOperationException(
                   "Windows no tiene instalada una voz de salida en español.");
    }
}
