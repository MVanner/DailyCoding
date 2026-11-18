namespace Daily.Cli.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class VerifierTests
{
    private static readonly DateOnly Day = new(2026, 11, 16);

    private const string Stub = "namespace Challenge; public static class Calc { public static int Sum(IEnumerable<int> values) => throw new NotImplementedException(); }";
    private const string Reference = "namespace Challenge; public static class Calc { public static int Sum(IEnumerable<int> values) => values.Sum(); }";
    private const string WrongReference = "namespace Challenge; public static class Calc { public static int Sum(IEnumerable<int> values) => values.Count(); }";
    private const string Naive = "namespace Challenge; public static class Calc { public static int Sum(IEnumerable<int> values) => 0; }";

    private const string GoodTests = """
        namespace Challenge.Tests;
        [TestClass]
        public sealed class CalcTests
        {
            [TestMethod] public void Empty_IsZero() => Assert.AreEqual(0, Challenge.Calc.Sum([]));
            [TestMethod] public void Adds() => Assert.AreEqual(6, Challenge.Calc.Sum([1, 2, 3]));
            [TestMethod] public void Negatives() => Assert.AreEqual(4, Challenge.Calc.Sum([-1, 5]));
            [TestMethod] public void Single() => Assert.AreEqual(10, Challenge.Calc.Sum([10]));
        }
        """;

    private const string LenientTests = """
        namespace Challenge.Tests;
        [TestClass]
        public sealed class CalcTests
        {
            [TestMethod] public void Empty() => Challenge.Calc.Sum([]);
            [TestMethod] public void Some() => Challenge.Calc.Sum([1, 2, 3]);
            [TestMethod] public void Negatives() => Challenge.Calc.Sum([-1, 5]);
        }
        """;

    private const string TwoTests = """
        namespace Challenge.Tests;
        [TestClass]
        public sealed class CalcTests
        {
            [TestMethod] public void Adds() => Assert.AreEqual(6, Challenge.Calc.Sum([1, 2, 3]));
            [TestMethod] public void Single() => Assert.AreEqual(10, Challenge.Calc.Sum([10]));
        }
        """;

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _data = new("data");
        private readonly TempDir _ws = new("ws dir");

        public Fixture(string tests, string? reference = Reference, string? naive = Naive, string stub = Stub)
        {
            Paths = new DailyPaths(_data.Path, RepoPaths.PluginRoot);
            var config = new DailyConfig(Level.Beginner, _ws.Path, Dotnet.InstalledSdkMajor(), 1);
            Folder = Scaffolder.Create(Paths, config, Day, "sum", force: false);
            File.WriteAllText(Path.Combine(Folder, "Challenge", "Calc.cs"), stub);
            File.WriteAllText(Path.Combine(Folder, "Challenge.Tests", "CalcTests.cs"), tests);
            if (reference is not null) Write(".verify/reference/Calc.cs", reference);
            if (naive is not null) Write(".verify/naive/Calc.cs", naive);
        }

        public DailyPaths Paths { get; }
        public string Folder { get; }

        public void Write(string relative, string content)
        {
            var file = Path.Combine(Folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        public void Dispose()
        {
            _data.Dispose();
            _ws.Dispose();
        }
    }

    [TestMethod]
    public void GoodChallenge_IsOk_AndReferenceIsStashed()
    {
        using var f = new Fixture(GoodTests);

        var result = Verifier.Run(f.Paths, f.Folder, Day);

        Assert.IsTrue(result.Ok, string.Join(" | ", result.Problems));
        Assert.IsTrue(result.StubBuilds);
        Assert.AreEqual(4, result.ReferencePassed);
        Assert.AreEqual(4, result.ReferenceTotal);
        Assert.AreEqual(3, result.NaiveFailed);
        Assert.AreEqual(4, result.NaiveTotal);
        Assert.IsTrue(File.Exists(Path.Combine(f.Paths.ReferencesDir, "2026-11-16", "Calc.cs")));
        Assert.IsFalse(Directory.Exists(Path.Combine(f.Folder, ".verify")));
        StringAssert.Contains(File.ReadAllText(Path.Combine(f.Folder, "Challenge", "Calc.cs")), "NotImplementedException");
    }

    [TestMethod]
    public void LenientTests_FailGate()
    {
        using var f = new Fixture(LenientTests);

        var result = Verifier.Run(f.Paths, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(0, result.NaiveFailed);
        Assert.IsTrue(result.Problems.Any(p => p.Contains("naive", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(Directory.Exists(Path.Combine(f.Folder, ".verify")), ".verify kept for fixing");
    }

    [TestMethod]
    public void WrongReference_FailsGate()
    {
        using var f = new Fixture(GoodTests, reference: WrongReference);

        var result = Verifier.Run(f.Paths, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsLessThan(result.ReferenceTotal, result.ReferencePassed);
        Assert.IsTrue(result.Problems.Any(p => p.Contains("reference", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void TooFewTests_FailsGate()
    {
        using var f = new Fixture(TwoTests);

        var result = Verifier.Run(f.Paths, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsTrue(result.Problems.Any(p => p.Contains("at least 3", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void MissingNaive_ReportsProblem()
    {
        using var f = new Fixture(GoodTests, naive: null);

        var result = Verifier.Run(f.Paths, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsTrue(result.Problems.Any(p => p.Contains(".verify/naive", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void StubThatDoesNotBuild_FailsGate()
    {
        using var f = new Fixture(GoodTests, stub: "namespace Challenge; public static class Calc { }");

        var result = Verifier.Run(f.Paths, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsFalse(result.StubBuilds);
    }

    [TestMethod]
    public void NaiveThatThrows_FailsGate_EvenWhenTestsWouldCatchIt()
    {
        using var f = new Fixture(LenientTests, naive: "namespace Challenge; public static class Calc { public static int Sum(IEnumerable<int> values) => throw new InvalidOperationException(); }");

        var result = Verifier.Run(f.Paths, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsTrue(result.Problems.Any(p => p.Contains("naive", StringComparison.Ordinal) && p.Contains("throw", StringComparison.Ordinal)), string.Join(" | ", result.Problems));
    }

    [TestMethod]
    public void VariantFileNotInStub_FailsGate()
    {
        using var f = new Fixture(GoodTests);
        f.Write(".verify/reference/Helper.cs", "namespace Challenge; internal static class Helper { }");

        var result = Verifier.Run(f.Paths, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsTrue(result.Problems.Any(p => p.Contains("Helper.cs", StringComparison.Ordinal)), string.Join(" | ", result.Problems));
    }

    [TestMethod]
    public void StubThatAlreadyPasses_FailsGate()
    {
        using var f = new Fixture(GoodTests, stub: Reference);

        var result = Verifier.Run(f.Paths, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsTrue(result.StubBuilds);
        Assert.IsTrue(result.Problems.Any(p => p.StartsWith("stub:", StringComparison.Ordinal)), string.Join(" | ", result.Problems));
    }
}
