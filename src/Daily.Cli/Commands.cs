namespace Daily.Cli;

/// <summary>The `today`, `record`, `hint` and `config` commands used by the skills.</summary>
public static class Commands
{
    private const int RecentCount = 14;
    private const int OutputLimit = 4000;
    private const string NoUsableLanguage = "No usable language — run /daily:setup.";

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
        var warnings = new List<string>();
        var languages = CliHelpers.LoadLanguages(paths, warnings);
        var catalogs = Catalogs.Load(languages, warnings);
        var entry = Today.Ensure(store, config, catalogs, today);
        if (entry is null)
        {
            stderr.WriteLine(NoUsableLanguage);
            return ExitCodes.Error;
        }

        var language = LanguageLoader.Get(languages, entry.Language);
        var tools = ToolCheck.Check(language);

        var topic = catalogs.GetValueOrDefault(entry.Language)?.FirstOrDefault(t => t.Id == entry.TopicId);
        var history = store.LoadState().History;
        var unfinished = history.Where(e => e.Date < today && e.Status == ChallengeStatus.Started).MaxBy(e => e.Date);

        stdout.WriteLine(Json.Serialize(new
        {
            date = today,
            entry,
            topic = new { id = entry.TopicId, name = topic?.Name ?? entry.TopicId },
            seed = topic?.Seeds.GetValueOrDefault(entry.Level),
            config,
            warnings,
            language = new
            {
                id = language.Id,
                name = language.Name,
                stubDir = language.Descriptor.StubDir,
                testDir = language.Descriptor.TestDir,
                ide = language.Descriptor.Ide,
                guide = language.GuidePath,
                available = tools.Available,
                missing = tools.Missing,
            },
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
        HistoryEntry? entry;
        if (date == CliHelpers.Today())
        {
            entry = Today.Ensure(store, config, Catalogs.Load(CliHelpers.LoadLanguages(paths)), date);
            if (entry is null)
            {
                stderr.WriteLine(NoUsableLanguage);
                return ExitCodes.Error;
            }
        }
        else
        {
            entry = store.LoadState().For(date);
        }

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

    /// <summary>
    /// <c>repick --exclude &lt;id[,id...]&gt; [--date d]</c> — replaces a still-Picked entry with one from another language
    /// (for when the day's language turns out to be unusable on this machine).
    /// </summary>
    public static int RepickCommand(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        var excluded = Option(args, "--exclude");
        if (excluded is null)
        {
            stderr.WriteLine("Usage: repick --exclude <languageId[,languageId...]> [--date yyyy-MM-dd]");
            return ExitCodes.Error;
        }

        var store = new StateStore(paths);
        var config = CliHelpers.RequireConfig(store, stderr);
        if (config is null)
        {
            return ExitCodes.NotConfigured;
        }

        var date = CliHelpers.Date(args);
        var state = store.LoadState();
        var entry = state.For(date);
        if (entry is null || entry.Status != ChallengeStatus.Picked)
        {
            stderr.WriteLine(entry is null
                ? $"No challenge for {date:yyyy-MM-dd}; nothing to repick."
                : $"Today's challenge is {entry.Status}; only a Picked challenge can be repicked.");
            return ExitCodes.Error;
        }

        var catalogs = Catalogs.Load(CliHelpers.LoadLanguages(paths));
        var replacement = Today.CreatePickedEntry(date, config, catalogs, state.History.Where(e => e.Date != date).ToList(), new HashSet<string>(excluded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        if (replacement is null)
        {
            stderr.WriteLine(NoUsableLanguage);
            return ExitCodes.Error;
        }

        store.Upsert(replacement);
        stdout.WriteLine(Json.Serialize(replacement));
        return ExitCodes.Ok;
    }

    /// <summary><c>languages</c> — every available language with its tool status and level hints.</summary>
    public static int LanguagesCommand(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        var warnings = new List<string>();
        var list = CliHelpers.LoadLanguages(paths, warnings).Select(l =>
        {
            var tools = ToolCheck.Check(l);
            return new { id = l.Id, name = l.Name, available = tools.Available, missing = tools.Missing, levelHints = l.Descriptor.LevelHints };
        });
        stdout.WriteLine(Json.Serialize(list));
        foreach (var warning in warnings)
        {
            stderr.WriteLine("warning: " + warning);
        }

        return ExitCodes.Ok;
    }

    /// <summary><c>run-tests &lt;folder&gt;</c> — runs the folder's tests with its own language's runner.</summary>
    public static int RunTestsCommand(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Usage: run-tests <folder>");
            return ExitCodes.Error;
        }

        var folder = Path.GetFullPath(args[0]);
        if (!Directory.Exists(folder))
        {
            stderr.WriteLine($"Folder not found: {folder}");
            return ExitCodes.Error;
        }

        var language = LanguageLoader.Get(CliHelpers.LoadLanguages(paths), Scaffolder.LanguageOf(folder) ?? "csharp");
        var (result, output) = TestRunner.Run(language, folder, TimeSpan.FromMinutes(5));
        stdout.WriteLine(Json.Serialize(new
        {
            ok = result is not null && result.Total > 0 && result.Passed == result.Total,
            passed = result?.Passed ?? 0,
            failed = result?.Failed ?? 0,
            total = result?.Total ?? 0,
            failures = result?.Failures ?? [],
            output = output.Length > OutputLimit ? output[^OutputLimit..] : output,
        }));
        return ExitCodes.Ok;
    }

    /// <summary>
    /// <c>warmup &lt;languageId&gt;</c> — copies the language's template, overlays its known-good <c>warmup/</c> sample and runs the
    /// tests once, so the first real run isn't slow (toolchain and dependency downloads) and a broken toolchain shows up now.
    /// <c>ok</c> means the sample built and all of its tests passed; a language without a sample has nothing to warm up.
    /// </summary>
    public static int WarmupCommand(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Usage: warmup <languageId>");
            return ExitCodes.Error;
        }

        Language language;
        try
        {
            language = LanguageLoader.Get(CliHelpers.LoadLanguages(paths), args[0]);
        }
        catch (LanguageException ex)
        {
            stderr.WriteLine(ex.Message);
            return ExitCodes.Error;
        }

        if (!Directory.Exists(language.WarmupDir))
        {
            stdout.WriteLine(Json.Serialize(new { ok = true, seconds = 0.0, problem = (string?)null, output = "No warm-up sample for this language; nothing to do." }));
            return ExitCodes.Ok;
        }

        var temp = Path.Combine(Path.GetTempPath(), "daily-warmup-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var sdkMajor = new StateStore(paths).LoadConfig()?.SdkMajor ?? Math.Max(10, Environment.Version.Major);
            var config = new DailyConfig([], temp, sdkMajor, 0);
            var folder = Scaffolder.Create(paths, config, language, CliHelpers.Today(), "warmup", force: true);
            OverlayWarmup(language, folder);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var (result, output) = TestRunner.Run(language, folder, TimeSpan.FromMinutes(5));
            watch.Stop();
            var ok = result is { Total: > 0, Failed: 0 };
            stdout.WriteLine(Json.Serialize(new
            {
                ok,
                seconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
                problem = ok ? null : WarmupProblem(result, output),
                output = output.Length > OutputLimit ? output[^OutputLimit..] : output,
            }));
            return ExitCodes.Ok;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: a lingering daemon (e.g. Gradle) may still hold files; the temp dir is harmless.
            }
        }
    }

    private static void OverlayWarmup(Language language, string folder)
    {
        foreach (var source in Directory.EnumerateFiles(language.WarmupDir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(language.WarmupDir, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    private static string WarmupProblem(TestRunResult? result, string output)
    {
        if (result is { Failures.Length: > 0 })
        {
            var first = result.Failures[0];
            return $"{first.Name}: {first.Message}";
        }

        if (result is { Total: > 0 })
        {
            return "tests did not pass";
        }

        var lines = System.Text.RegularExpressions.Regex.Replace(output, "\u001B\\[[0-9;]*m", "")
            .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var error = lines
            .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.StartsWith("e: ", StringComparison.Ordinal))
            .Select(l => l.Length > 300 ? l[..300] + "..." : l)
            .Take(3)
            .ToList();
        return error.Count > 0
            ? string.Join(" | ", error)
            : "no tests ran: " + (lines.LastOrDefault() ?? "no output");
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

            case ["set", "level", var languageId, var levelText]:
                var levelConfig = store.LoadConfig() ?? Defaults();
                if (!Enum.TryParse<Level>(levelText, ignoreCase: true, out var newLevel) || !Enum.IsDefined(newLevel)
                    || !levelConfig.Languages.Any(l => l.Id == languageId))
                {
                    stderr.WriteLine($"Invalid setting: level {languageId} {levelText}. The language must be selected and the level one of Beginner|Intermediate|Advanced|Expert.");
                    return ExitCodes.Error;
                }

                var levelChanged = levelConfig with
                {
                    Languages = levelConfig.Languages.Select(l => l.Id == languageId ? l with { Level = newLevel } : l).ToList(),
                };
                store.SaveConfig(levelChanged);
                stdout.WriteLine(Json.Serialize(levelChanged));
                return ExitCodes.Ok;

            case ["set", "languages", var ids]:
                var langConfig = store.LoadConfig() ?? Defaults();
                var wanted = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
                try
                {
                    var all = CliHelpers.LoadLanguages(paths);
                    foreach (var id in wanted)
                    {
                        LanguageLoader.Get(all, id);
                    }
                }
                catch (LanguageException ex)
                {
                    stderr.WriteLine(ex.Message);
                    return ExitCodes.Error;
                }

                if (wanted.Count == 0)
                {
                    stderr.WriteLine("Invalid setting: languages needs at least one id, e.g. csharp,lua.");
                    return ExitCodes.Error;
                }

                var langChanged = langConfig with
                {
                    Languages = wanted.Select(id => langConfig.Languages.FirstOrDefault(l => l.Id == id) ?? new LanguageSetting(id, Level.Beginner)).ToList(),
                };
                store.SaveConfig(langChanged);
                stdout.WriteLine(Json.Serialize(langChanged));
                return ExitCodes.Ok;

            case ["set", var key, var value]:
                var config = store.LoadConfig() ?? Defaults();
                DailyConfig? changed = key switch
                {
                    "workspace" when !string.IsNullOrWhiteSpace(value) => config with { Workspace = value },
                    "sdkMajor" when int.TryParse(value, out var major) && major >= 10 => config with { SdkMajor = major },
                    _ => null,
                };
                if (changed is null)
                {
                    stderr.WriteLine($"Invalid setting: {key} = {value}. Keys: workspace (path), sdkMajor (10+); also: config set languages <id,id,...> and config set level <languageId> <Level>.");
                    return ExitCodes.Error;
                }

                store.SaveConfig(changed);
                stdout.WriteLine(Json.Serialize(changed));
                return ExitCodes.Ok;

            default:
                stderr.WriteLine("Usage: config show | config set languages <id,id,...> | config set level <languageId> <Level> | config set workspace|sdkMajor <value>");
                return ExitCodes.Error;
        }
    }

    private static DailyConfig Defaults() => new(
        [new LanguageSetting("csharp", Level.Beginner)],
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DailyCoding"),
        Math.Max(10, Environment.Version.Major),
        Random.Shared.Next(1, int.MaxValue));

    private static string? ReferenceDir(DailyPaths paths, DateOnly date)
    {
        var dir = Path.Combine(paths.ReferencesDir, date.ToString("yyyy-MM-dd"));
        return Directory.Exists(dir) ? dir : null;
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
