using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jarvis.Native;

public sealed record InstalledWindowsApp(string Name, string LaunchTarget);

public static class PcCatalog
{
    public static InstalledWindowsApp? FindInstalledApp(string requestedName)
    {
        var query = NativeActionEngine.Normalize(requestedName);
        if (query.Length < 2)
        {
            return null;
        }

        object? shellObject = null;
        object? folderObject = null;
        object? itemsObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return null;
            }

            shellObject = Activator.CreateInstance(shellType);
            dynamic shell = shellObject!;
            folderObject = shell.NameSpace("shell:AppsFolder");
            if (folderObject is null)
            {
                return null;
            }

            dynamic folder = folderObject;
            itemsObject = folder.Items();
            dynamic items = itemsObject;
            var matches = new List<(int Score, InstalledWindowsApp App)>();
            for (var index = 0; index < (int)items.Count; index++)
            {
                object? itemObject = null;
                try
                {
                    itemObject = items.Item(index);
                    dynamic item = itemObject;
                    var name = ((string?)item.Name ?? string.Empty).Trim();
                    var path = ((string?)item.Path ?? string.Empty).Trim();
                    if (name.Length == 0 || path.Length == 0 ||
                        name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("desinstalar", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var normalizedName = NativeActionEngine.Normalize(name);
                    var score = MatchScore(normalizedName, query, allowFuzzy: true);
                    if (score > 0)
                    {
                        matches.Add((score, new InstalledWindowsApp(name, path)));
                    }
                }
                finally
                {
                    ReleaseCom(itemObject);
                }
            }

            return matches
                .OrderByDescending(match => match.Score)
                .ThenBy(match => match.App.Name.Length)
                .Select(match => match.App)
                .FirstOrDefault();
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            ReleaseCom(itemsObject);
            ReleaseCom(folderObject);
            ReleaseCom(shellObject);
        }
    }

    public static void LaunchInstalledApp(InstalledWindowsApp app)
    {
        if (File.Exists(app.LaunchTarget) ||
            Uri.TryCreate(app.LaunchTarget, UriKind.Absolute, out _))
        {
            Process.Start(new ProcessStartInfo(app.LaunchTarget) { UseShellExecute = true });
            return;
        }

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add("shell:AppsFolder\\" + app.LaunchTarget);
        Process.Start(startInfo);
    }

    public static string? FindUserContent(string requestedName)
    {
        var query = CleanContentQuery(requestedName);
        if (query.Length < 2)
        {
            return null;
        }

        var roots = GetUserRoots();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            MaxRecursionDepth = 8
        };
        var matches = new List<(int Score, DateTime Modified, string Path)>();
        var inspected = 0;

        foreach (var root in roots)
        {
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", options))
                {
                    if (++inspected > 20_000)
                    {
                        break;
                    }

                    var name = Path.GetFileName(path);
                    var nameWithoutExtension = Path.GetFileNameWithoutExtension(path);
                    var score = Math.Max(
                        MatchScore(NativeActionEngine.Normalize(name), query),
                        MatchScore(NativeActionEngine.Normalize(nameWithoutExtension), query));
                    if (score <= 0)
                    {
                        continue;
                    }

                    DateTime modified;
                    try
                    {
                        modified = File.GetLastWriteTime(path);
                    }
                    catch
                    {
                        modified = DateTime.MinValue;
                    }

                    matches.Add((score, modified, path));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Modified)
            .Select(match => match.Path)
            .FirstOrDefault();
    }

    public static void RevealInExplorer(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }

        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add("/select," + path);
        Process.Start(startInfo);
    }

    private static string[] GetUserRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(userProfile, "Documents"),
            Path.Combine(userProfile, "Documentos"),
            Path.Combine(userProfile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    private static string CleanContentQuery(string requestedName)
    {
        var normalized = NativeActionEngine.Normalize(requestedName);
        var removablePrefixes = new[]
        {
            "el archivo ", "la carpeta ", "el documento ", "la foto ",
            "archivo ", "carpeta ", "documento ", "foto "
        };
        var prefix = removablePrefixes.FirstOrDefault(normalized.StartsWith);
        return prefix is null ? normalized : normalized[prefix.Length..].Trim();
    }

    private static int MatchScore(string candidate, string query, bool allowFuzzy = false)
    {
        if (candidate.Equals(query, StringComparison.Ordinal))
        {
            return 100;
        }

        if (candidate.StartsWith(query + " ", StringComparison.Ordinal) ||
            candidate.StartsWith(query + ".", StringComparison.Ordinal))
        {
            return 82;
        }

        if (candidate.Contains(query, StringComparison.Ordinal))
        {
            return 65;
        }

        if (allowFuzzy)
        {
            var compactCandidate = string.Concat(candidate.Where(char.IsLetterOrDigit));
            var compactQuery = string.Concat(query.Where(char.IsLetterOrDigit));
            if (compactQuery.Length >= 4)
            {
                var candidateParts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var similarities = candidateParts
                    .Append(compactCandidate)
                    .Select(part => Similarity(string.Concat(part.Where(char.IsLetterOrDigit)), compactQuery));
                var similarity = similarities.Max();
                if (similarity >= 0.72)
                {
                    return 45 + (int)Math.Round(similarity * 15);
                }
            }
        }

        return 0;
    }

    private static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

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

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try
            {
                Marshal.FinalReleaseComObject(value);
            }
            catch
            {
            }
        }
    }
}
