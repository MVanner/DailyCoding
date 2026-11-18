using System.Diagnostics;

namespace Daily.Cli;

internal static class ProcessRunner
{
    public static (int Exit, string Output) Dotnet(string workingDir, TimeSpan timeout, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // MSBuild worker nodes and the compiler server outlive `dotnet build` and inherit the redirected pipes,
        // so reading to end-of-stream would hang. Keep builds self-contained and never wait on the pipes forever.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        psi.Environment["UseSharedCompilation"] = "false";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            return (-1, $"dotnet {string.Join(' ', args)} timed out after {timeout.TotalMinutes:0} min");
        }

        Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(10));
        return (process.ExitCode, Completed(stdout) + Completed(stderr));
    }

    private static string Completed(Task<string> read) => read.IsCompletedSuccessfully ? read.Result : "";
}
