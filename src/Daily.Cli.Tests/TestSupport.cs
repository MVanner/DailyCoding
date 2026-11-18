namespace Daily.Cli.Tests;

internal sealed class TempDir : IDisposable
{
    public TempDir(string? nameHint = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "daily-tests", (nameHint ?? "t") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal static class RepoPaths
{
    /// <summary>The plugin root (folder holding .claude-plugin/plugin.json), found by walking up from the test binaries.</summary>
    public static string PluginRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".claude-plugin", "plugin.json")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Plugin root not found above " + AppContext.BaseDirectory);
        }
    }
}
