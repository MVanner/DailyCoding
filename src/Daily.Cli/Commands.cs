namespace Daily.Cli;

/// <summary>The `today`, `record`, `hint` and `config` commands used by the skills.</summary>
public static class Commands
{
    private const int RecentCount = 14;

    /// <summary>
    /// <c>today [--date d]</c> — the day's entry (picking one if the hook hasn't), its seed, config, recent history and
    /// the most recent earlier challenge that was started but not completed (<c>unfinished</c>).
    /// </summary>
    public static int TodayCommand(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        var store = new StateStore(paths);
        var config = CliHelpers.RequireConfig(store, stderr);
        if (config is null)
        {
            return ExitCodes.NotConfigured;
        }

        var today = CliHelpers.Date(args);
        var catalog = CatalogLoader.Load(paths.CatalogDir);
        var entry = EnsureEntry(store, config, catalog, today);
        var topic = catalog.FirstOrDefault(t => t.Id == entry.TopicId);
        var history = store.LoadState().History;
        var unfinished = history.Where(e => e.Date < today && e.Status == ChallengeStatus.Started).MaxBy(e => e.Date);

        stdout.WriteLine(Json.Serialize(new
        {
            date = today,
            entry,
            topic = new { id = entry.TopicId, name = topic?.Name ?? entry.TopicId },
            seed = topic?.Seeds.GetValueOrDefault(entry.Level),
            config,
            recent = history.Where(e => e.Date < today).OrderByDescending(e => e.Date).Take(RecentCount),
            referenceDir = ReferenceDir(paths, today),
            unfinished = unfinished is null ? null : new { entry = unfinished, referenceDir = ReferenceDir(paths, unfinished.Date) },
        }));
        return ExitCodes.Ok;
    }

    /// <summary>
    /// <c>record started --folder &lt;path&gt; | completed --passed true|false | skipped | generation-attempt | generation-failed</c>,
    /// optionally with <c>--date d</c> for an earlier day's existing entry.
    /// </summary>
    public static int RecordCommand(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Usage: record started|completed|skipped|generation-attempt|generation-failed [options]");
            return ExitCodes.Error;
        }

        var store = new StateStore(paths);
        var config = CliHelpers.RequireConfig(store, stderr);
        if (config is null)
        {
            return ExitCodes.NotConfigured;
        }

        var date = CliHelpers.Date(args);
        var entry = date == CliHelpers.Today()
            ? EnsureEntry(store, config, CatalogLoader.Load(paths.CatalogDir), date)
            : store.LoadState().For(date);
        if (entry is null)
        {
            stderr.WriteLine($"No challenge for {date:yyyy-MM-dd}.");
            return ExitCodes.Error;
        }

        var now = DateTimeOffset.Now;
        HistoryEntry updated;
        switch (args[0])
        {
            case "started":
                if (!Allowed(entry, stderr, ChallengeStatus.Picked, ChallengeStatus.Started)) return ExitCodes.Error;
                var folder = Option(args, "--folder");
                if (folder is null)
                {
                    stderr.WriteLine("record started needs --folder <path>.");
                    return ExitCodes.Error;
                }

                updated = entry with { Status = ChallengeStatus.Started, Folder = folder, StartedAt = entry.StartedAt ?? now };
                break;

            case "completed":
                if (!Allowed(entry, stderr, ChallengeStatus.Started, ChallengeStatus.Completed)) return ExitCodes.Error;
                if (!bool.TryParse(Option(args, "--passed"), out var passed))
                {
                    stderr.WriteLine("record completed needs --passed true|false.");
                    return ExitCodes.Error;
                }

                updated = entry with { Status = ChallengeStatus.Completed, Passed = passed, CompletedAt = now };
                break;

            case "skipped":
                if (!Allowed(entry, stderr, ChallengeStatus.Picked, ChallengeStatus.Started)) return ExitCodes.Error;
                updated = entry with { Status = ChallengeStatus.Skipped };
                break;

            case "generation-attempt":
                if (!Allowed(entry, stderr, ChallengeStatus.Picked, ChallengeStatus.Started)) return ExitCodes.Error;
                updated = entry with { GenerationAttempts = entry.GenerationAttempts + 1 };
                break;

            case "generation-failed":
                if (!Allowed(entry, stderr, ChallengeStatus.Picked, ChallengeStatus.Started)) return ExitCodes.Error;
                updated = entry with { Status = ChallengeStatus.GenerationFailed };
                break;

            default:
                stderr.WriteLine($"Unknown record status '{args[0]}'.");
                return ExitCodes.Error;
        }

