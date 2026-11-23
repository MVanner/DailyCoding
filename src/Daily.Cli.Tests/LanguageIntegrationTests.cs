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
}
