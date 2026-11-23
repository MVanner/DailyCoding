namespace Daily.Cli.Tests;

[TestClass]
public sealed class TestResultsTests
{
    private const string GradleA = """
        <?xml version="1.0" encoding="UTF-8"?>
        <testsuite name="CalcTest" tests="3" skipped="0" failures="1" errors="0">
          <testcase name="adds" classname="CalcTest" time="0.01"/>
          <testcase name="subtracts" classname="CalcTest" time="0.01"/>
          <testcase name="divides" classname="CalcTest" time="0.01"><failure message="expected 2 but was 3" type="AssertionError">trace</failure></testcase>
        </testsuite>
        """;

    private const string GradleB = """
        <testsuite name="OtherTest" tests="2" failures="0" errors="1">
          <testcase name="ok" classname="OtherTest"/>
          <testcase name="boom" classname="OtherTest"><error type="Exception">java.lang.Exception: kaput
        at line</error></testcase>
        </testsuite>
        """;

    private static string[] Files(TempDir temp, params string[] contents) =>
        contents.Select((c, i) => temp.Write($"r{i}.xml", c)).ToArray();

    [TestMethod]
    public void Junit_CountsAcrossFiles()
    {
        using var temp = new TempDir("junit");

        var result = JUnitResults.Read(Files(temp, GradleA, GradleB));

        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.Passed);
        Assert.AreEqual(2, result.Failed);
        Assert.AreEqual(5, result.Total);
        CollectionAssert.AreEquivalent(new[] { "CalcTest.divides", "OtherTest.boom" }, result.Failures.Select(f => f.Name).ToArray());
        Assert.AreEqual("expected 2 but was 3", result.Failures.Single(f => f.Name == "CalcTest.divides").Message);
        Assert.AreEqual("java.lang.Exception: kaput", result.Failures.Single(f => f.Name == "OtherTest.boom").Message);
    }

    [TestMethod]
    public void Junit_SkippedExcluded()
    {
        using var temp = new TempDir("junit");
        var xml = "<testsuite><testcase name=\"a\" classname=\"C\"/><testcase name=\"b\" classname=\"C\"><skipped/></testcase></testsuite>";

        var result = JUnitResults.Read(Files(temp, xml));

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Total);
        Assert.AreEqual(1, result.Passed);
    }

    [TestMethod]
    public void Junit_NoTestcases_IsNull()
    {
        using var temp = new TempDir("junit");
        Assert.IsNull(JUnitResults.Read(Files(temp, "<testsuites/>")));
    }

    [TestMethod]
    public void Junit_NoFiles_IsNull() => Assert.IsNull(JUnitResults.Read([]));

    [TestMethod]
    public void Junit_InvalidXml_IsNull()
    {
        using var temp = new TempDir("junit");
        Assert.IsNull(JUnitResults.Read(Files(temp, GradleA, "<testsuite><oops")));
    }

    [TestMethod]
    public void Junit_ReadsSwiftXunitOutput()
    {
        using var temp = new TempDir("junit");
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <testsuites>
              <testsuite name="xctest" tests="2" failures="1">
                <testcase classname="ChallengeTests.CalcTests" name="testAdds"><failure message="XCTAssertEqual failed"/></testcase>
                <testcase classname="ChallengeTests.CalcTests" name="testSubtracts"/>
              </testsuite>
            </testsuites>
            """;

        var result = JUnitResults.Read(Files(temp, xml));

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Total);
        Assert.AreEqual(1, result.Failed);
        Assert.AreEqual("ChallengeTests.CalcTests.testAdds", result.Failures[0].Name);
        Assert.AreEqual("XCTAssertEqual failed", result.Failures[0].Message);
    }

    [TestMethod]
    public void Trx_ReadsCountersAndFailures()
    {
        using var temp = new TempDir("trx");
        var trx = temp.Write("a.trx", """
            <?xml version="1.0" encoding="UTF-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Passes" outcome="Passed"/>
                <UnitTestResult testName="Fails" outcome="Failed"><Output><ErrorInfo><Message>Assert.AreEqual failed.</Message></ErrorInfo></Output></UnitTestResult>
              </Results>
              <ResultSummary outcome="Failed"><Counters total="2" executed="2" passed="1" failed="1"/></ResultSummary>
            </TestRun>
            """);

        var result = TrxResults.Read([trx]);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Passed);
        Assert.AreEqual(1, result.Failed);
        Assert.AreEqual(2, result.Total);
        Assert.AreEqual("Fails", result.Failures.Single().Name);
        Assert.AreEqual("Assert.AreEqual failed.", result.Failures.Single().Message);
    }

    [TestMethod]
    public void Trx_NotExecutedCountsAsNotPassed()
    {
        using var temp = new TempDir("trx");
        var trx = temp.Write("a.trx", """
            <?xml version="1.0" encoding="UTF-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <ResultSummary outcome="Completed"><Counters total="3" executed="2" passed="2" failed="0" notExecuted="1" inconclusive="0"/></ResultSummary>
            </TestRun>
            """);

        var result = TrxResults.Read([trx]);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Passed);
        Assert.AreEqual(1, result.Failed);
        Assert.AreEqual(3, result.Total);
    }

    [TestMethod]
    public void Trx_NoFiles_IsNull() => Assert.IsNull(TrxResults.Read([]));
}
