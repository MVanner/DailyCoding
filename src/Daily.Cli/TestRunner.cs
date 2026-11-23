using System.Text.RegularExpressions;

namespace Daily.Cli;

public static class TestRunner
{
    /// <summary>The first command (for this OS) whose executable is available, or null when none is.</summary>
    public static string[]? ResolveCommand(Language lang, string workDir, bool isWindows, Func<string, bool> isOnPath)
    {
        var os = isWindows ? "windows" : "unix";
        foreach (var command in lang.Descriptor.Test.Commands.Where(c => c.Os is null || c.Os == os))
        {
            var exe = command.Args[0];
            var relative = exe.StartsWith("./", StringComparison.Ordinal)
                || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                || exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            if (relative ? File.Exists(Path.Combine(workDir, exe)) : isOnPath(exe))
            {
                return command.Args;
            }
        }

        return null;
    }

    /// <summary>Files matching <c>dir/[**/]file-pattern</c> (or an exact path) under <paramref name="workDir"/>, sorted.</summary>
    public static IReadOnlyList<string> FindResults(string workDir, string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var dirPart = slash < 0 ? "" : normalized[..slash];
        var filePart = slash < 0 ? normalized : normalized[(slash + 1)..];

        var recursive = dirPart == "**" || dirPart.EndsWith("/**", StringComparison.Ordinal);
        if (recursive)
        {
            dirPart = dirPart == "**" ? "" : dirPart[..^3];
        }

        var dir = Path.Combine(workDir, dirPart.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return Directory.EnumerateFiles(dir, filePart, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static (TestRunResult? Result, string Output) Run(Language lang, string workDir, TimeSpan timeout)
    {
        var command = ResolveCommand(lang, workDir, OperatingSystem.IsWindows(), ProcessRunner.IsOnPath);
        if (command is null)
        {
            return (null, $"no runner found: {lang.Descriptor.RunnerHint}\n" + string.Join("\n", lang.Descriptor.Requires.Select(r => r.InstallHint)));
        }

        var spec = lang.Descriptor.Test;
        EnsureExecutable(workDir, command[0]);
        var (_, output) = ProcessRunner.Run(command[0], command.Skip(1).ToList(), workDir, spec.Env, timeout);

        var files = FindResults(workDir, spec.Results);
        var result = spec.Format == "trx" ? TrxResults.Read(files) : JUnitResults.Read(files);
        return (result, output);
    }

    private static void EnsureExecutable(string workDir, string exe)
    {
        if (OperatingSystem.IsWindows() || !exe.StartsWith("./", StringComparison.Ordinal))
        {
            return;
        }

        var path = Path.Combine(workDir, exe);
        if (!File.Exists(path))
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        var wanted = mode | UnixFileMode.UserExecute;
        if (wanted != mode)
        {
            File.SetUnixFileMode(path, wanted);
        }
    }
}
