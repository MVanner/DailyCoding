namespace Daily.Cli.Tests;

[TestClass]
public sealed class VerifierFirstErrorTests
{
    [TestMethod]
    public void Kotlin_ErrorLinesKeepTheirLocation()
    {
        const string output = """
            > Task :compileKotlin FAILED
            e: file:///tmp/ws/2026-11-23-kotlin-x/src/main/kotlin/Calc.kt:3:36 Unresolved reference 'c'.
            e: file:///tmp/ws/2026-11-23-kotlin-x/src/main/kotlin/Calc.kt:5:1 Expecting a top level declaration
            FAILURE: Build failed with an exception.
            """;

        var message = Verifier.FirstError(output);

        StringAssert.Contains(message, "Calc.kt:3:36 Unresolved reference 'c'.");
        StringAssert.Contains(message, "Calc.kt:5:1");
    }

    [TestMethod]
    public void ReturnsAtMostThreeMatchingLines()
    {
        const string output = "error: one\nerror: two\nerror: three\nerror: four";

        var message = Verifier.FirstError(output);

        StringAssert.Contains(message, "three");
        Assert.DoesNotContain("four", message);
    }

    [TestMethod]
    public void NoErrorLine_FallsBackToLastLine() =>
        Assert.AreEqual("done", Verifier.FirstError("starting\ndone\n"));

    [TestMethod]
    public void Swift_ErrorLineStillMatches() =>
        StringAssert.Contains(Verifier.FirstError("Sources/Challenge/Sum.swift:2:9: error: cannot find 'this' in scope"), "cannot find 'this'");
}
