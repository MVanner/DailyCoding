using System.Text.RegularExpressions;

namespace Daily.Cli;

public sealed record VerifyResult(
    bool Ok,
    bool StubBuilds,
    int ReferencePassed,
    int ReferenceTotal,
    int NaiveFailed,
    int NaiveTotal,
    string[] Problems);

/// <summary>
/// The quality gate for a generated challenge: the stub builds and fails at least 70 % of the tests, the reference
/// passes every test (and there are at least 3), and a naive implementation (nothing the language forbids, e.g. throws) fails at least 70 % of them. On success the reference is moved out of
/// the workspace into references/&lt;date&gt;/ and .verify/ is removed.
/// </summary>
public static partial class Verifier
{
    public const int MinimumTests = 3;
    public const double NaiveMustFail = 0.7;

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private const string VerifyDirName = ".verify";

    public static VerifyResult Run(DailyPaths paths, Language language, string challengeDir, DateOnly date)
    {
        var problems = new List<string>();
        var verifyDir = Path.Combine(challengeDir, VerifyDirName);
        var referenceDir = Path.Combine(verifyDir, "reference");
        var naiveDir = Path.Combine(verifyDir, "naive");
        var hasReference = Directory.Exists(referenceDir);
        var hasNaive = Directory.Exists(naiveDir);
        var stubDirName = language.Descriptor.StubDir;

        if (!hasReference) problems.Add($"Missing .verify/reference/ (reference versions of the files in {stubDirName}/).");
        if (!hasNaive) problems.Add("Missing .verify/naive/ (naive versions returning default/empty values).");
        if (hasReference) CheckOverlayFiles(language, challengeDir, referenceDir, "reference", problems);
        if (hasNaive)
        {
            CheckOverlayFiles(language, challengeDir, naiveDir, "naive", problems);
            CheckNaiveIsNaive(language, naiveDir, problems);
        }

        // The shipped stub: must build, and the tests must mostly fail against it.
        var stub = RunTests(language, challengeDir, overlayDir: null, "stub", problems);
        if (stub is { } s)
        {
            RequireMostlyFailing(s, "stub", "the shipped stub", problems);
        }

        var reference = hasReference ? RunTests(language, challengeDir, referenceDir, "reference", problems) : null;
        if (reference is { } r)
        {
            if (r.Total < MinimumTests)
            {
                problems.Add($"reference: only {r.Total} tests ran; write at least {MinimumTests}.");
            }
            else if (r.Passed < r.Total)
            {
                problems.Add($"reference: {r.Total - r.Passed} of {r.Total} tests fail against the reference solution.");
            }
        }

        var naive = hasNaive ? RunTests(language, challengeDir, naiveDir, "naive", problems) : null;
        if (naive is { } n)
        {
            RequireMostlyFailing(n, "naive", "the naive stub", problems);
        }

        var ok = problems.Count == 0;
        if (ok)
        {
            var stash = Path.Combine(paths.ReferencesDir, date.ToString("yyyy-MM-dd"));
            if (Directory.Exists(stash))
            {
                Directory.Delete(stash, recursive: true);
            }

            Directory.CreateDirectory(paths.ReferencesDir);
            CopyTree(referenceDir, stash, []);
            Directory.Delete(verifyDir, recursive: true);
        }

        return new VerifyResult(
            ok,
            StubBuilds: stub is not null,
            reference?.Passed ?? 0,
            reference?.Total ?? 0,
            naive?.Failed ?? 0,
            naive?.Total ?? 0,
            problems.ToArray());
    }

    /// <summary><c>verify &lt;folder&gt; [--date yyyy-MM-dd]</c> — prints the result JSON; exit 0 even when not Ok.</summary>
    public static int Command(string[] args, DailyPaths paths, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Usage: verify <folder> [--date yyyy-MM-dd]");
            return ExitCodes.Error;
        }

        var folder = Path.GetFullPath(args[0]);
        if (!Directory.Exists(folder))
        {
            stderr.WriteLine($"Folder not found: {folder}");
            return ExitCodes.Error;
        }

