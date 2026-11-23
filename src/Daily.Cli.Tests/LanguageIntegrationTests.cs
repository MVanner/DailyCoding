namespace Daily.Cli.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class LanguageIntegrationTests
{
    private static readonly DateOnly Day = new(2026, 11, 16);

    private const string LuaStub = """
        local M = {}
        function M.sum(values) error("not implemented") end
        return M
        """;

    private const string LuaReference = """
        local M = {}
        function M.sum(values)
          local total = 0
          for _, v in ipairs(values) do total = total + v end
          return total
        end
        return M
        """;

    private const string LuaNaive = """
        local M = {}
        function M.sum(values) return 0 end
        return M
        """;

    private const string LuaNaiveWithError = """
        local M = {}
        function M.sum(values) if #values > 100 then error("too many") end return 0 end
        return M
        """;

    private const string LuaGoodTests = """
        local calc = require("calc")
        describe("calc.sum", function()
          it("is zero for empty", function() assert.equals(0, calc.sum({})) end)
          it("adds", function() assert.equals(6, calc.sum({1, 2, 3})) end)
          it("handles negatives", function() assert.equals(4, calc.sum({-1, 5})) end)
          it("handles a single value", function() assert.equals(10, calc.sum({10})) end)
        end)
        """;

    private const string LuaLenientTests = """
        local calc = require("calc")
        describe("calc.sum", function()
          it("empty", function() calc.sum({}) end)
          it("some", function() calc.sum({1, 2, 3}) end)
          it("negatives", function() calc.sum({-1, 5}) end)
        end)
        """;

    private const string LuaBrokenTests = "describe(\"calc\", function( end end";

    private static bool LuaRunnerAvailable() =>
        new[] { "luajit", "lua", "nvim" }.Any(tool =>
            (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Any(dir => File.Exists(Path.Combine(dir, tool))));

    private class LangFixture : IDisposable
    {
        private readonly TempDir _data = new("data");
        private readonly TempDir _ws = new("ws dir");

        protected LangFixture(string language, string stubFile, string stub, string testFile, string tests, string verifyFile, string? reference, string? naive)
        {
            Paths = new DailyPaths(_data.Path, RepoPaths.PluginRoot);
            var config = new DailyConfig([new(language, Level.Beginner)], _ws.Path, 10, 1);
            Language = LanguageLoader.Get(LanguageLoader.Load(Paths.LanguagesDir), language);
            Folder = Scaffolder.Create(Paths, config, Language, Day, "sum", force: false);
            Write(stubFile, stub);
            Write(testFile, tests);
            if (reference is not null) Write(".verify/reference/" + verifyFile, reference);
            if (naive is not null) Write(".verify/naive/" + verifyFile, naive);
        }

        public DailyPaths Paths { get; }
        public Language Language { get; }
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

    private sealed class Fixture(string tests, string? reference = LuaReference, string? naive = LuaNaive, string stub = LuaStub)
        : LangFixture("lua", "src/calc.lua", stub, "spec/challenge_spec.lua", tests, "calc.lua", reference, naive);

    private static void RequireLua()
    {
        if (!LuaRunnerAvailable())
        {
            Assert.Inconclusive("No Lua runner (luajit, lua or nvim) on PATH.");
        }
    }

    [TestMethod]
    public void Lua_GoodChallenge_Verifies()
    {
        RequireLua();
        using var f = new Fixture(LuaGoodTests);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsTrue(result.Ok, string.Join(" | ", result.Problems));
        Assert.IsTrue(result.StubBuilds);
        Assert.AreEqual(4, result.ReferencePassed);
        Assert.AreEqual(4, result.ReferenceTotal);
        Assert.AreEqual(3, result.NaiveFailed);
    }

    [TestMethod]
    public void Lua_LenientTests_Rejected()
    {
        RequireLua();
        using var f = new Fixture(LuaLenientTests);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(0, result.NaiveFailed);
        Assert.IsTrue(result.Problems.Any(p => p.Contains("naive", StringComparison.OrdinalIgnoreCase)), string.Join(" | ", result.Problems));
    }

    [TestMethod]
    public void Lua_NaiveWithError_Rejected()
    {
        RequireLua();
        using var f = new Fixture(LuaGoodTests, naive: LuaNaiveWithError);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsTrue(result.Problems.Any(p => p.Contains("naive", StringComparison.Ordinal) && p.Contains("error(", StringComparison.Ordinal)), string.Join(" | ", result.Problems));
    }

    [TestMethod]
    public void Lua_BrokenStub_DoesNotBuild()
    {
        RequireLua();
        using var f = new Fixture(LuaBrokenTests);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsFalse(result.StubBuilds);
    }

    [TestMethod]
    public void Lua_RunTests_ReportsFailures()
    {
        RequireLua();
        using var f = new Fixture(LuaGoodTests);

        var (result, _) = TestRunner.Run(f.Language, f.Folder, TimeSpan.FromMinutes(1));

        Assert.IsNotNull(result);
        Assert.AreEqual(4, result.Total);
        Assert.AreEqual(4, result.Failed);
        Assert.IsTrue(result.Failures.Any(x => x.Name.Contains("calc.sum", StringComparison.Ordinal) && x.Message.Contains("not implemented", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Lua_InvalidUtf8InFailureMessage_StillParsed()
    {
        RequireLua();
        const string tests = """
            local calc = require("calc")
            describe("calc.sum", function()
              it("fails with invalid utf8 in message", function() error("bad " .. string.sub("æøå", 1, 1)) end)
              it("adds", function() assert.equals(6, calc.sum({1, 2, 3})) end)
            end)
            """;
        using var f = new Fixture(tests, stub: LuaReference);

        var (result, _) = TestRunner.Run(f.Language, f.Folder, TimeSpan.FromMinutes(1));

        Assert.IsNotNull(result);
        Assert.IsGreaterThan(0, result.Total);
        Assert.AreEqual(1, result.Failed);
    }

    private const string KotlinStub = """
        package challenge

        fun sum(values: List<Int>): Int = TODO()
        """;

    private const string KotlinReference = """
        package challenge

        fun sum(values: List<Int>): Int = values.sum()
        """;

    private const string KotlinNaive = """
        package challenge

        fun sum(values: List<Int>): Int = 0
        """;

    private const string KotlinNaiveWithTodo = """
        package challenge

        fun sum(values: List<Int>): Int = TODO()
        """;

    private const string KotlinGoodTests = """
        package challenge

        import kotlin.test.Test
        import kotlin.test.assertEquals

        class SumTest {
            @Test fun emptyIsZero() = assertEquals(0, sum(emptyList()))
            @Test fun adds() = assertEquals(6, sum(listOf(1, 2, 3)))
            @Test fun handlesNegatives() = assertEquals(4, sum(listOf(-1, 5)))
            @Test fun singleValue() = assertEquals(10, sum(listOf(10)))
        }
        """;

    private const string KotlinCoroutineTests = """
        package challenge

        import kotlin.test.Test
        import kotlin.test.assertEquals
        import kotlinx.coroutines.test.runTest

        class SumTest {
            @Test fun emptyIsZero() = assertEquals(0, sum(emptyList()))
            @Test fun adds() = assertEquals(6, sum(listOf(1, 2, 3)))
            @Test fun handlesNegatives() = assertEquals(4, sum(listOf(-1, 5)))
            @Test fun singleValue() = assertEquals(10, sum(listOf(10)))
            @Test fun worksInsideRunTest() = runTest { assertEquals(3, sum(listOf(1, 2))) }
        }
        """;

    private const string KotlinBrokenStub = """
        package challenge

        fun sum(values: List<Int>): Int = this does not compile
        """;

    private static bool JavaAtLeast17()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("java", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            var m = System.Text.RegularExpressions.Regex.Match(output, @"\d+(\.\d+)*");
            if (!m.Success) return false;
            var parts = m.Value.Split('.');
            var major = parts[0] == "1" && parts.Length > 1 ? int.Parse(parts[1]) : int.Parse(parts[0]);
            return major >= 17;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void RequireJava()
    {
        if (!JavaAtLeast17())
        {
            Assert.Inconclusive("No java >= 17 on PATH.");
        }
    }

    private sealed class KotlinFixture(string tests, string stub = KotlinStub, string? reference = KotlinReference, string? naive = KotlinNaive)
        : LangFixture("kotlin", "src/main/kotlin/Sum.kt", stub, "src/test/kotlin/SumTest.kt", tests, "Sum.kt", reference, naive);

    [TestMethod]
    public void Kotlin_GoodChallenge_Verifies()
    {
        RequireJava();
        using var f = new KotlinFixture(KotlinGoodTests);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsTrue(result.Ok, string.Join(" | ", result.Problems));
        Assert.IsTrue(result.StubBuilds);
        Assert.AreEqual(4, result.ReferencePassed);
        Assert.AreEqual(4, result.ReferenceTotal);
        Assert.AreEqual(3, result.NaiveFailed);
    }

    [TestMethod]
    public void Kotlin_NaiveWithTodo_Rejected()
    {
        RequireJava();
        using var f = new KotlinFixture(KotlinGoodTests, naive: KotlinNaiveWithTodo);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsTrue(result.Problems.Any(p => p.Contains("naive", StringComparison.Ordinal) && p.Contains("TODO", StringComparison.Ordinal)), string.Join(" | ", result.Problems));
    }

    [TestMethod]
    public void Kotlin_BrokenStub_DoesNotBuild()
    {
        RequireJava();
        using var f = new KotlinFixture(KotlinGoodTests, stub: KotlinBrokenStub);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsFalse(result.StubBuilds);
    }

    [TestMethod]
    public void Kotlin_CoroutineTest_Runs()
    {
        RequireJava();
        using var f = new KotlinFixture(KotlinCoroutineTests);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsTrue(result.Ok, string.Join(" | ", result.Problems));
        Assert.AreEqual(5, result.ReferenceTotal);
        Assert.AreEqual(5, result.ReferencePassed);
    }

    private const string SwiftStub = """
        public func sum(_ values: [Int]) -> Int {
            return -1 // TODO: implement
        }
        """;

    private const string SwiftReference = """
        public func sum(_ values: [Int]) -> Int {
            return values.reduce(0, +)
        }
        """;

    private const string SwiftNaive = """
        public func sum(_ values: [Int]) -> Int {
            return 0
        }
        """;

    private const string SwiftNaiveWithFatalError = """
        public func sum(_ values: [Int]) -> Int {
            fatalError("not implemented")
        }
        """;

    private const string SwiftCrashingReference = """
        public func sum(_ values: [Int]) -> Int {
            fatalError("boom")
        }
        """;

    private const string SwiftBrokenStub = """
        public func sum(_ values: [Int]) -> Int {
            this does not compile
        }
        """;

    private const string SwiftGoodTests = """
        import XCTest
        @testable import Challenge

        final class SumTests: XCTestCase {
            func testEmptyIsZero() { XCTAssertEqual(sum([]), 0) }
            func testAdds() { XCTAssertEqual(sum([1, 2, 3]), 6) }
            func testHandlesNegatives() { XCTAssertEqual(sum([-1, 5]), 4) }
            func testSingleValue() { XCTAssertEqual(sum([10]), 10) }
        }
        """;

    private static bool SwiftAtLeast6()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("swift", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            var m = System.Text.RegularExpressions.Regex.Match(output, @"Swift version (\d+)");
            return m.Success && int.Parse(m.Groups[1].Value) >= 6;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void RequireSwift()
    {
        if (!SwiftAtLeast6())
        {
            Assert.Inconclusive("No swift >= 6.0 on PATH.");
        }
    }

    private sealed class SwiftFixture(string tests, string stub = SwiftStub, string? reference = SwiftReference, string? naive = SwiftNaive)
        : LangFixture("swift", "Sources/Challenge/Sum.swift", stub, "Tests/ChallengeTests/SumTests.swift", tests, "Sum.swift", reference, naive);

    [TestMethod]
    public void Swift_GoodChallenge_Verifies()
    {
        RequireSwift();
        using var f = new SwiftFixture(SwiftGoodTests);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsTrue(result.Ok, string.Join(" | ", result.Problems));
        Assert.IsTrue(result.StubBuilds);
        Assert.AreEqual(4, result.ReferencePassed);
        Assert.AreEqual(4, result.ReferenceTotal);
        Assert.AreEqual(3, result.NaiveFailed);
    }

    [TestMethod]
    public void Swift_NaiveWithFatalError_Rejected()
    {
        RequireSwift();
        using var f = new SwiftFixture(SwiftGoodTests, naive: SwiftNaiveWithFatalError);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsTrue(result.Problems.Any(p => p.Contains("naive", StringComparison.Ordinal) && p.Contains("fatalError", StringComparison.Ordinal)), string.Join(" | ", result.Problems));
    }

    [TestMethod]
    public void Swift_BrokenStub_DoesNotBuild()
    {
        RequireSwift();
        using var f = new SwiftFixture(SwiftGoodTests, stub: SwiftBrokenStub);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsFalse(result.StubBuilds);
    }

    [TestMethod]
    public void Swift_CrashingTest_IsNotAPass()
    {
        RequireSwift();
        using var f = new SwiftFixture(SwiftGoodTests, reference: SwiftCrashingReference);

        var result = Verifier.Run(f.Paths, f.Language, f.Folder, Day);

        Assert.IsFalse(result.Ok);
        Assert.IsFalse(result.ReferencePassed > 0 && result.ReferencePassed == result.ReferenceTotal, $"{result.ReferencePassed}/{result.ReferenceTotal}");
    }
}
