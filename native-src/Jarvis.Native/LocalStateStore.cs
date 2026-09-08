using System.Text.Json;

namespace Jarvis.Native;

public sealed record LocalNote(int Id, string Content, DateTime CreatedAt);

public sealed class LocalState
{
    public string AssistantName { get; set; } = "Jarvis";
    public bool AiAssistantEnabled { get; set; }
    public List<LocalNote> Notes { get; set; } = [];
    public List<string> Memories { get; set; } = [];
}

public sealed class LocalStateStore
{
    private readonly object _sync = new();
    private readonly string _path;
    private LocalState _state;

    public LocalStateStore(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? AppConfig.DataDirectory;
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "state.json");
        _state = Load();
    }

    public LocalNote AddNote(string content)
    {
        lock (_sync)
        {
            var nextId = _state.Notes.Count == 0 ? 1 : _state.Notes.Max(note => note.Id) + 1;
            var note = new LocalNote(nextId, content, DateTime.Now);
            _state.Notes.Add(note);
            Save();
            return note;
        }
    }

    public string GetAssistantName()
    {
        lock (_sync)
        {
            try
            {
                return AssistantIdentity.Validate(_state.AssistantName);
            }
            catch (ArgumentException)
            {
                return "Jarvis";
            }
        }
    }

    public void SetAssistantName(string name)
    {
        var validated = AssistantIdentity.Validate(name);
        lock (_sync)
        {
            _state.AssistantName = validated;
            Save();
        }
    }

    public bool GetAiAssistantEnabled()
    {
        lock (_sync)
        {
            return _state.AiAssistantEnabled;
        }
    }

    public void SetAiAssistantEnabled(bool enabled)
    {
        lock (_sync)
        {
            _state.AiAssistantEnabled = enabled;
            Save();
        }
    }

    public IReadOnlyList<LocalNote> GetNotes(int limit = 10)
    {
        lock (_sync)
        {
            return _state.Notes.OrderByDescending(note => note.Id).Take(limit).ToArray();
        }
    }

    public bool AddMemory(string content)
    {
        lock (_sync)
        {
            if (_state.Memories.Any(item => string.Equals(item, content, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _state.Memories.Add(content);
            Save();
            return true;
        }
    }

    public IReadOnlyList<string> GetMemories()
    {
        lock (_sync)
        {
            return _state.Memories.ToArray();
        }
    }

    private LocalState Load()
    {
        if (!File.Exists(_path))
        {
            return new LocalState();
        }

        try
        {
            return JsonSerializer.Deserialize<LocalState>(File.ReadAllText(_path)) ?? new LocalState();
        }
        catch (JsonException)
        {
            return new LocalState();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _path, true);
    }
}

public static class AssistantIdentity
{
    public static string Validate(string? name)
    {
        var clean = string.Join(' ', (name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length is < 2 or > 20)
        {
            throw new ArgumentException("El nombre debe tener entre 2 y 20 caracteres.");
        }

        if (clean.Any(character => !char.IsLetter(character) && character is not ' ' and not '-'))
        {
            throw new ArgumentException("Usa únicamente letras, espacios o guiones.");
        }

        return clean;
    }
}
