namespace Daily.Cli.Tests;

[TestClass]
public sealed class ToolCheckTests
{
    [TestMethod]
    [DataRow("openjdk version \"25.0.2\" 2026-01-20", "17", true)]
    [DataRow("java version \"1.8.0_402\"", "17", false)]
    [DataRow("Apple Swift version 6.4.2 (swiftlang...)", "6.0", true)]
    [DataRow("Swift version 5.10", "6.0", false)]
    [DataRow("no digits", "1", false)]
    public void VersionAtLeast(string output, string min, bool expected) =>
        Assert.AreEqual(expected, ToolCheck.VersionAtLeast(output, min));

    private const string SwiftPattern = @"Swift version (\d+(?:\.\d+)*)";
    private const string JavaPattern = @"version ""(?:1\.)?(\d+(?:\.\d+)*)";

    [TestMethod]
    [DataRow("swift-driver version: 1.168.6 Apple Swift version 6.4.2 (swiftlang-6.4.2.1.9 clang-2100.4.1.1)", "6.0", SwiftPattern, true)]
    [DataRow("swift-driver version: 1.90.11.1 Apple Swift version 5.10 (swiftlang-5.10.0.13 clang-1500.3.9.4)", "6.0", SwiftPattern, false)]
    [DataRow("Picked up JAVA_TOOL_OPTIONS: -Xmx4g\nopenjdk version \"21.0.2\" 2024-01-16", "17", JavaPattern, true)]
    [DataRow("java version \"1.8.0_402\"", "17", JavaPattern, false)]
    [DataRow("openjdk version \"25.0.2\" 2026-01-20", "17", JavaPattern, true)]
    [DataRow("no match here", "1", JavaPattern, false)]
    public void VersionAtLeast_WithPattern(string output, string min, string pattern, bool expected) =>
        Assert.AreEqual(expected, ToolCheck.VersionAtLeast(output, min, pattern));

    [TestMethod]
    public void ToolCheck_UsesVersionPatternFromDescriptor()
    {
        var lang = TestRunnerTests.MakeLanguage(
            """[{"args":["sh","-c","true"]}]""",
            requires: """[{"command":"sh","args":["-c","echo 'driver version: 1.9 Swift version 5.10'"],"minVersion":"6.0","versionPattern":"Swift version (\\d+(?:\\.\\d+)*)","installHint":"Install Swift 6."}]""");

        var status = ToolCheck.Check(lang);

        CollectionAssert.Contains(status.Missing, "Install Swift 6.");
    }

    [TestMethod]
    public void ToolCheck_NoRunner_ReportsRunnerHint()
    {
        var lang = TestRunnerTests.MakeLanguage("""[{"args":["definitely-not-a-real-exe-xyz"]}]""");

        var status = ToolCheck.Check(lang);

        Assert.IsFalse(status.Available);
        CollectionAssert.Contains(status.Missing, "Install a Lua runner.");
    }

    [TestMethod]
    public void ToolCheck_MissingRequirement_ReportsInstallHint()
    {
        var lang = TestRunnerTests.MakeLanguage(
            """[{"args":["sh","-c","true"]}]""",
            requires: """[{"command":"definitely-not-a-real-exe-xyz","args":["--version"],"installHint":"Install xyz."}]""");

        var status = ToolCheck.Check(lang);

        Assert.IsFalse(status.Available);
        CollectionAssert.Contains(status.Missing, "Install xyz.");
    }
}
