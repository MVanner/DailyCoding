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