        var language = LanguageLoader.Get(LanguageLoader.Load(paths.LanguagesDir), Scaffolder.LanguageOf(folder) ?? "csharp");
        stdout.WriteLine(Json.Serialize(Run(paths, language, folder, CliHelpers.Date(args))));
        return ExitCodes.Ok;
    }

    private readonly record struct TestCounts(int Passed, int Failed, int Total);

    /// <summary>Runs the tests on a temp copy, with <paramref name="overlayDir"/> copied over the stub folder. Null when it doesn't build.</summary>
    private static TestCounts? RunTests(Language language, string challengeDir, string? overlayDir, string name, List<string> problems)
    {
        var work = CopyToTemp(language, challengeDir);
        try
        {
            if (overlayDir is not null)
            {
                CopyTree(overlayDir, Path.Combine(work, language.Descriptor.StubDir), []);
            }

            var (result, output) = TestRunner.Run(language, work, Timeout);
            if (result is null)
            {
                problems.Add($"{name}: does not build or no tests ran: " + FirstError(output));
                return null;
            }

            return new TestCounts(result.Passed, result.Failed, result.Total);
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static void RequireMostlyFailing(TestCounts counts, string name, string description, List<string> problems)
    {
        var required = (int)Math.Ceiling(NaiveMustFail * counts.Total);
        if (counts.Total > 0 && counts.Failed < required)
        {
            problems.Add($"{name}: only {counts.Failed} of {counts.Total} tests fail against {description}; at least {required} must. The tests are too lenient or the stub/naive code does too much.");
        }
    }

    /// <summary>Reference and naive files replace stub files: each must have a counterpart in Challenge/.</summary>
    private static void CheckOverlayFiles(Language language, string challengeDir, string overlayDir, string name, List<string> problems)
    {
        foreach (var file in Directory.EnumerateFiles(overlayDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(overlayDir, file);
            if (!File.Exists(Path.Combine(challengeDir, language.Descriptor.StubDir, relative)))
            {
                problems.Add($"{name}: {relative.Replace('\\', '/')} has no matching stub file in {language.Descriptor.StubDir}/.");
            }
        }
    }

    /// <summary>A naive version that uses something the language forbids (e.g. throws) would make any test suite look strict.</summary>
    private static void CheckNaiveIsNaive(Language language, string naiveDir, List<string> problems)
    {
        var forbidden = new Regex(language.Descriptor.NaiveForbidden);
        foreach (var file in Directory.EnumerateFiles(naiveDir, "*", SearchOption.AllDirectories))
        {
            if (!language.Descriptor.SourceExtensions.Contains(Path.GetExtension(file)))
            {
                continue;
            }

            var match = forbidden.Match(File.ReadAllText(file));
            if (match.Success)
            {
                problems.Add($"naive: {Path.GetFileName(file)} contains `{match.Value}`; the naive version must return default/empty values or echo input, never throw or fail.");
            }
        }
    }

    private static string CopyToTemp(Language language, string challengeDir)
    {
        var work = Path.Combine(Path.GetTempPath(), "daily-verify", Guid.NewGuid().ToString("N")[..12]);
        CopyTree(challengeDir, work, [.. language.Descriptor.IgnoreDirs, VerifyDirName]);
        return work;
    }

    private static void CopyTree(string source, string target, string[] skipDirs)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (!skipDirs.Contains(name))
            {
                CopyTree(dir, Path.Combine(target, name), skipDirs);
            }
        }
    }

    /// <summary>Up to the first three compiler/runner error lines (including Kotlin's <c>e: file:line:col message</c>), or the last output line when there are none.</summary>
    public static string FirstError(string output)
    {
        var lines = output.Split('\n').Select(l => l.Trim()).ToList();
        var errors = lines
            .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.StartsWith("e: ", StringComparison.Ordinal))
            .Select(l => l.Length > 400 ? l[..400] + "..." : l)
            .Distinct()
            .Take(3)
            .ToList();
        return errors.Count > 0 ? string.Join(" | ", errors) : lines.Where(l => l.Length > 0).LastOrDefault() ?? "";
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
