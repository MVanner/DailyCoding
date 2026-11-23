namespace Daily.Cli.Tests;

[TestClass]
public sealed class TestRunnerTests
{
    internal static Language MakeLanguage(string commandsJson, string results = "out/*.xml", string format = "junit", string requires = "[]")
    {
        var temp = new TempDir("runner");
        temp.Write("languages/lua/language.json", $$"""
            {
              "id": "lua", "name": "Lua", "stubDir": "challenge", "testDir": "tests",
              "sourceExtensions": [".lua"], "ignoreDirs": [],
              "test": { "commands": {{commandsJson}}, "results": "{{results}}", "format": "{{format}}" },
              "naiveForbidden": "x", "requires": {{requires}}, "ide": "ide",
              "runnerHint": "Install a Lua runner.", "levelHints": { "Beginner": "a", "Intermediate": "b", "Advanced": "c", "Expert": "d" }
            }
            """);
        return LanguageLoader.Get(LanguageLoader.Load(Path.Combine(temp.Path, "languages")), "lua");
    }

    [TestMethod]
    public void ResolveCommand_PicksOsVariant()
    {
        var lang = MakeLanguage("""[{"args":["./gradlew","test"],"os":"unix"},{"args":["gradlew.bat","test"],"os":"windows"}]""");
        using var work = new TempDir("work");
        work.Write("gradlew.bat", "");
        work.Write("gradlew", "");

        var command = TestRunner.ResolveCommand(lang, work.Path, isWindows: true, _ => false);

        CollectionAssert.AreEqual(new[] { "gradlew.bat", "test" }, command);
    }

    [TestMethod]
    public void ResolveCommand_FallsThroughToNextAvailable()
    {
        var lang = MakeLanguage("""[{"args":["luajit","spec/runner.lua"]},{"args":["lua","spec/runner.lua"]}]""");
        using var work = new TempDir("work");

        CollectionAssert.AreEqual(new[] { "lua", "spec/runner.lua" }, TestRunner.ResolveCommand(lang, work.Path, false, e => e == "lua"));
        Assert.IsNull(TestRunner.ResolveCommand(lang, work.Path, false, _ => false));
    }

    [TestMethod]
    public void FindResults_SupportsDoubleStar()
    {
        using var work = new TempDir("work");
        var nested = work.Write("TestResults/abc/deep/result.trx", "");
        work.Write("TestResults/abc/deep/other.txt", "");

        CollectionAssert.AreEqual(new[] { nested }, TestRunner.FindResults(work.Path, "TestResults/**/*.trx").ToArray());
    }

    [TestMethod]
    public void FindResults_SingleLevelGlob()
    {
        using var work = new TempDir("work");
        var a = work.Write("build/test-results/test/a.xml", "");
        work.Write("build/test-results/test/sub/b.xml", "");

        CollectionAssert.AreEqual(new[] { a }, TestRunner.FindResults(work.Path, "build/test-results/test/*.xml").ToArray());
    }

    [TestMethod]
    public void FindResults_ExactFile()
    {
        using var work = new TempDir("work");
        var file = work.Write(".build/results.xml", "");

        CollectionAssert.AreEqual(new[] { file }, TestRunner.FindResults(work.Path, ".build/results.xml").ToArray());
        Assert.IsEmpty(TestRunner.FindResults(work.Path, ".build/missing.xml"));
    }

    [TestMethod]
    public void Run_NoRunner_ReportsHint()
    {
        var lang = MakeLanguage("""[{"args":["definitely-not-a-real-exe-xyz"]}]""");
        using var work = new TempDir("work");

        var (result, output) = TestRunner.Run(lang, work.Path, TimeSpan.FromSeconds(5));

        Assert.IsNull(result);
        StringAssert.Contains(output, "no runner found");
        StringAssert.Contains(output, "Install a Lua runner.");
    }

    [TestMethod]
    public void Run_ParsesResultsWrittenByCommand()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("unix shell required"); }
        var lang = MakeLanguage("""[{"args":["sh","-c","mkdir -p out && echo '<testsuite><testcase name=\"a\" classname=\"C\"/></testsuite>' > out/r.xml"]}]""");
        using var work = new TempDir("work");

        var (result, _) = TestRunner.Run(lang, work.Path, TimeSpan.FromSeconds(30));

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Total);
    }

    [TestMethod]
    public void Run_NonZeroExitWithOnlyPassingResults_IsNull()
    {
        var lang = MakeLanguage("""[{"args":["sh","-c","mkdir -p out && echo '<testsuite><testcase name=\"a\" classname=\"C\"/></testsuite>' > out/r.xml; exit 3"]}]""");
        using var work = new TempDir("work");

        var (result, _) = TestRunner.Run(lang, work.Path, TimeSpan.FromSeconds(30));

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Run_NonZeroExitWithFailedTests_KeepsTheResult()
    {
        var lang = MakeLanguage("""[{"args":["sh","-c","mkdir -p out && echo '<testsuite><testcase name=\"a\" classname=\"C\"><failure message=\"x\"/></testcase></testsuite>' > out/r.xml; exit 1"]}]""");
        using var work = new TempDir("work");

        var (result, _) = TestRunner.Run(lang, work.Path, TimeSpan.FromSeconds(30));

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Failed);
    }

    [TestMethod]
    public void Run_IgnoresStaleResultsWhenCommandWritesNone()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("unix shell required"); }
        var lang = MakeLanguage("""[{"args":["sh","-c","exit 1"]}]""");
        using var work = new TempDir("work");
        work.Write("out/old.xml", "<testsuite><testcase name=\"a\" classname=\"C\"/></testsuite>");

        var (result, _) = TestRunner.Run(lang, work.Path, TimeSpan.FromSeconds(30));

        Assert.IsNull(result);
        Assert.IsFalse(File.Exists(Path.Combine(work.Path, "out", "old.xml")));
    }

    [TestMethod]
    public void Run_TimeoutIsNullEvenWithResultsWritten()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("unix shell required"); }
        var lang = MakeLanguage("""[{"args":["sh","-c","mkdir -p out && echo '<testsuite><testcase name=\"a\" classname=\"C\"/></testsuite>' > out/r.xml; sleep 30"]}]""");
        using var work = new TempDir("work");

        var (result, output) = TestRunner.Run(lang, work.Path, TimeSpan.FromMilliseconds(500));

        Assert.IsNull(result);
        StringAssert.Contains(output, "timed out");
    }

    [TestMethod]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void Run_LaunchesRelativeExecutableFromWorkDir()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("unix shell required"); }
        var lang = MakeLanguage("""[{"args":["./run.sh"]}]""");
        using var work = new TempDir("work");
        var script = work.Write("run.sh", "#!/bin/sh\nmkdir -p out\necho '<testsuite><testcase name=\"a\" classname=\"C\"/></testsuite>' > out/r.xml\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var (result, _) = TestRunner.Run(lang, work.Path, TimeSpan.FromSeconds(30));

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Total);
    }
}
