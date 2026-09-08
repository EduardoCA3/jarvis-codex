using System.Text.RegularExpressions;

namespace Jarvis.Native;

/// <summary>
/// Corrige errores fonéticos frecuentes del modelo de voz antes de buscar una
/// aplicación. No intenta "traducir" lo dicho ni cambia preguntas normales.
/// </summary>
public static partial class VoiceCommandNormalizer
{
    private sealed record AppAlias(string CanonicalName, string[] Variants);

    private static readonly AppAlias[] AppAliases =
    [
        new("spotify",
        [
            "spotify", "spotifai", "espotifai", "es poti fai", "es poti fay",
            "es pot y fai", "es pot y fay", "esposa y fai", "esposa y fay",
            "explota y rai", "explota y ray", "es por ti fai", "es por ti fay",
            "spot y fai", "spot y fay"
        ]),
        new("google chrome", ["google chrome", "google crom", "gugel crom", "chrome", "crom"]),
        new("discord", ["discord", "discor", "diskord", "discordia"]),
        new("firefox", ["firefox", "fire fox", "fairefox", "firefocs"]),
        new("whatsapp", ["whatsapp", "whats app", "guasap", "wasap"]),
        new("steam", ["steam", "stin", "estim"])
    ];

    public static string Normalize(string recognizedText)
    {
        var text = CollapseSpacesRegex().Replace(
            NativeActionEngine.Normalize(recognizedText),
            " ").Trim();

        foreach (var app in AppAliases)
        {
            var variant = app.Variants
                .OrderByDescending(value => value.Length)
                .FirstOrDefault(value => ContainsPhrase(text, value));
            if (variant is null)
            {
                continue;
            }

            var corrected = ReplacePhrase(text, variant, app.CanonicalName);
            var explicitLaunch = LaunchIntentRegex().IsMatch(text);
            var inferredLaunch = !variant.Equals(app.CanonicalName, StringComparison.Ordinal) &&
                                 CourtesyCommandRegex().IsMatch(text) &&
                                 !QuestionIntentRegex().IsMatch(text);
            if (explicitLaunch || inferredLaunch)
            {
                return "abre " + app.CanonicalName;
            }

            return corrected;
        }

        return text;
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        var index = text.IndexOf(phrase, StringComparison.Ordinal);
        while (index >= 0)
        {
            var leftBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + phrase.Length;
            var rightBoundary = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (leftBoundary && rightBoundary)
            {
                return true;
            }

            index = text.IndexOf(phrase, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static string ReplacePhrase(string text, string phrase, string replacement)
    {
        var index = text.IndexOf(phrase, StringComparison.Ordinal);
        return index < 0
            ? text
            : (text[..index] + replacement + text[(index + phrase.Length)..]).Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseSpacesRegex();

    [GeneratedRegex(@"\b(?:abre|abras|abreme|abrir|abrirme|haber|inicia|iniciar|ejecuta|ejecutar|lanza|poner|pon|a\s+ver|labras)\b")]
    private static partial Regex LaunchIntentRegex();

    [GeneratedRegex(@"^(?:puedes|puedas|podrias|podria|por\s+favor|quiero|quisiera|necesito)\b")]
    private static partial Regex CourtesyCommandRegex();

    [GeneratedRegex(@"\b(?:que\s+es|dime|decirme|explica|explicame|informacion)\b")]
    private static partial Regex QuestionIntentRegex();
}
