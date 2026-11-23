using System.Text.Json;

namespace Daily.Cli.Tests;

[TestClass]
public sealed class CommandsTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _data = new("data");

        public Fixture(bool configured = true)
        {
            Paths = new DailyPaths(_data.Path, RepoPaths.PluginRoot);
            Store = new StateStore(Paths);
            if (configured)
            {
                Store.SaveConfig(new DailyConfig([new("csharp", Level.Beginner)], "/ws", 11, 3));
            }
        }

        public DailyPaths Paths { get; }
        public StateStore Store { get; }
        public DateOnly Today => CliHelpers.Today();
        public HistoryEntry? Entry => Store.LoadState().For(Today);

        public (int Exit, string Out, string Err) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = Program.Run([.. args, "--data", Paths.DataDir, "--plugin-root", Paths.PluginRoot], new Dictionary<string, string?>(), stdout, stderr);
            return (exit, stdout.ToString(), stderr.ToString());
        }

        public void Dispose() => _data.Dispose();
    }

    [TestMethod]
    public void Today_CreatesPickWhenHookDidNotRun()
    {
        using var f = new Fixture();

        var (exit, output, _) = f.Run("today");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(ChallengeStatus.Picked, f.Entry!.Status);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.AreEqual(f.Entry.TopicId, json.GetProperty("topic").GetProperty("id").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("seed").GetProperty("learningGoal").GetString()));
    }

    [TestMethod]
    public void Today_ReturnsExistingEntryAndSeed()
    {
        using var f = new Fixture();
        f.Store.Upsert(new HistoryEntry(f.Today, "linq", Level.Beginner, false, ChallengeStatus.Started) { Folder = "/ws/x" });
        f.Store.Upsert(new HistoryEntry(f.Today.AddDays(-1), "async-await", Level.Beginner, false, ChallengeStatus.Completed));
        var linqBeginner = CatalogLoader.Load(Path.Combine(f.Paths.PluginRoot, "catalog")).Single(t => t.Id == "linq").Seeds[Level.Beginner];

        var json = JsonDocument.Parse(f.Run("today").Out).RootElement;

        Assert.AreEqual("LINQ", json.GetProperty("topic").GetProperty("name").GetString());
        Assert.AreEqual("Started", json.GetProperty("entry").GetProperty("status").GetString());
        Assert.AreEqual("/ws/x", json.GetProperty("entry").GetProperty("folder").GetString());
        Assert.AreEqual(linqBeginner.LearningGoal, json.GetProperty("seed").GetProperty("learningGoal").GetString());
        Assert.AreEqual("async-await", json.GetProperty("recent").EnumerateArray().Single().GetProperty("topicId").GetString());
        Assert.AreEqual(JsonValueKind.Null, json.GetProperty("referenceDir").ValueKind);
    }

    [TestMethod]
    public void Today_ReportsReferenceDirWhenStashed()
    {
        using var f = new Fixture();
        var stash = Directory.CreateDirectory(Path.Combine(f.Paths.ReferencesDir, f.Today.ToString("yyyy-MM-dd"))).FullName;

        var json = JsonDocument.Parse(f.Run("today").Out).RootElement;

        Assert.AreEqual(stash, json.GetProperty("referenceDir").GetString());
    }

    [TestMethod]
    public void Today_NotConfigured_Exits2()
    {
        using var f = new Fixture(configured: false);

        Assert.AreEqual(2, f.Run("today").Exit);
    }

    [TestMethod]
    public void Record_Started_ThenCompleted_SetsTimes()
    {
        using var f = new Fixture();
        f.Run("today");

        Assert.AreEqual(0, f.Run("record", "started", "--folder", "/ws/2026-11-16-linq").Exit);
        Assert.AreEqual(ChallengeStatus.Started, f.Entry!.Status);
        Assert.AreEqual("/ws/2026-11-16-linq", f.Entry.Folder);
        Assert.IsNotNull(f.Entry.StartedAt);

        Assert.AreEqual(0, f.Run("record", "completed", "--passed", "false").Exit);
        Assert.AreEqual(0, f.Run("record", "completed", "--passed", "true").Exit);
        Assert.AreEqual(ChallengeStatus.Completed, f.Entry!.Status);
        Assert.IsTrue(f.Entry.Passed);
        Assert.IsNotNull(f.Entry.CompletedAt);
        Assert.AreEqual("/ws/2026-11-16-linq", f.Entry.Folder);
    }

    [TestMethod]
    public void Record_StartedAfterSkipped_Fails()
    {
        using var f = new Fixture();
        Assert.AreEqual(0, f.Run("record", "skipped").Exit);

        var (exit, _, err) = f.Run("record", "started", "--folder", "/ws/x");

        Assert.AreEqual(1, exit);
        StringAssert.Contains(err, "Skipped");
        Assert.AreEqual(ChallengeStatus.Skipped, f.Entry!.Status);
    }

    [TestMethod]
    public void Record_StartedAfterGenerationFailed_Fails()
    {
        using var f = new Fixture();
        f.Run("record", "generation-failed");

        Assert.AreEqual(1, f.Run("record", "started", "--folder", "/ws/x").Exit);
    }

    [TestMethod]
    public void Record_CompletedBeforeStarted_Fails()
    {
        using var f = new Fixture();

        Assert.AreEqual(1, f.Run("record", "completed", "--passed", "true").Exit);
    }

    [TestMethod]
    public void Record_GenerationAttempt_Increments()
    {
        using var f = new Fixture();

        f.Run("record", "generation-attempt");
        f.Run("record", "generation-attempt");

        Assert.AreEqual(2, f.Entry!.GenerationAttempts);
        Assert.AreEqual(ChallengeStatus.Picked, f.Entry.Status);
    }

    [TestMethod]
    public void Hint_Increments()
    {
        using var f = new Fixture();
        f.Run("record", "started", "--folder", "/ws/x");

        f.Run("hint");
        var (exit, output, _) = f.Run("hint");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(2, JsonDocument.Parse(output).RootElement.GetProperty("hints").GetInt32());
        Assert.AreEqual(2, f.Entry!.Hints);
    }

    [TestMethod]
    public void Hint_BeforeStart_Fails()
    {
        using var f = new Fixture();

        Assert.AreEqual(1, f.Run("hint").Exit);
    }

    [TestMethod]
    public void ConfigSet_FirstTime_GeneratesSaltAndDefaults()
    {
        using var f = new Fixture(configured: false);

        Assert.AreEqual(0, f.Run("config", "set", "level", "csharp", "advanced").Exit);

        var config = f.Store.LoadConfig()!;
        Assert.AreEqual(Level.Advanced, config.LevelFor("csharp"));
        Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DailyCoding"), config.Workspace);
        Assert.AreNotEqual(0, config.Salt);
        Assert.IsGreaterThanOrEqualTo(10, config.SdkMajor);
    }

    [TestMethod]
    public void ConfigSet_UpdatesWorkspaceAndSdkMajor_KeepsSalt()
    {
        using var f = new Fixture();

        f.Run("config", "set", "workspace", "/Users/jane doe/Daily");
        f.Run("config", "set", "sdkMajor", "10");

        Assert.AreEqual(new DailyConfig([new("csharp", Level.Beginner)], "/Users/jane doe/Daily", 10, 3), f.Store.LoadConfig());
    }

    [TestMethod]
    [DataRow("sdkMajor", "eleven")]
    [DataRow("colour", "blue")]
    public void ConfigSet_InvalidInput_Fails(string key, string value)
    {
        using var f = new Fixture();

        Assert.AreEqual(1, f.Run("config", "set", key, value).Exit);
    }

    private static string PluginRootWithLua(TempDir root)
    {
        var json = File.ReadAllText(Path.Combine(RepoPaths.PluginRoot, "languages", "csharp", "language.json"))
            .Replace("\"id\": \"csharp\"", "\"id\": \"lua\"")
            .Replace("\"name\": \"C#\"", "\"name\": \"Lua\"")
            .Replace("\"../../catalog\"", "\"" + Path.Combine(RepoPaths.PluginRoot, "catalog").Replace("\\", "/") + "\"")
            .Replace("\"../../templates/challenge\"", "\"" + Path.Combine(RepoPaths.PluginRoot, "templates", "challenge").Replace("\\", "/") + "\"");
        root.Write("languages/lua/language.json", json);
        root.Write("languages/csharp/language.json", File.ReadAllText(Path.Combine(RepoPaths.PluginRoot, "languages", "csharp", "language.json")));
        return root.Path;
    }

    private static (int Exit, DailyConfig? Config) RunConfigWithLua(DailyConfig initial, params string[] args)
    {
        using var data = new TempDir("data");
        using var root = new TempDir("root");
        var paths = new DailyPaths(data.Path, PluginRootWithLua(root));
        var store = new StateStore(paths);
        store.SaveConfig(initial);
        var exit = Program.Run([.. args, "--data", paths.DataDir, "--plugin-root", paths.PluginRoot], new Dictionary<string, string?>(), new StringWriter(), new StringWriter());
        return (exit, store.LoadConfig());
    }

    [TestMethod]
    public void ConfigSetLanguages_KeepsExistingLevels()
    {
        var (exit, config) = RunConfigWithLua(new DailyConfig([new("csharp", Level.Expert)], "/ws", 11, 3), "config", "set", "languages", "csharp,lua");

        Assert.AreEqual(0, exit);
        CollectionAssert.AreEqual(new[] { new LanguageSetting("csharp", Level.Expert), new LanguageSetting("lua", Level.Beginner) }, config!.Languages);
    }

    [TestMethod]
    public void ConfigSetLanguages_RejectsUnknownId()
    {
        var initial = new DailyConfig([new("csharp", Level.Expert)], "/ws", 11, 3);

        var (exit, config) = RunConfigWithLua(initial, "config", "set", "languages", "csharp,cobol");

        Assert.AreEqual(1, exit);
        Assert.AreEqual(initial, config);
    }

    [TestMethod]
    public void ConfigSetLevel_PerLanguage()
    {
        var initial = new DailyConfig([new("csharp", Level.Expert), new("lua", Level.Beginner)], "/ws", 11, 3);

        var (exit, config) = RunConfigWithLua(initial, "config", "set", "level", "lua", "Intermediate");

        Assert.AreEqual(0, exit);
        CollectionAssert.AreEqual(new[] { new LanguageSetting("csharp", Level.Expert), new LanguageSetting("lua", Level.Intermediate) }, config!.Languages);
    }

    [TestMethod]
    public void ConfigSetLevel_WithoutLanguage_IsRejected()
    {
        using var f = new Fixture();

        Assert.AreEqual(1, f.Run("config", "set", "level", "Advanced").Exit);
        Assert.AreEqual(Level.Beginner, f.Store.LoadConfig()!.LevelFor("csharp"));
    }

    [TestMethod]
    public void ConfigSetLevel_InvalidLevel_IsRejected()
    {
        using var f = new Fixture();

        Assert.AreEqual(1, f.Run("config", "set", "level", "csharp", "guru").Exit);
        Assert.AreEqual(Level.Beginner, f.Store.LoadConfig()!.LevelFor("csharp"));
    }

    [TestMethod]
    public void ConfigSetLevel_ForUnselectedLanguage_IsRejected()
    {
        using var f = new Fixture();

        Assert.AreEqual(1, f.Run("config", "set", "level", "lua", "Advanced").Exit);
    }

    [TestMethod]
    public void ConfigShow_NotConfigured_Exits2()
    {
        using var f = new Fixture(configured: false);

        Assert.AreEqual(2, f.Run("config", "show").Exit);
    }

    [TestMethod]
    public void ConfigShow_PrintsConfig()
    {
        using var f = new Fixture();

        var json = JsonDocument.Parse(f.Run("config", "show").Out).RootElement;

        var language = json.GetProperty("languages").EnumerateArray().Single();
        Assert.AreEqual("csharp", language.GetProperty("id").GetString());
        Assert.AreEqual("Beginner", language.GetProperty("level").GetString());
    }

    [TestMethod]
    public void Today_KeepsNonAsciiAndAngleBracketsReadable()
    {
        using var f = new Fixture();
        f.Store.SaveConfig(new DailyConfig([new("csharp", Level.Expert)], "/Users/søren/DailyCoding", 11, 3));

        var output = f.Run("today").Out;

        StringAssert.Contains(output, "/Users/søren/DailyCoding");
        Assert.IsFalse(output.Contains("\\u", StringComparison.Ordinal), "JSON for skills should not contain \\u escapes");
    }

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd");

    [TestMethod]
    public void Today_ReportsUnfinishedEarlierChallenge()
    {
        using var f = new Fixture();
        var yesterday = f.Today.AddDays(-1);
        f.Store.Upsert(new HistoryEntry(yesterday.AddDays(-1), "linq", Level.Beginner, false, ChallengeStatus.Started) { Folder = "/ws/older" });
        f.Store.Upsert(new HistoryEntry(yesterday, "async-await", Level.Beginner, false, ChallengeStatus.Started) { Folder = "/ws/yesterday" });
        var stash = Directory.CreateDirectory(Path.Combine(f.Paths.ReferencesDir, D(yesterday))).FullName;

        var unfinished = JsonDocument.Parse(f.Run("today").Out).RootElement.GetProperty("unfinished");

        Assert.AreEqual(D(yesterday), unfinished.GetProperty("entry").GetProperty("date").GetString());
        Assert.AreEqual("/ws/yesterday", unfinished.GetProperty("entry").GetProperty("folder").GetString());
        Assert.AreEqual(stash, unfinished.GetProperty("referenceDir").GetString());
    }

    [TestMethod]
    public void Today_NoUnfinished_IsNull()
    {
        using var f = new Fixture();
        f.Store.Upsert(new HistoryEntry(f.Today.AddDays(-1), "linq", Level.Beginner, false, ChallengeStatus.Completed));

        var json = JsonDocument.Parse(f.Run("today").Out).RootElement;

        Assert.AreEqual(JsonValueKind.Null, json.GetProperty("unfinished").ValueKind);
    }

    [TestMethod]
    public void Record_WithDate_UpdatesThatDaysEntryOnly()
    {
        using var f = new Fixture();
        var yesterday = f.Today.AddDays(-1);
        f.Store.Upsert(new HistoryEntry(yesterday, "linq", Level.Beginner, false, ChallengeStatus.Started) { Folder = "/ws/y" });

        Assert.AreEqual(0, f.Run("record", "completed", "--passed", "true", "--date", D(yesterday)).Exit);

        var state = f.Store.LoadState();
        Assert.AreEqual(ChallengeStatus.Completed, state.For(yesterday)!.Status);
        Assert.IsNull(state.For(f.Today));
    }

    [TestMethod]
    public void Record_WithPastDateAndNoEntry_Fails()
    {
        using var f = new Fixture();

        var (exit, _, err) = f.Run("record", "skipped", "--date", D(f.Today.AddDays(-3)));

        Assert.AreEqual(1, exit);
        StringAssert.Contains(err, "No challenge");
    }

    [TestMethod]
    public void Hint_WithDate_CountsOnThatDay()
    {
        using var f = new Fixture();
        var yesterday = f.Today.AddDays(-1);
        f.Store.Upsert(new HistoryEntry(yesterday, "linq", Level.Beginner, false, ChallengeStatus.Started));

        Assert.AreEqual(0, f.Run("hint", "--date", D(yesterday)).Exit);

        Assert.AreEqual(1, f.Store.LoadState().For(yesterday)!.Hints);
    }

    [TestMethod]
    public void Scaffold_WithDate_UsesThatDateInFolderName()
    {
        using var f = new Fixture();
        using var ws = new TempDir("ws");
        f.Run("config", "set", "workspace", ws.Path);

        var output = f.Run("scaffold", "date-pinned", "--date", "2026-11-15").Out;

        StringAssert.Contains(output, "2026-11-15-csharp-date-pinned");
    }

    [TestMethod]
    public void InvalidDateOption_Fails()
    {
        using var f = new Fixture();

        Assert.AreEqual(1, f.Run("today", "--date", "16-11-2026").Exit);
    }

    [TestMethod]
    public void Today_IncludesLanguage()
    {
        using var f = new Fixture();

        var language = JsonDocument.Parse(f.Run("today").Out).RootElement.GetProperty("language");

        Assert.AreEqual("csharp", language.GetProperty("id").GetString());
        Assert.AreEqual("Challenge", language.GetProperty("stubDir").GetString());
        Assert.AreEqual("Challenge.Tests", language.GetProperty("testDir").GetString());
        Assert.IsTrue(File.Exists(language.GetProperty("guide").GetString()));
        Assert.IsTrue(language.TryGetProperty("available", out _));
        Assert.IsTrue(language.TryGetProperty("missing", out _));
    }

    [TestMethod]
    public void Languages_ListsEveryLanguageWithLevelHints()
    {
        using var f = new Fixture();

        var (exit, output, _) = f.Run("languages");

        Assert.AreEqual(0, exit);
        var csharp = JsonDocument.Parse(output).RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "csharp");
        Assert.AreEqual("C#", csharp.GetProperty("name").GetString());
        Assert.IsTrue(csharp.GetProperty("levelHints").TryGetProperty("Beginner", out _));
        Assert.IsTrue(csharp.TryGetProperty("available", out _));
    }

    private const string LuaDescriptor = """
        {
          "id": "lua", "name": "Lua", "stubDir": "src", "testDir": "spec",
          "sourceExtensions": [".lua"], "ignoreDirs": [], "test": { "commands": [ { "args": ["busted"] } ], "results": "r.xml", "format": "junit" },
          "naiveForbidden": "\\berror\\b", "requires": [], "ide": "x", "runnerHint": "install busted",
          "levelHints": { "Beginner": "a", "Intermediate": "b", "Advanced": "c", "Expert": "d" }
        }
        """;

    private const string LuaTopic = """
        { "id": "tables", "name": "Tables", "seeds": { "Beginner": { "learningGoal": "g", "gotchas": [], "goodSolutionShows": [] } } }
        """;

    /// <summary>A plugin root with the real C# language (absolute catalog/template paths) and a Lua language with one topic.</summary>
    private static DailyPaths TempPluginRoot(TempDir root, TempDir data)
    {
        var csharpJson = File.ReadAllText(Path.Combine(RepoPaths.PluginRoot, "languages", "csharp", "language.json"))
            .Replace("../../catalog", Path.Combine(RepoPaths.PluginRoot, "catalog").Replace("\\", "/"))
            .Replace("../../templates/challenge", Path.Combine(RepoPaths.PluginRoot, "templates", "challenge").Replace("\\", "/"));
        root.Write("languages/csharp/language.json", csharpJson);
        root.Write("languages/lua/language.json", LuaDescriptor);
        root.Write("languages/lua/catalog/tables.json", LuaTopic);
        return new DailyPaths(data.Path, root.Path);
    }

    private static (int Exit, string Out, string Err) RunWith(DailyPaths paths, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = Program.Run([.. args, "--data", paths.DataDir, "--plugin-root", paths.PluginRoot], new Dictionary<string, string?>(), stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [TestMethod]
    public void Today_NoUsableLanguage_Exits1()
    {
        using var root = new TempDir("root");
        using var data = new TempDir("data");
        var paths = TempPluginRoot(root, data);
        File.Delete(Path.Combine(paths.PluginRoot, "languages", "lua", "catalog", "tables.json"));
        new StateStore(paths).SaveConfig(new DailyConfig([new("lua", Level.Beginner)], "/ws", 11, 3));

        var (exit, _, err) = RunWith(paths, "today");

        Assert.AreEqual(1, exit);
        StringAssert.Contains(err, "run /daily:setup");
    }

    [TestMethod]
    public void Repick_ExcludesLanguage()
    {
        using var root = new TempDir("root");
        using var data = new TempDir("data");
        var paths = TempPluginRoot(root, data);
        var store = new StateStore(paths);
        store.SaveConfig(new DailyConfig([new("csharp", Level.Beginner), new("lua", Level.Beginner)], "/ws", 11, 3));
        var today = CliHelpers.Today();
        store.Upsert(new HistoryEntry(today, "tables", Level.Beginner, false, ChallengeStatus.Picked) { Language = "lua" });

        var (exit, output, err) = RunWith(paths, "repick", "--exclude", "lua");

        Assert.AreEqual(0, exit, err);
        Assert.AreEqual("csharp", store.LoadState().For(today)!.Language);
        Assert.AreEqual("csharp", JsonDocument.Parse(output).RootElement.GetProperty("language").GetString());
    }

    [TestMethod]
    public void Repick_ExcludesCommaSeparatedList()
    {
        using var root = new TempDir("root");
        using var data = new TempDir("data");
        var paths = TempPluginRoot(root, data);
        root.Write("languages/kotlin/language.json", LuaDescriptor.Replace("\"lua\"", "\"kotlin\""));
        root.Write("languages/kotlin/catalog/tables.json", LuaTopic);
        var store = new StateStore(paths);
        store.SaveConfig(new DailyConfig([new("csharp", Level.Beginner), new("lua", Level.Beginner), new("kotlin", Level.Beginner)], "/ws", 11, 3));
        var today = CliHelpers.Today();
        store.Upsert(new HistoryEntry(today, "tables", Level.Beginner, false, ChallengeStatus.Picked) { Language = "lua" });

        var (exit, _, err) = RunWith(paths, "repick", "--exclude", "lua,csharp");

        Assert.AreEqual(0, exit, err);
        Assert.AreEqual("kotlin", store.LoadState().For(today)!.Language);
    }

    [TestMethod]
    public void Repick_StartedEntry_Fails()
    {
        using var root = new TempDir("root");
        using var data = new TempDir("data");
        var paths = TempPluginRoot(root, data);
        var store = new StateStore(paths);
        store.SaveConfig(new DailyConfig([new("csharp", Level.Beginner), new("lua", Level.Beginner)], "/ws", 11, 3));
        var today = CliHelpers.Today();
        store.Upsert(new HistoryEntry(today, "tables", Level.Beginner, false, ChallengeStatus.Started) { Language = "lua" });

        var (exit, _, err) = RunWith(paths, "repick", "--exclude", "lua");

        Assert.AreEqual(1, exit);
        Assert.IsFalse(string.IsNullOrWhiteSpace(err));
        Assert.AreEqual("lua", store.LoadState().For(today)!.Language);
    }

    [TestMethod]
    public void Repick_NoOtherUsableLanguage_Fails()
    {
        using var root = new TempDir("root");
        using var data = new TempDir("data");
        var paths = TempPluginRoot(root, data);
        var store = new StateStore(paths);
        store.SaveConfig(new DailyConfig([new("lua", Level.Beginner)], "/ws", 11, 3));
        var today = CliHelpers.Today();
        store.Upsert(new HistoryEntry(today, "tables", Level.Beginner, false, ChallengeStatus.Picked) { Language = "lua" });

        var (exit, _, err) = RunWith(paths, "repick", "--exclude", "lua");

        Assert.AreEqual(1, exit);
        StringAssert.Contains(err, "No usable language");
        Assert.AreEqual("lua", store.LoadState().For(today)!.Language);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void RunTests_UsesFolderMarker_EvenWhenLanguageNotInConfig()
    {
        using var root = new TempDir("root");
        using var data = new TempDir("data");
        using var ws = new TempDir("ws");
        var paths = TempPluginRoot(root, data);
        var config = new DailyConfig([new("lua", Level.Beginner)], ws.Path, Dotnet.InstalledSdkMajor(), 1);
        new StateStore(paths).SaveConfig(config);
        var folder = Scaffolder.Create(paths, config, ScaffolderTests.Csharp(paths), new DateOnly(2026, 11, 16), "smoke", force: false);
        File.WriteAllText(Path.Combine(folder, "Challenge", "Calc.cs"), "namespace Challenge; public static class Calc { public static int Add(int a, int b) => a + b; }");
        File.WriteAllText(Path.Combine(folder, "Challenge.Tests", "CalcTests.cs"),
            "namespace Challenge.Tests; [TestClass] public sealed class CalcTests { [TestMethod] public void Adds() => Assert.AreEqual(3, Challenge.Calc.Add(1, 2)); }");

        var (exit, output, err) = RunWith(paths, "run-tests", folder);

        Assert.AreEqual(0, exit, err);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.IsTrue(json.GetProperty("ok").GetBoolean(), output);
        Assert.AreEqual(1, json.GetProperty("total").GetInt32());
        Assert.AreEqual(1, json.GetProperty("passed").GetInt32());
    }

    [TestMethod]
    public void Warmup_UnknownLanguage_Exits1()
    {
        using var f = new Fixture();

        var (exit, _, err) = f.Run("warmup", "cobol");

        Assert.AreEqual(1, exit);
        StringAssert.Contains(err, "Unknown language");
    }

    private static void WarmupOk(string languageId)
    {
        using var f = new Fixture();
        var language = LanguageLoader.Get(LanguageLoader.Load(f.Paths.LanguagesDir), languageId);
        if (!ToolCheck.Check(language).Available)
        {
            Assert.Inconclusive($"{language.Name} toolchain not available.");
        }

        var (exit, output, err) = f.Run("warmup", languageId);

        Assert.AreEqual(0, exit, err);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.IsTrue(json.GetProperty("ok").GetBoolean(), output);
        Assert.IsGreaterThanOrEqualTo(0.0, json.GetProperty("seconds").GetDouble());
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Warmup_Lua_Ok() => WarmupOk("lua");

    [TestMethod]
    [TestCategory("Integration")]
    public void Warmup_Kotlin_Ok() => WarmupOk("kotlin");

    [TestMethod]
    [TestCategory("Integration")]
    public void Warmup_Swift_Ok() => WarmupOk("swift");

    [TestMethod]
    [TestCategory("Integration")]
    public void Warmup_BrokenSample_IsNotOk()
    {
        using var root = new TempDir("root");
        using var data = new TempDir("data");
        var realLua = Path.Combine(RepoPaths.PluginRoot, "languages", "lua");
        foreach (var file in Directory.EnumerateFiles(realLua, "*", SearchOption.AllDirectories))
        {
            root.Write(Path.Combine("languages", "lua", Path.GetRelativePath(realLua, file)), File.ReadAllText(file));
        }

        root.Write("languages/lua/warmup/src/warmup.lua", "local M = {}\nfunction M.add(a, b) return a - b end\nreturn M\n");
        var paths = new DailyPaths(data.Path, root.Path);
        if (!ToolCheck.Check(LanguageLoader.Get(LanguageLoader.Load(paths.LanguagesDir), "lua")).Available)
        {
            Assert.Inconclusive("No Lua runner (luajit, lua or nvim) on PATH.");
        }

        var (exit, output, err) = RunWith(paths, "warmup", "lua");

        Assert.AreEqual(0, exit, err);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.IsFalse(json.GetProperty("ok").GetBoolean(), output);
        Assert.IsFalse(string.IsNullOrWhiteSpace(json.GetProperty("problem").GetString()), output);
    }

    [TestMethod]
    public void Warmup_LanguageWithoutSample_IsOkAndNothingRuns()
    {
        using var f = new Fixture();

        var (exit, output, err) = f.Run("warmup", "csharp");

        Assert.AreEqual(0, exit, err);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.IsTrue(json.GetProperty("ok").GetBoolean(), output);
        Assert.AreEqual(0.0, json.GetProperty("seconds").GetDouble());
    }

    [TestMethod]
    public void RunTests_MissingFolder_Exits1()
    {
        using var f = new Fixture();
        var missing = Path.Combine(f.Paths.DataDir, "nope");

        var (exit, _, err) = f.Run("run-tests", missing);

        Assert.AreEqual(1, exit);
        StringAssert.Contains(err, $"Folder not found: {missing}");
    }

    [TestMethod]
    public void Verify_MissingFolder_Exits1()
    {
        using var f = new Fixture();
        var missing = Path.Combine(f.Paths.DataDir, "nope");

        var (exit, _, err) = f.Run("verify", missing);

        Assert.AreEqual(1, exit);
        StringAssert.Contains(err, $"Folder not found: {missing}");
    }
}
