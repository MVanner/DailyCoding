using System.Collections;

namespace Daily.Cli;

public static class Program
{
    public delegate int CommandHandler(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr);

    /// <summary>Command name → handler. Handlers receive their arguments without the command name or global options.</summary>
    private static readonly Dictionary<string, CommandHandler> Handlers = new(StringComparer.Ordinal)
    {
        ["nudge"] = Nudge.Command,
        ["stamp-write"] = BuildStamp.Command,
        ["scaffold"] = Scaffolder.Command,
        ["verify"] = Verifier.Command,
        ["today"] = Commands.TodayCommand,
        ["record"] = Commands.RecordCommand,
        ["hint"] = Commands.HintCommand,
        ["config"] = Commands.ConfigCommand,
        ["repick"] = Commands.RepickCommand,
        ["languages"] = Commands.LanguagesCommand,
        ["run-tests"] = Commands.RunTestsCommand,
        ["warmup"] = Commands.WarmupCommand,
    };

    /// <summary>Commands run by the SessionStart hook: they must never print errors or exit non-zero.</summary>
    private static readonly HashSet<string> SilentCommands = new(StringComparer.Ordinal) { "nudge" };

    public static int Main(string[] args) => Run(args, ReadEnvironment(), Console.Out, Console.Error);

    public static int Run(string[] args, IDictionary<string, string?> env, TextWriter stdout, TextWriter stderr)
    {
        var commandArgs = DailyPaths.StripGlobalOptions(args);
        if (commandArgs.Length == 0 || !Handlers.TryGetValue(commandArgs[0], out var handler))
        {
            stderr.WriteLine($"Unknown command. Available: {string.Join(", ", Handlers.Keys.Order())}");
            return ExitCodes.Error;
        }

        try
        {
            var paths = DailyPaths.Resolve(args, env);
            return handler(commandArgs[1..], paths, stdout, stderr);
        }
        catch (Exception ex)
        {
            if (SilentCommands.Contains(commandArgs[0]))
            {
                return ExitCodes.Ok;
            }

            stderr.WriteLine(ex.Message);
            return ExitCodes.Error;
        }
    }

    private static Dictionary<string, string?> ReadEnvironment()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            env[(string)entry.Key] = entry.Value as string;
        }

        return env;
    }
}

public static class ExitCodes
{
    public const int Ok = 0;
    public const int Error = 1;
    public const int NotConfigured = 2;
}
