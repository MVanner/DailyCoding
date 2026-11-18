using System.Text.RegularExpressions;

namespace Daily.Cli;

/// <summary>Creates a challenge folder in the workspace from the plugin's template.</summary>
public static partial class Scaffolder
{
    private static readonly string[] TextExtensions = [".csproj", ".slnx", ".cs", ".md", ".json"];

    [GeneratedRegex("^[a-z0-9-]{3,40}$")]
    private static partial Regex SlugPattern();

    public static string Create(DailyPaths paths, DailyConfig config, DateOnly date, string slug, bool force)
    {
        if (!SlugPattern().IsMatch(slug))
        {
            throw new ArgumentException($"Slug '{slug}' must be 3–40 characters of a-z, 0-9 and '-'.", nameof(slug));
        }

        var folder = Path.Combine(Path.GetFullPath(config.Workspace), $"{date:yyyy-MM-dd}-{slug}");
        if (Directory.Exists(folder))
        {
            if (!force)
            {
                throw new InvalidOperationException($"{folder} already exists (use --force to recreate it).");
            }

            Directory.Delete(folder, recursive: true);
        }

        var tfm = $"net{config.SdkMajor}.0";
        foreach (var source in Directory.EnumerateFiles(paths.TemplatesDir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(paths.TemplatesDir, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (TextExtensions.Contains(Path.GetExtension(source)))
            {
                File.WriteAllText(target, File.ReadAllText(source).Replace("{{TFM}}", tfm));
            }
            else
            {
                File.Copy(source, target);
            }
        }

        return folder;
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

        var folder = Create(paths, config, CliHelpers.Date(args), args[0], args.Contains("--force"));
        stdout.WriteLine(Json.Serialize(new { folder }));
        return ExitCodes.Ok;
    }
}
