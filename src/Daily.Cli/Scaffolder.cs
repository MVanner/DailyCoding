using System.Text.RegularExpressions;

namespace Daily.Cli;

/// <summary>Creates a challenge folder in the workspace from the plugin's template.</summary>
public static partial class Scaffolder
{
    public const string MarkerFile = ".daily-language";

    private static readonly string[] AlwaysSubstituted = [".md", ".json"];

    [GeneratedRegex("^[a-z0-9-]{3,40}$")]
    private static partial Regex SlugPattern();

    public static string Create(DailyPaths paths, DailyConfig config, Language language, DateOnly date, string slug, bool force)
    {
        if (!SlugPattern().IsMatch(slug))
        {
            throw new ArgumentException($"Slug '{slug}' must be 3–40 characters of a-z, 0-9 and '-'.", nameof(slug));
        }

        var folder = Path.Combine(Path.GetFullPath(config.Workspace), $"{date:yyyy-MM-dd}-{language.Id}-{slug}");
        if (Directory.Exists(folder))
        {
            if (!force)
            {
                throw new InvalidOperationException($"{folder} already exists (use --force to recreate it).");
            }

            Directory.Delete(folder, recursive: true);
        }

        var tfm = $"net{config.SdkMajor}.0";
        foreach (var source in Directory.EnumerateFiles(language.TemplateDir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(language.TemplateDir, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (IsSubstituted(language, source))
            {
                File.WriteAllText(target, File.ReadAllText(source).Replace("{{TFM}}", tfm));
            }
            else
            {
                File.Copy(source, target);
            }
        }

        File.WriteAllText(Path.Combine(folder, MarkerFile), language.Id);
        return folder;
    }

    private static bool IsSubstituted(Language language, string file)
    {
        var extension = Path.GetExtension(file);
        return AlwaysSubstituted.Contains(extension) || language.Descriptor.SourceExtensions.Contains(extension);
    }

    /// <summary>The language id in <c>.daily-language</c>, or null for folders made before languages existed.</summary>
    public static string? LanguageOf(string folder)
    {
        var marker = Path.Combine(folder, MarkerFile);
        if (!File.Exists(marker))
        {
            return null;
        }

        var id = File.ReadAllText(marker).Trim();
        return id.Length == 0 ? null : id;
    }

    /// <summary><c>scaffold &lt;slug&gt; [--force]</c></summary>
    public static int Command(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Usage: scaffold <slug> [--force]");
            return ExitCodes.Error;
        }

        var config = CliHelpers.RequireConfig(new StateStore(paths), stderr);
        if (config is null)
        {
            return ExitCodes.NotConfigured;
        }

        var date = CliHelpers.Date(args);
        var languages = CliHelpers.LoadLanguages(paths);
        var store = new StateStore(paths);
        var entry = Today.Ensure(store, config, Catalogs.Load(languages), date);
        if (entry is null)
        {
            stderr.WriteLine("No usable language — run /daily:setup.");
            return ExitCodes.Error;
        }

        var folder = Create(paths, config, LanguageLoader.Get(languages, entry.Language), date, args[0], args.Contains("--force"));
        stdout.WriteLine(Json.Serialize(new { folder }));
        return ExitCodes.Ok;
    }
}
