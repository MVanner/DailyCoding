namespace Daily.Cli.Tests;

[TestClass]
public sealed class PickerTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);
    private const int Salt = 1234;

    private static Topic T(string id, params Level[] levels) =>
        new(id, id.ToUpperInvariant(), levels.ToDictionary(l => l, _ => new Seed("goal", ["gotcha"], ["shows"], [])));

    private static HistoryEntry Done(string id, int daysAgo, Level level = Level.Beginner, ChallengeStatus status = ChallengeStatus.Completed) =>
        new(Today.AddDays(-daysAgo), id, level, false, status);

    private static IEnumerable<Pick> Days(int count, Level level, IReadOnlyList<Topic> catalog, IReadOnlyList<HistoryEntry> history, int salt = Salt) =>
        Enumerable.Range(0, count).Select(i => Picker.Choose(Today.AddDays(i), level, salt, catalog, history));

    [TestMethod]
    public void SameInputs_SamePick()
    {
        var catalog = new[] { T("a", Level.Beginner), T("b", Level.Beginner), T("c", Level.Beginner), T("d", Level.Beginner) };
        HistoryEntry[] history = [Done("a", 3)];

        var first = Picker.Choose(Today, Level.Beginner, Salt, catalog, history);
        var second = Picker.Choose(Today, Level.Beginner, Salt, catalog, history);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void DifferentSalt_CanDiffer()
    {
        var catalog = "abcdefgh".Select(c => T(c.ToString(), Level.Beginner)).ToArray();

        var one = Days(30, Level.Beginner, catalog, [], salt: 1).ToList();
        var two = Days(30, Level.Beginner, catalog, [], salt: 2).ToList();

        Assert.IsTrue(one.Zip(two).Any(p => p.First != p.Second));
    }

    [TestMethod]
    public void StretchRate_AboutOneInFour()
    {
        var catalog = "abcdefgh".Select(c => T(c.ToString(), Level.Beginner, Level.Intermediate)).ToArray();

        var stretchDays = Days(365, Level.Beginner, catalog, []).Count(p => p.Stretch);

        Assert.IsTrue(stretchDays is >= 60 and <= 125, $"stretch days: {stretchDays}");
    }

    [TestMethod]
    public void Stretch_PrefersUnseenTopic()
    {
        var catalog = "abcdef".Select(c => T(c.ToString(), Level.Intermediate, Level.Advanced)).ToArray();
        var history = "abcde".Select((c, i) => Done(c.ToString(), 30 + i, Level.Intermediate)).ToArray();

        var stretch = Days(60, Level.Intermediate, catalog, history).First(p => p.Stretch);

        Assert.AreEqual(new Pick("f", Level.Intermediate, true, StretchKind.NewTopic), stretch);
    }

    [TestMethod]
    public void Stretch_LevelsUpWhenAllSeen()
    {
        var catalog = "abcdef".Select(c => T(c.ToString(), Level.Intermediate, Level.Advanced)).ToArray();
        var history = "abcdef".Select((c, i) => Done(c.ToString(), 30 + i, Level.Intermediate)).ToArray();

        var stretch = Days(60, Level.Intermediate, catalog, history).First(p => p.Stretch);

        Assert.AreEqual(Level.Advanced, stretch.Level);
        Assert.AreEqual(StretchKind.LevelUp, stretch.Kind);
    }

    [TestMethod]
    public void Stretch_AtExpertWithAllSeen_IsNotStretch()
    {
        var catalog = "abcd".Select(c => T(c.ToString(), Level.Expert)).ToArray();
        var history = "abcd".Select((c, i) => Done(c.ToString(), 30 + i, Level.Expert)).ToArray();

        var picks = Days(100, Level.Expert, catalog, history).ToList();

        Assert.IsFalse(picks.Any(p => p.Stretch));
        Assert.IsTrue(picks.All(p => p.Level == Level.Expert && p.Kind is null));
    }

    [TestMethod]
    public void Stretch_WithNoHigherLevelSeeds_IsNotStretch()
    {
        var catalog = "abcd".Select(c => T(c.ToString(), Level.Beginner)).ToArray();
        var history = "abcd".Select((c, i) => Done(c.ToString(), 30 + i)).ToArray();

        Assert.IsFalse(Days(100, Level.Beginner, catalog, history).Any(p => p.Stretch));
    }

    [TestMethod]
    public void AvoidsLast5NonSkippedTopics()
    {
        var catalog = "abcdefgh".Select(c => T(c.ToString(), Level.Beginner)).ToArray();
        var history = "abcde".Select((c, i) => Done(c.ToString(), i + 1)).ToArray();

        var picked = Days(60, Level.Beginner, catalog, history).Select(p => p.TopicId).Distinct().ToList();

        CollectionAssert.AreEquivalent(new[] { "f", "g", "h" }, picked);
    }

    [TestMethod]
    public void PrefersLeastRecentlyDone()
    {
        var catalog = "abcdefghi".Select(c => T(c.ToString(), Level.Beginner)).ToArray();
        HistoryEntry[] history =
        [
            Done("a", 100), Done("b", 90), Done("c", 80), Done("d", 70),
            Done("e", 5), Done("f", 4), Done("g", 3), Done("h", 2), Done("i", 1),
        ];

        var picked = Days(60, Level.Beginner, catalog, history).Select(p => p.TopicId).Distinct().ToList();

        CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, picked);
    }

    [TestMethod]
    public void SkippedTopic_CanComeBack()
    {
        var catalog = "abcdef".Select(c => T(c.ToString(), Level.Beginner)).ToArray();
        HistoryEntry[] history =
        [
            Done("f", 6), Done("e", 5), Done("d", 4), Done("c", 3), Done("b", 2),
            Done("a", 1, status: ChallengeStatus.Skipped),
        ];

        var picked = Days(30, Level.Beginner, catalog, history).Select(p => p.TopicId).Distinct().ToList();

        CollectionAssert.AreEqual(new[] { "a" }, picked);
    }

    [TestMethod]
    public void NoSeedAtLevel_FallsBackToLowerLevel()
    {
        var catalog = "abcd".Select(c => T(c.ToString(), Level.Beginner, Level.Intermediate)).ToArray();

        var picks = Days(60, Level.Expert, catalog, []).ToList();

        Assert.IsTrue(picks.All(p => p.Level == Level.Intermediate), string.Join(",", picks.Select(p => p.Level)));
    }

    [TestMethod]
    public void NoSeedAtOrBelowLevel_FallsBackToHigherLevel()
    {
        var catalog = "abcd".Select(c => T(c.ToString(), Level.Advanced)).ToArray();

        var picks = Days(60, Level.Beginner, catalog, []).ToList();

        Assert.IsTrue(picks.All(p => p.Level == Level.Advanced));
    }

    [TestMethod]
    public void AllTopicsRecent_StillPicks()
    {
        var catalog = new[] { T("a", Level.Beginner), T("b", Level.Beginner) };
        HistoryEntry[] history = [Done("a", 2), Done("b", 1)];

        var pick = Picker.Choose(Today, Level.Beginner, Salt, catalog, history);

        Assert.IsTrue(pick.TopicId is "a" or "b");
    }

    [TestMethod]
    public void EmptyCatalog_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => Picker.Choose(Today, Level.Beginner, Salt, [], []));
    }
}
