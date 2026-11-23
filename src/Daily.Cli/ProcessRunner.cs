using System.ComponentModel;
using System.Diagnostics;

namespace Daily.Cli;

public static class ProcessRunner
{
    private static readonly Dictionary<string, string> DotnetEnv = new()
    {
        // MSBuild worker nodes and the compiler server outlive `dotnet build` and inherit the redirected pipes,
        // so reading to end-of-stream would hang. Keep builds self-contained.
        ["MSBUILDDISABLENODEREUSE"] = "1",
        ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
        ["UseSharedCompilation"] = "false",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["DOTNET_NOLOGO"] = "1",
    };

    public static (int Exit, string Output) Dotnet(string workingDir, TimeSpan timeout, params string[] args) =>
        Run("dotnet", args, workingDir, DotnetEnv, timeout);

    /// <summary>Runs a process and returns its exit code and combined output; exit -1 means it timed out or could not start.</summary>
    public static (int Exit, string Output) Run(
        string executable,
        IReadOnlyList<string> args,
        string workingDir,
        IReadOnlyDictionary<string, string>? env,
        TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var (key, value) in env ?? new Dictionary<string, string>())
        {
            psi.Environment[key] = value;
        }

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {executable}.");
        }
        catch (Win32Exception ex)
        {
            return (-1, $"Could not start {executable}: {ex.Message}");
        }

        using (process)
        {
            // Never wait on the pipes forever: grandchildren can inherit them and keep them open after the process exits.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                return (-1, $"{executable} {string.Join(' ', args)} timed out after {timeout.TotalMinutes:0} min");
            }

            Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(10));
            return (process.ExitCode, Completed(stdout) + Completed(stderr));
        }
    }

    /// <summary>True when <paramref name="executable"/> resolves on PATH (trying PATHEXT extensions on Windows).</summary>
    public static bool IsOnPath(string executable) => IsOnPath(executable, Environment.GetEnvironmentVariable("PATH"));

    /// <summary>As <see cref="IsOnPath(string)"/>, searching the given PATH-style string.</summary>
    public static bool IsOnPath(string executable, string? path)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [];

        foreach (var dir in (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), executable);
            if (File.Exists(candidate) || extensions.Any(ext => File.Exists(candidate + ext)))
            {
                return true;
            }
        }

        return false;
    }

    private static string Completed(Task<string> read) => read.IsCompletedSuccessfully ? read.Result : "";
}
