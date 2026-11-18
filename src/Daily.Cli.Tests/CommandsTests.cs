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
                Store.SaveConfig(new DailyConfig(Level.Beginner, "/ws", 11, 3));
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
        var linqBeginner = CatalogLoader.Load(f.Paths.CatalogDir).Single(t => t.Id == "linq").Seeds[Level.Beginner];

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

        Assert.AreEqual(0, f.Run("config", "set", "level", "advanced").Exit);

        var config = f.Store.LoadConfig()!;
        Assert.AreEqual(Level.Advanced, config.Level);
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

        Assert.AreEqual(new DailyConfig(Level.Beginner, "/Users/jane doe/Daily", 10, 3), f.Store.LoadConfig());
    }

    [TestMethod]
    [DataRow("level", "guru")]
    [DataRow("sdkMajor", "eleven")]
    [DataRow("colour", "blue")]
    public void ConfigSet_InvalidInput_Fails(string key, string value)
    {
        using var f = new Fixture();

        Assert.AreEqual(1, f.Run("config", "set", key, value).Exit);
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

        Assert.AreEqual("Beginner", json.GetProperty("level").GetString());
    }

    [TestMethod]
    public void Today_KeepsNonAsciiAndAngleBracketsReadable()
    {
        using var f = new Fixture();
        f.Store.SaveConfig(new DailyConfig(Level.Expert, "/Users/søren/DailyCoding", 11, 3));

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

        StringAssert.Contains(output, "2026-11-15-date-pinned");
    }

    [TestMethod]
    public void InvalidDateOption_Fails()
    {
        using var f = new Fixture();

        Assert.AreEqual(1, f.Run("today", "--date", "16-11-2026").Exit);
    }
}
