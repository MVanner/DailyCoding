using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Daily.Cli;

/// <summary>One way to run the tests: <c>Os</c> is "windows", "unix" or null (any).</summary>
public sealed record TestCommand(string[] Args, string? Os);

/// <summary>How tests run and where their result files end up; <c>Format</c> is "junit" or "trx".</summary>
public sealed record TestSpec(TestCommand[] Commands, string Results, string Format, Dictionary<string, string>? Env);

public sealed record ToolRequirement(string Command, string[] Args, string? MinVersion, string InstallHint);

/// <summary>The contents of <c>languages/&lt;id&gt;/language.json</c>.</summary>
public sealed record LanguageDescriptor(
    string Id,
    string Name,
    string StubDir,
    string TestDir,
    string[] SourceExtensions,
    string[] IgnoreDirs,
    TestSpec Test,
    string NaiveForbidden,
    ToolRequirement[] Requires,
    string Ide,
    string RunnerHint,
    Dictionary<Level, string> LevelHints,
    string? CatalogDir,
    string? TemplateDir);

/// <summary>A loaded descriptor with its directories resolved to absolute paths.</summary>
public sealed record Language(LanguageDescriptor Descriptor, string Dir, string CatalogDir, string TemplateDir, string GuidePath)
{
    public string Id => Descriptor.Id;

    public string Name => Descriptor.Name;
}

public sealed class LanguageException(string message) : Exception(message);

public static class LanguageLoader
{
    private const string DescriptorFile = "language.json";

    public static IReadOnlyList<Language> Load(string languagesDir)
    {
        if (!Directory.Exists(languagesDir))
        {
            throw new LanguageException($"Languages folder not found: {languagesDir}");
        }

        return Directory.GetDirectories(languagesDir)
            .Order(StringComparer.Ordinal)
            .Where(dir => File.Exists(Path.Combine(dir, DescriptorFile)))
            .Select(LoadOne)
            .ToList();
    }

    public static Language Get(IReadOnlyList<Language> all, string id) =>
        all.FirstOrDefault(l => l.Id == id)
        ?? throw new LanguageException($"Unknown language '{id}'. Available: {string.Join(", ", all.Select(l => l.Id))}.");

    private static Language LoadOne(string dir)
    {
        var file = Path.Combine(dir, DescriptorFile);
        LanguageDescriptor? descriptor;
        try
        {
            descriptor = Json.Deserialize<LanguageDescriptor>(File.ReadAllText(file));
        }
        catch (JsonException ex)
        {
            throw new LanguageException($"{file}: invalid JSON ({ex.Message})");
        }

        if (descriptor is null)
        {
            throw new LanguageException($"{file}: empty descriptor");
        }

        Validate(descriptor, Path.GetFileName(dir), file);

        return new Language(
            descriptor,
            dir,
            Resolve(dir, descriptor.CatalogDir, "catalog"),
            Resolve(dir, descriptor.TemplateDir, "template"),
            Path.Combine(dir, "guide.md"));
    }

    private static string Resolve(string dir, string? configured, string fallback) =>
        Path.GetFullPath(Path.Combine(dir, configured ?? fallback));

    private static void Validate(LanguageDescriptor d, string folder, string file)
    {
        [DoesNotReturn]
        void Fail(string message) => throw new LanguageException($"{file}: {message}");

        if (d.Id != folder)
        {
            Fail($"id '{d.Id}' must match folder name '{folder}'");
        }

        foreach (var (field, value) in new[]
        {
            ("name", d.Name), ("stubDir", d.StubDir), ("testDir", d.TestDir), ("ide", d.Ide), ("runnerHint", d.RunnerHint),
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Fail($"'{field}' is required");
            }
        }

        if (d.SourceExtensions is null || d.IgnoreDirs is null || d.Requires is null || d.LevelHints is null)
        {
            Fail("'sourceExtensions', 'ignoreDirs', 'requires' and 'levelHints' are required");
        }

        if (d.Test?.Commands is not { Length: > 0 } || d.Test.Commands.Any(c => c?.Args is not { Length: > 0 }))
        {
            Fail("'test.commands' needs at least one command with args (and no null entries)");
        }

        if (d.Requires.Any(r => r is null))
        {
            Fail("'requires' must not contain null entries");
        }

        if (d.Requires.Any(r => string.IsNullOrWhiteSpace(r.Command) || string.IsNullOrWhiteSpace(r.InstallHint)))
        {
            Fail("each 'requires' entry needs a 'command' and an 'installHint'");
        }

        if (d.Test.Commands.Any(c => c.Os is not (null or "windows" or "unix")))
        {
            Fail("'test.commands[].os' must be \"windows\", \"unix\" or omitted");
        }

        if (string.IsNullOrWhiteSpace(d.Test.Results))
        {
            Fail("'test.results' is required");
        }

        if (d.Test.Format is not ("junit" or "trx"))
        {
            Fail($"'test.format' must be \"junit\" or \"trx\", not '{d.Test.Format}'");
        }

        try
        {
            _ = new Regex(d.NaiveForbidden ?? "");
        }
        catch (ArgumentException ex)
        {
            Fail($"'naiveForbidden' is not a valid regex ({ex.Message})");
        }

        if (string.IsNullOrEmpty(d.NaiveForbidden))
        {
            Fail("'naiveForbidden' is required");
        }

        var missing = Enum.GetValues<Level>().Where(l => !d.LevelHints.ContainsKey(l)).ToList();
        if (missing.Count > 0)
        {
            Fail($"'levelHints' is missing {string.Join(", ", missing)}");
        }
    }
}
