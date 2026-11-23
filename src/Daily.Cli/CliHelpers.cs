namespace Daily.Cli;

public static class CliHelpers
{
    public const string NotConfigured = "Not configured yet — run /daily:setup.";

    public static DailyConfig? RequireConfig(StateStore store, TextWriter stderr)
    {
        var config = store.LoadConfig();
        if (config is null)
        {
            stderr.WriteLine(NotConfigured);
        }

        return config;
    }

    /// <summary>Loads the languages, skipping (and reporting in <paramref name="warnings"/>) any with a broken descriptor.</summary>
    public static IReadOnlyList<Language> LoadLanguages(DailyPaths paths, ICollection<string>? warnings = null) =>
        LanguageLoader.Load(paths.LanguagesDir, warnings ?? new List<string>());

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>The <c>--date yyyy-MM-dd</c> option if given (skills pin the date for a whole run), otherwise today.</summary>
    public static DateOnly Date(string[] args)
    {
        var index = Array.IndexOf(args, "--date");
        if (index < 0)
        {
            return Today();
        }

        if (index + 1 >= args.Length || !DateOnly.TryParseExact(args[index + 1], "yyyy-MM-dd", out var date))
        {
            throw new FormatException("--date needs a value like 2026-11-16.");
        }

        return date;
    }
}
