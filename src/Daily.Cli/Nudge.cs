using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Daily.Cli;

/// <summary>The SessionStart hook: one line on the first session of the day, nothing otherwise.</summary>
public static class Nudge
{
    public const string NotConfiguredMessage = "☕ Daily C# challenges are installed — run /daily:setup to pick your level";
    public const string StaleSuffix = " (plugin updated — run /daily:setup to rebuild)";

    // Default escaping keeps hook output pure ASCII (☕ becomes \u2615), so it survives a non-UTF-8 console code page on Windows.
    private static readonly JsonSerializerOptions HookJson = new();

    /// <summary>Returns the hook's JSON output, or null when nothing should be shown today.</summary>
    public static string? Run(DailyPaths paths, DateOnly today)
    {
        var store = new StateStore(paths);
        var state = store.LoadState();
        if (state.LastNudgeDate == today)
        {
            return null;
        }

        var config = store.LoadConfig();
        if (config is null)
        {
            store.SaveState(state with { LastNudgeDate = today });
            return Output(NotConfiguredMessage);
        }

        var catalogs = Catalogs.Load(LanguageLoader.Load(paths.LanguagesDir));
        var entry = state.For(today);
        if (entry is null || Today.NeedsRepick(entry, config, catalogs))
        {
            entry = Today.CreatePickedEntry(today, config, catalogs, state.History.Where(e => e.Date != today).ToList());
        }

        if (entry is null)
        {
            return null;
        }

        var history = state.History.Where(e => e.Date != today).Append(entry).OrderBy(e => e.Date).ToList();
        store.SaveState(new DailyState(today, history));

        var topicName = catalogs.GetValueOrDefault(entry.Language)?.FirstOrDefault(t => t.Id == entry.TopicId)?.Name ?? entry.TopicId;
        var stretch = entry.Stretch ? " — stretch day" : "";
        var message = $"☕ Today's C# challenge is ready ({entry.Level} · {topicName}{stretch}) — /daily:start when you have 15 min";
        if (BuildStamp.IsStale(paths))
        {
            message += StaleSuffix;
        }

        return Output(message);
    }

    /// <summary>CLI handler. Never fails: errors go to hook-errors.log and the hook stays silent.</summary>
    public static int Command(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            var output = Run(paths, DateOnly.FromDateTime(DateTime.Now));
            if (output is not null)
            {
                stdout.WriteLine(output);
            }
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(paths.DataDir);
                File.AppendAllText(paths.HookErrorLog, $"{DateTimeOffset.Now:O} {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Nothing sensible left to do; the hook must stay silent.
            }
        }

        return ExitCodes.Ok;
    }

    private static string Output(string message) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["systemMessage"] = message }, HookJson);
}

/// <summary>Detects when the plugin's CLI sources changed since the last /daily:setup build.</summary>
public static class BuildStamp
{
    private static readonly string[] Extensions = [".cs", ".csproj"];

    public static string Compute(string pluginRoot)
    {
        var sourceDir = Path.Combine(pluginRoot, "src", "Daily.Cli");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (Directory.Exists(sourceDir))
        {
            var files = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)
                .Select(f => (Full: f, Relative: Path.GetRelativePath(sourceDir, f).Replace('\\', '/')))
                .Where(f => Extensions.Contains(Path.GetExtension(f.Full)))
                .Where(f => !f.Relative.StartsWith("bin/", StringComparison.Ordinal) && !f.Relative.StartsWith("obj/", StringComparison.Ordinal))
                .OrderBy(f => f.Relative, StringComparer.Ordinal);

            foreach (var file in files)
            {
                sha.AppendData(Encoding.UTF8.GetBytes(file.Relative + "\n"));
                sha.AppendData(File.ReadAllBytes(file.Full));
            }
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    public static bool IsStale(DailyPaths paths)
    {
        var file = StampFile(paths);
        return !File.Exists(file) || File.ReadAllText(file).Trim() != Compute(paths.PluginRoot);
    }

    public static void Write(DailyPaths paths)
    {
        Directory.CreateDirectory(paths.BinDir);
        File.WriteAllText(StampFile(paths), Compute(paths.PluginRoot));
    }

    public static int Command(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        Write(paths);
        stdout.WriteLine(Json.Serialize(new { stamp = Compute(paths.PluginRoot) }));
        return ExitCodes.Ok;
    }

    private static string StampFile(DailyPaths paths) => Path.Combine(paths.BinDir, "build-stamp.txt");
}
