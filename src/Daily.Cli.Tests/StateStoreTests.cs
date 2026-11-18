namespace Daily.Cli.Tests;

[TestClass]
public sealed class StateStoreTests
{
    private static readonly DateOnly Day = new(2026, 11, 16);

    [TestMethod]
    public void SaveThenLoad_RoundTrips()
    {
        using var dir = new TempDir();
        var store = new StateStore(new DailyPaths(dir.Path, dir.Path));
        var config = new DailyConfig(Level.Advanced, "/work space/DailyCoding", 11, 42);
        var entry = new HistoryEntry(Day, "linq", Level.Advanced, Stretch: true, ChallengeStatus.Completed)
        {
            Passed = true,
            Hints = 2,
            GenerationAttempts = 1,
            Folder = "/work space/DailyCoding/2026-11-16-x",
            StartedAt = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.FromHours(1)),
            CompletedAt = new DateTimeOffset(2026, 11, 16, 8, 17, 0, TimeSpan.FromHours(1)),
        };

        store.SaveConfig(config);
        store.SaveState(new DailyState(Day, [entry]));

        Assert.AreEqual(config, store.LoadConfig());
        var state = store.LoadState();
        Assert.AreEqual(Day, state.LastNudgeDate);
        Assert.AreEqual(entry, state.History.Single());
    }

    [TestMethod]
    public void LoadConfig_ReturnsNullWhenMissing()
    {
        using var dir = new TempDir();
        var store = new StateStore(new DailyPaths(Path.Combine(dir.Path, "not-created-yet"), dir.Path));

        Assert.IsNull(store.LoadConfig());
        Assert.IsEmpty(store.LoadState().History);
    }

    [TestMethod]
    public void LoadState_CorruptFile_ReturnsEmptyAndKeepsBadCopy()
    {
        using var dir = new TempDir();
        var paths = new DailyPaths(dir.Path, dir.Path);
        File.WriteAllText(paths.StateFile, """{"History": [""");

        var state = new StateStore(paths).LoadState();

        Assert.IsNull(state.LastNudgeDate);
        Assert.IsEmpty(state.History);
        Assert.IsTrue(File.Exists(paths.StateFile + ".bad"));
        Assert.IsFalse(File.Exists(paths.StateFile));
    }

    [TestMethod]
    public void LoadConfig_CorruptFile_ReturnsNullAndKeepsBadCopy()
    {
        using var dir = new TempDir();
        var paths = new DailyPaths(dir.Path, dir.Path);
        File.WriteAllText(paths.ConfigFile, "not json");

        Assert.IsNull(new StateStore(paths).LoadConfig());
        Assert.IsTrue(File.Exists(paths.ConfigFile + ".bad"));
    }

    [TestMethod]
    public void Upsert_ReplacesSameDate()
    {
        using var dir = new TempDir();
        var store = new StateStore(new DailyPaths(dir.Path, dir.Path));
        store.Upsert(new HistoryEntry(Day.AddDays(-1), "linq", Level.Beginner, false, ChallengeStatus.Completed));
        store.Upsert(new HistoryEntry(Day, "linq", Level.Beginner, false, ChallengeStatus.Picked));

        store.Upsert(new HistoryEntry(Day, "linq", Level.Beginner, false, ChallengeStatus.Started));

        var state = store.LoadState();
        Assert.HasCount(2, state.History);
        Assert.AreEqual(ChallengeStatus.Started, state.For(Day)!.Status);
    }

    [TestMethod]
    public void SaveState_LeavesNoTempFile()
    {
        using var dir = new TempDir();
        var paths = new DailyPaths(dir.Path, dir.Path);

        new StateStore(paths).SaveState(new DailyState(Day, []));

        CollectionAssert.AreEquivalent(new[] { "state.json" }, Directory.GetFiles(dir.Path).Select(Path.GetFileName).ToArray());
    }
}
