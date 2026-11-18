using System.Text.RegularExpressions;
using System.Xml.Linq;

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
/// passes every test (and there are at least 3), and a naive implementation (no throws) fails at least 70 % of them. On success the reference is moved out of
/// the workspace into references/&lt;date&gt;/ and .verify/ is removed.
/// </summary>
public static partial class Verifier
{
    public const int MinimumTests = 3;
    public const double NaiveMustFail = 0.7;

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private static readonly string[] SkippedDirs = ["bin", "obj", ".verify", "TestResults"];
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static VerifyResult Run(DailyPaths paths, string challengeDir, DateOnly date)
    {
        var problems = new List<string>();
        var verifyDir = Path.Combine(challengeDir, ".verify");
        var referenceDir = Path.Combine(verifyDir, "reference");
        var naiveDir = Path.Combine(verifyDir, "naive");
        var hasReference = Directory.Exists(referenceDir);
        var hasNaive = Directory.Exists(naiveDir);

        if (!hasReference) problems.Add("Missing .verify/reference/ (reference versions of the files in Challenge/).");
        if (!hasNaive) problems.Add("Missing .verify/naive/ (naive versions returning default/empty values).");
        if (hasReference) CheckOverlayFiles(challengeDir, referenceDir, "reference", problems);
        if (hasNaive)
        {
            CheckOverlayFiles(challengeDir, naiveDir, "naive", problems);
            CheckNaiveDoesNotThrow(naiveDir, problems);
        }

        // The shipped stub: must build, and the tests must mostly fail against it.
        var stub = RunTests(challengeDir, overlayDir: null, "stub", problems);
        if (stub is { } s)
        {
            RequireMostlyFailing(s, "stub", "the shipped stub", problems);
        }

        var reference = hasReference ? RunTests(challengeDir, referenceDir, "reference", problems) : null;
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

        var naive = hasNaive ? RunTests(challengeDir, naiveDir, "naive", problems) : null;
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

        stdout.WriteLine(Json.Serialize(Run(paths, Path.GetFullPath(args[0]), CliHelpers.Date(args))));
        return ExitCodes.Ok;
    }

    private readonly record struct TestCounts(int Passed, int Failed, int Total);

    /// <summary>Runs the tests on a temp copy, with <paramref name="overlayDir"/> copied over Challenge/. Null when it doesn't build.</summary>
    private static TestCounts? RunTests(string challengeDir, string? overlayDir, string name, List<string> problems)
    {
        var work = CopyToTemp(challengeDir);
        try
        {
            if (overlayDir is not null)
            {
                CopyTree(overlayDir, Path.Combine(work, "Challenge"), []);
            }

            var results = Path.Combine(work, "TestResults");
            var (_, output) = ProcessRunner.Dotnet(work, Timeout, "test", "--logger", "trx;LogFileName=result.trx", "--results-directory", results);
            var trx = Directory.Exists(results) ? Directory.EnumerateFiles(results, "*.trx", SearchOption.AllDirectories).FirstOrDefault() : null;
            if (trx is null)
            {
                problems.Add($"{name}: does not build or no tests ran: " + FirstError(output));
                return null;
            }

            var counters = XDocument.Load(trx).Descendants(Trx + "Counters").Single();
            int Read(string attribute) => (int?)counters.Attribute(attribute) ?? 0;
            return new TestCounts(Read("passed"), Read("failed"), Read("total"));
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
    private static void CheckOverlayFiles(string challengeDir, string overlayDir, string name, List<string> problems)
    {
        foreach (var file in Directory.EnumerateFiles(overlayDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(overlayDir, file);
            if (!File.Exists(Path.Combine(challengeDir, "Challenge", relative)))
            {
                problems.Add($"{name}: {relative.Replace('\\', '/')} has no matching stub file in Challenge/.");
            }
        }
    }

    /// <summary>A naive version that throws would make any test suite look strict.</summary>
    private static void CheckNaiveDoesNotThrow(string naiveDir, List<string> problems)
    {
        foreach (var file in Directory.EnumerateFiles(naiveDir, "*.cs", SearchOption.AllDirectories))
        {
            if (ThrowKeyword().IsMatch(File.ReadAllText(file)))
            {
                problems.Add($"naive: {Path.GetFileName(file)} contains `throw`; the naive version must return default/empty values or echo input, never throw.");
            }
        }
    }

    [GeneratedRegex(@"\bthrow\b")]
    private static partial Regex ThrowKeyword();

    private static string CopyToTemp(string challengeDir)
    {
        var work = Path.Combine(Path.GetTempPath(), "daily-verify", Guid.NewGuid().ToString("N")[..12]);
        CopyTree(challengeDir, work, SkippedDirs);
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

    private static string FirstError(string output) =>
        output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? output.Trim().Split('\n').LastOrDefault() ?? "";

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
