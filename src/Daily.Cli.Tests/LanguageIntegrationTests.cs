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

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _data = new("data");
        private readonly TempDir _ws = new("ws dir");

        public Fixture(string tests, string? reference = LuaReference, string? naive = LuaNaive, string stub = LuaStub)
        {
            Paths = new DailyPaths(_data.Path, RepoPaths.PluginRoot);
            var config = new DailyConfig([new("lua", Level.Beginner)], _ws.Path, 10, 1);
            Language = LanguageLoader.Get(LanguageLoader.Load(Paths.LanguagesDir), "lua");
            Folder = Scaffolder.Create(Paths, config, Language, Day, "sum", force: false);
            Write("src/calc.lua", stub);
            Write("spec/challenge_spec.lua", tests);
            if (reference is not null) Write(".verify/reference/calc.lua", reference);
            if (naive is not null) Write(".verify/naive/calc.lua", naive);
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

    private sealed class KotlinFixture : IDisposable
    {
        private readonly TempDir _data = new("data");
        private readonly TempDir _ws = new("ws dir");

        public KotlinFixture(string tests, string stub = KotlinStub, string? reference = KotlinReference, string? naive = KotlinNaive)
        {
            Paths = new DailyPaths(_data.Path, RepoPaths.PluginRoot);
            var config = new DailyConfig([new("kotlin", Level.Beginner)], _ws.Path, 10, 1);
            Language = LanguageLoader.Get(LanguageLoader.Load(Paths.LanguagesDir), "kotlin");
            Folder = Scaffolder.Create(Paths, config, Language, Day, "sum", force: false);
            Write("src/main/kotlin/Sum.kt", stub);
            Write("src/test/kotlin/SumTest.kt", tests);
            if (reference is not null) Write(".verify/reference/Sum.kt", reference);
            if (naive is not null) Write(".verify/naive/Sum.kt", naive);
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
}