        store.Upsert(updated);
        stdout.WriteLine(Json.Serialize(updated));
        return ExitCodes.Ok;
    }

    /// <summary><c>hint [--date d]</c> — counts a hint for the day's started challenge and returns the new total.</summary>
    public static int HintCommand(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        var store = new StateStore(paths);
        if (CliHelpers.RequireConfig(store, stderr) is null)
        {
            return ExitCodes.NotConfigured;
        }

        var entry = store.LoadState().For(CliHelpers.Date(args));
        if (entry is null || !Allowed(entry, stderr, ChallengeStatus.Started, ChallengeStatus.Completed))
        {
            if (entry is null) stderr.WriteLine("No challenge started today — run /daily:start first.");
            return ExitCodes.Error;
        }

        var updated = entry with { Hints = entry.Hints + 1 };
        store.Upsert(updated);
        stdout.WriteLine(Json.Serialize(new { hints = updated.Hints }));
        return ExitCodes.Ok;
    }

    /// <summary><c>config show</c> | <c>config set level|workspace|sdkMajor &lt;value&gt;</c></summary>
    public static int ConfigCommand(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        var store = new StateStore(paths);
        switch (args)
        {
            case ["show"]:
                var current = CliHelpers.RequireConfig(store, stderr);
                if (current is null) return ExitCodes.NotConfigured;
                stdout.WriteLine(Json.Serialize(current));
                return ExitCodes.Ok;

            case ["set", var key, var value]:
                var config = store.LoadConfig() ?? Defaults();
                DailyConfig? changed = key switch
                {
                    "level" when Enum.TryParse<Level>(value, ignoreCase: true, out var level) && Enum.IsDefined(level) => config with { Level = level },
                    "workspace" when !string.IsNullOrWhiteSpace(value) => config with { Workspace = value },
                    "sdkMajor" when int.TryParse(value, out var major) && major >= 10 => config with { SdkMajor = major },
                    _ => null,
                };
                if (changed is null)
                {
                    stderr.WriteLine($"Invalid setting: {key} = {value}. Keys: level (Beginner|Intermediate|Advanced|Expert), workspace (path), sdkMajor (10+).");
                    return ExitCodes.Error;
                }

                store.SaveConfig(changed);
                stdout.WriteLine(Json.Serialize(changed));
                return ExitCodes.Ok;

            default:
                stderr.WriteLine("Usage: config show | config set level|workspace|sdkMajor <value>");
                return ExitCodes.Error;
        }
    }

    private static DailyConfig Defaults() => new(
        Level.Beginner,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DailyCoding"),
        Math.Max(10, Environment.Version.Major),
        Random.Shared.Next(1, int.MaxValue));

    private static string? ReferenceDir(DailyPaths paths, DateOnly date)
    {
        var dir = Path.Combine(paths.ReferencesDir, date.ToString("yyyy-MM-dd"));
        return Directory.Exists(dir) ? dir : null;
    }

    private static HistoryEntry EnsureEntry(StateStore store, DailyConfig config, IReadOnlyList<Topic> catalog, DateOnly today)
    {
        var state = store.LoadState();
        var entry = state.For(today);
        if (entry is null)
        {
            entry = Today.CreatePickedEntry(today, config, catalog, state.History);
            store.Upsert(entry);
        }

        return entry;
    }

    private static bool Allowed(HistoryEntry entry, TextWriter stderr, params ChallengeStatus[] from)
    {
        if (from.Contains(entry.Status))
        {
            return true;
        }

        stderr.WriteLine($"Today's challenge is {entry.Status}; that isn't allowed from this state.");
        return false;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
