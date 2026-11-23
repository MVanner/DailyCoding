using System.Text.RegularExpressions;

namespace Daily.Cli;

public sealed record ToolStatus(bool Available, string[] Missing);

public static class ToolCheck
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    public static ToolStatus Check(Language lang)
    {
        var missing = new List<string>();

        foreach (var req in lang.Descriptor.Requires)
        {
            var (exit, output) = ProcessRunner.Run(req.Command, req.Args, lang.Dir, null, CheckTimeout);
            if (exit == -1 || (req.MinVersion is not null && !VersionAtLeast(output, req.MinVersion, req.VersionPattern)))
            {
                missing.Add(req.InstallHint);
            }
        }

        if (TestRunner.ResolveCommand(lang, lang.TemplateDir, OperatingSystem.IsWindows(), ProcessRunner.IsOnPath) is null)
        {
            missing.Add(lang.Descriptor.RunnerHint);
        }

        return new ToolStatus(missing.Count == 0, [.. missing]);
    }

    /// <summary>
    /// Compares the version in <paramref name="output"/> with <paramref name="min"/>. <paramref name="pattern"/> is a regex whose
    /// first capture group holds the version; without one the first <c>\d+(.\d+)*</c> is used.
    /// </summary>
    public static bool VersionAtLeast(string output, string min, string? pattern = null)
    {
        var found = Regex.Match(output, pattern ?? @"\d+(\.\d+)*");
        if (!found.Success)
        {
            return false;
        }

        var text = pattern is null ? found.Value : found.Groups[1].Value;
        var actual = text.Split('.').Select(int.Parse).ToList();
        var required = min.Split('.').Select(int.Parse).ToList();
        for (var i = 0; i < Math.Max(actual.Count, required.Count); i++)
        {
            var a = i < actual.Count ? actual[i] : 0;
            var r = i < required.Count ? required[i] : 0;
            if (a != r)
            {
                return a > r;
            }
        }

        return true;
    }
}
