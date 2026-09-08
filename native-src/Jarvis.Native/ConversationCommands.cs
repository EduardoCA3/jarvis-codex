namespace Jarvis.Native;

public static class ConversationCommands
{
    public static bool IsEnd(string text, string? assistantName = null)
    {
        var normalized = NormalizePhrase(text);
        var normalizedName = string.IsNullOrWhiteSpace(assistantName)
            ? string.Empty
            : NormalizePhrase(assistantName);

        if (normalizedName.Length > 0)
        {
            foreach (var prefix in new[]
                     {
                         normalizedName,
                         "oye " + normalizedName,
                         "hey " + normalizedName
                     })
            {
                if (normalized.StartsWith(prefix + " ", StringComparison.Ordinal))
                {
                    normalized = normalized[(prefix.Length + 1)..].Trim();
                    break;
                }
            }
        }

        return normalized is
            "finalizar" or "finaliza" or
            "finaliza la conversacion" or "finalizar la conversacion" or
            "termina la conversacion" or "terminar conversacion" or
            "fin de la conversacion" or "deja de escuchar" or "eso es todo";
    }

    private static string NormalizePhrase(string text)
    {
        var normalized = NativeActionEngine.Normalize(text);
        var characters = normalized.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or ' '
                ? character
                : ' ');
        return string.Join(
            ' ',
            new string(characters.ToArray()).Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries));
    }
}
