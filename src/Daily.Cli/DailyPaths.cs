namespace Daily.Cli;

/// <summary>Where the plugin's per-user data and bundled files live.</summary>
public sealed record DailyPaths(string DataDir, string PluginRoot)
{
    public const string DataOption = "--data";
    public const string PluginRootOption = "--plugin-root";

    public string ConfigFile => Path.Combine(DataDir, "config.json");
    public string StateFile => Path.Combine(DataDir, "state.json");
    public string BinDir => Path.Combine(DataDir, "bin");
    public string ReferencesDir => Path.Combine(DataDir, "references");
    public string HookErrorLog => Path.Combine(DataDir, "hook-errors.log");
    public string LanguagesDir => Path.Combine(PluginRoot, "languages");

    /// <summary>
    /// Resolves paths from <c>--data</c>/<c>--plugin-root</c>, falling back to the
    /// <c>CLAUDE_PLUGIN_DATA</c>/<c>CLAUDE_PLUGIN_ROOT</c> environment variables.
    /// </summary>
    public static DailyPaths Resolve(string[] args, IDictionary<string, string?> env)
    {
        var data = OptionValue(args, DataOption) ?? Env(env, "CLAUDE_PLUGIN_DATA");
        var root = OptionValue(args, PluginRootOption) ?? Env(env, "CLAUDE_PLUGIN_ROOT");
        if (string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "Plugin paths unknown: pass --data and --plugin-root, or set CLAUDE_PLUGIN_DATA and CLAUDE_PLUGIN_ROOT.");
        }

        return new DailyPaths(Path.GetFullPath(data), Path.GetFullPath(root));
    }

    /// <summary>Removes the global path options (and their values) so commands only see their own arguments.</summary>
    public static string[] StripGlobalOptions(string[] args)
    {
        var result = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is DataOption or PluginRootOption)
            {
                i++; // skip the value
                continue;
            }

            result.Add(args[i]);
        }

        return result.ToArray();
    }

    private static string? OptionValue(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string? Env(IDictionary<string, string?> env, string key) =>
        env.TryGetValue(key, out var value) ? value : null;
}
