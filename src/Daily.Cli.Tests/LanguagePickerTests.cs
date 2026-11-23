namespace Daily.Cli.Tests;

[TestClass]
public sealed class LanguagePickerTests
{
    private static readonly DateOnly Day = new(2026, 11, 23);

    private static Topic T(string id) => new(id, id, new Dictionary<Level, Seed>
    {
        [Level.Beginner] = new("g", ["x"], ["y"], null),
    });

    private static IReadOnlyDictionary<string, IReadOnlyList<Topic>> Cats(params string[] languages) =>
        languages.ToDictionary(l => l, l => (IReadOnlyList<Topic>)[T("a"), T("b"), T("c")]);

    private static LanguageSetting S(string id) => new(id, Level.Beginner);

    private static HistoryEntry H(string language, int daysAgo, ChallengeStatus status) =>
        new(Day.AddDays(-daysAgo), "a", Level.Beginner, false, status) { Language = language };

    private static DailyConfig Config(params string[] languages) =>
        new(languages.Select(S).ToList(), "/ws", 11, 5);

    [TestMethod]
    public void Choose_NeverPractisedWins()
    {
        var pick = LanguagePicker.Choose([S("csharp"), S("lua")], Cats("csharp", "lua"), [H("csharp", 1, ChallengeStatus.Completed)]);

        Assert.AreEqual("lua", pick?.Id);
    }

    [TestMethod]
    public void Choose_LeastRecentWins()
    {
        var pick = LanguagePicker.Choose([S("csharp"), S("lua")], Cats("csharp", "lua"),
            [H("csharp", 1, ChallengeStatus.Completed), H("lua", 3, ChallengeStatus.Started)]);

        Assert.AreEqual("lua", pick?.Id);
    }

    [TestMethod]
    public void Choose_SkippedDoesntCount()
    {
        var pick = LanguagePicker.Choose([S("csharp"), S("lua")], Cats("csharp", "lua"),
        [
            H("lua", 1, ChallengeStatus.Skipped),
            H("csharp", 5, ChallengeStatus.Completed),
            H("lua", 10, ChallengeStatus.Completed),
        ]);

        Assert.AreEqual("lua", pick?.Id);
    }

    [TestMethod]
    public void Choose_GenerationFailedCountsAsTouch()
    {
        var pick = LanguagePicker.Choose([S("csharp"), S("lua")], Cats("csharp", "lua"),
        [
            H("lua", 1, ChallengeStatus.GenerationFailed),
            H("csharp", 5, ChallengeStatus.Completed),
            H("lua", 10, ChallengeStatus.Completed),
        ]);

        Assert.AreEqual("csharp", pick?.Id);
    }

    [TestMethod]
    public void Choose_TieUsesConfigOrder()
    {
        var pick = LanguagePicker.Choose([S("kotlin"), S("csharp")], Cats("kotlin", "csharp"), []);

        Assert.AreEqual("kotlin", pick?.Id);
    }

    [TestMethod]
    public void Choose_SkipsEmptyCatalog()
    {
        var catalogs = new Dictionary<string, IReadOnlyList<Topic>> { ["lua"] = [], ["csharp"] = [T("a")] };

        var pick = LanguagePicker.Choose([S("lua"), S("csharp")], catalogs, []);

        Assert.AreEqual("csharp", pick?.Id);
    }

    [TestMethod]
    public void Choose_RespectsExclude()
    {
        var pick = LanguagePicker.Choose([S("lua"), S("csharp")], Cats("lua", "csharp"), [], new HashSet<string> { "lua" });

        Assert.AreEqual("csharp", pick?.Id);
    }

    [TestMethod]
    public void Choose_NoneUsable_ReturnsNull()
    {
        Assert.IsNull(LanguagePicker.Choose([S("lua")], Cats("csharp"), []));
        Assert.IsNull(LanguagePicker.Choose([S("lua")], Cats("lua"), [], new HashSet<string> { "lua" }));
    }

    [TestMethod]
    public void CreatePickedEntry_FiltersHistoryToLanguage()
    {
        var catalogs = new Dictionary<string, IReadOnlyList<Topic>>
        {
            ["swift"] = [T("generics"), T("closures")],
            ["csharp"] = [T("generics")],
        };
        var history = new List<HistoryEntry>
        {
            new(Day.AddDays(-1), "generics", Level.Beginner, false, ChallengeStatus.Completed),
            new(Day.AddDays(-2), "x", Level.Beginner, false, ChallengeStatus.Completed) { Language = "swift" },
        };

        var entry = Daily.Cli.Today.CreatePickedEntry(Day, Config("csharp", "swift"), catalogs, history);

        Assert.IsNotNull(entry);
        Assert.AreEqual("swift", entry.Language);
        Assert.IsTrue(catalogs["swift"].Any(t => t.Id == entry.TopicId));
        Assert.AreEqual(ChallengeStatus.Picked, entry.Status);
    }

    [TestMethod]
    public void CreatePickedEntry_NoUsableLanguage_ReturnsNull()
    {
        Assert.IsNull(Daily.Cli.Today.CreatePickedEntry(Day, Config("lua"), Cats("csharp"), []));
    }

    private static (StateStore Store, TempDir Dir) NewStore(params HistoryEntry[] history)
    {
        var dir = new TempDir("data");
        var store = new StateStore(new DailyPaths(dir.Path, dir.Path));
        store.SaveState(new DailyState(null, history.ToList()));
        return (store, dir);
    }

    [TestMethod]
    public void Ensure_PickedEntryWithRemovedLanguage_IsRepicked()
    {
        var (store, dir) = NewStore(new HistoryEntry(Day, "a", Level.Beginner, false, ChallengeStatus.Picked) { Language = "lua" });
        using var _ = dir;

        var entry = Daily.Cli.Today.Ensure(store, Config("csharp"), Cats("csharp"), Day);

        Assert.AreEqual("csharp", entry?.Language);
        Assert.AreEqual("csharp", store.LoadState().For(Day)?.Language);
    }

    [TestMethod]
    public void Ensure_PickedEntryWithUnknownTopic_IsRepicked()
    {
        var (store, dir) = NewStore(new HistoryEntry(Day, "gone", Level.Beginner, false, ChallengeStatus.Picked));
        using var _ = dir;

        var entry = Daily.Cli.Today.Ensure(store, Config("csharp"), Cats("csharp"), Day);

        Assert.AreNotEqual("gone", entry?.TopicId);
        Assert.AreEqual(entry, store.LoadState().For(Day));
    }

    [TestMethod]
    public void Ensure_StartedEntry_IsNeverRepicked()
    {
        var started = new HistoryEntry(Day, "a", Level.Beginner, false, ChallengeStatus.Started) { Language = "lua" };
        var (store, dir) = NewStore(started);
        using var _ = dir;

        var entry = Daily.Cli.Today.Ensure(store, Config("csharp"), Cats("csharp"), Day);

        Assert.AreEqual(started, entry);
    }

    [TestMethod]
    public void Ensure_NoUsableLanguage_ReturnsNull()
    {
        var (store, dir) = NewStore();
        using var _ = dir;

        Assert.IsNull(Daily.Cli.Today.Ensure(store, Config("lua"), Cats("csharp"), Day));
    }
}
