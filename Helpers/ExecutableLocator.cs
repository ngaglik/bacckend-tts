namespace BackendTts.Helpers;

/// <summary>Pencarian executable di PATH (pengganti sederhana <c>which</c>/<c>where</c>).</summary>
public static class ExecutableLocator
{
    public static string? Which(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(name) ? name : null;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { string.Empty };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim(), name + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    public static bool Exists(string name) => Which(name) is not null;
}
