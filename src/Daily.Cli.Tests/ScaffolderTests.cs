using System.Diagnostics;

namespace Daily.Cli.Tests;

[TestClass]
public sealed class ScaffolderTests
{
    private static readonly DateOnly Day = new(2026, 11, 16);

    private static (DailyPaths Paths, DailyConfig Config) Setup(TempDir data, string workspace, int sdkMajor = 11) =>
        (new DailyPaths(data.Path, RepoPaths.PluginRoot), new DailyConfig(Level.Intermediate, workspace, sdkMajor, 1));

    [TestMethod]
    public void Create_CopiesTemplateAndSubstitutesTfm()
    {
        using var data = new TempDir();
        using var ws = new TempDir("ws");
        var (paths, config) = Setup(data, ws.Path);

        var folder = Scaffolder.Create(paths, config, Day, "async-retry", force: false);

        Assert.AreEqual(Path.Combine(ws.Path, "2026-11-16-async-retry"), folder);
        foreach (var file in new[] { "Challenge.slnx", "Challenge/Challenge.csproj", "Challenge.Tests/Challenge.Tests.csproj", ".gitignore" })
        {
            Assert.IsTrue(File.Exists(Path.Combine(folder, file)), file);
        }

        var csproj = File.ReadAllText(Path.Combine(folder, "Challenge", "Challenge.csproj"));
        StringAssert.Contains(csproj, "<TargetFramework>net11.0</TargetFramework>");
        var all = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(File.ReadAllText);
        Assert.IsFalse(all.Any(text => text.Contains("{{")), "placeholders left");
    }

    [TestMethod]
    [DataRow("A")]
    [DataRow("ab")]
    [DataRow("Async_Retry")]
    [DataRow("../escape")]
    [DataRow("")]
    public void Create_RejectsBadSlug(string slug)
    {
        using var data = new TempDir();
        using var ws = new TempDir("ws");
        var (paths, config) = Setup(data, ws.Path);

        Assert.ThrowsExactly<ArgumentException>(() => Scaffolder.Create(paths, config, Day, slug, force: false));
    }

    [TestMethod]
    public void Create_ExistingFolder_ThrowsWithoutForce_RecreatesWithForce()
    {
        using var data = new TempDir();
        using var ws = new TempDir("ws");
        var (paths, config) = Setup(data, ws.Path);
        var folder = Scaffolder.Create(paths, config, Day, "linq-groups", force: false);
        File.WriteAllText(Path.Combine(folder, "leftover.txt"), "old");

        Assert.ThrowsExactly<InvalidOperationException>(() => Scaffolder.Create(paths, config, Day, "linq-groups", force: false));

        Scaffolder.Create(paths, config, Day, "linq-groups", force: true);
        Assert.IsFalse(File.Exists(Path.Combine(folder, "leftover.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(folder, "Challenge.slnx")));
    }

    [TestMethod]
    public void Create_WorksWithSpaceInWorkspacePath()
    {
        using var data = new TempDir();
        using var ws = new TempDir("Jane Doe");
        var (paths, config) = Setup(data, Path.Combine(ws.Path, "Daily Coding"));

        var folder = Scaffolder.Create(paths, config, Day, "records", force: false);

        Assert.IsTrue(File.Exists(Path.Combine(folder, "Challenge", "Challenge.csproj")));
    }

    [TestMethod]
    public void ScaffoldCommand_PrintsFolder()
    {
        using var data = new TempDir();
        using var ws = new TempDir("ws");
        var (paths, config) = Setup(data, ws.Path);
        new StateStore(paths).SaveConfig(config);
        var stdout = new StringWriter();

        var exit = Program.Run(["scaffold", "pattern-match", "--data", paths.DataDir, "--plugin-root", paths.PluginRoot], new Dictionary<string, string?>(), stdout, new StringWriter());

        Assert.AreEqual(0, exit);
        StringAssert.Contains(stdout.ToString().Replace("\\\\", "\\"), "-pattern-match");
    }

    [TestMethod]
    public void ScaffoldCommand_NotConfigured_Exits2()
    {
        using var data = new TempDir();

        var exit = Program.Run(["scaffold", "pattern-match", "--data", data.Path, "--plugin-root", RepoPaths.PluginRoot], new Dictionary<string, string?>(), new StringWriter(), new StringWriter());

        Assert.AreEqual(2, exit);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Scaffolded_BuildsAndTestsRun()
    {
        using var data = new TempDir();
        using var ws = new TempDir("ws with space");
        var (paths, config) = Setup(data, ws.Path, Dotnet.InstalledSdkMajor());
        var folder = Scaffolder.Create(paths, config, Day, "smoke-test", force: false);
        File.WriteAllText(Path.Combine(folder, "Challenge", "Calc.cs"), "namespace Challenge; public static class Calc { public static int Add(int a, int b) => a + b; }");
        File.WriteAllText(Path.Combine(folder, "Challenge.Tests", "CalcTests.cs"),
            "namespace Challenge.Tests; [TestClass] public sealed class CalcTests { [TestMethod] public void Adds() => Assert.AreEqual(3, Challenge.Calc.Add(1, 2)); }");

        var (exit, output) = Dotnet.Run(folder, "test");

        Assert.AreEqual(0, exit, output);
    }
}

internal static class Dotnet
{
    public static int InstalledSdkMajor() => int.Parse(Run(Path.GetTempPath(), "--version").Output.Trim().Split('.')[0]);

    public static (int Exit, string Output) Run(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDir, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        psi.Environment["UseSharedCompilation"] = "false";
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
