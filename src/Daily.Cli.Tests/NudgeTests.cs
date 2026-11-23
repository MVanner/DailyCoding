using System.Text.Json;

namespace Daily.Cli.Tests;

[TestClass]
public sealed class NudgeTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);

    private const string Topic = """
        { "id": "async-await", "name": "async/await",
          "seeds": { "Intermediate": { "learningGoal": "g", "gotchas": ["x"], "goodSolutionShows": ["y"] },
                     "Advanced": { "learningGoal": "g", "gotchas": ["x"], "goodSolutionShows": ["y"] } } }
        """;

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _data = new("data");
        private readonly TempDir _root = new("root");

        public Fixture(bool configured = true)
        {
            _root.Write("catalog/async-await.json", Topic);
            _root.Write("src/Daily.Cli/Program.cs", "// v1");
            _root.Write("src/Daily.Cli/Daily.Cli.csproj", "<Project />");
            Paths = new DailyPaths(_data.Path, _root.Path);
            Store = new StateStore(Paths);
            if (configured)
            {
                Store.SaveConfig(new DailyConfig([new("csharp", Level.Intermediate)], "/ws", 11, 7));
                BuildStamp.Write(Paths);
            }
        }

        public DailyPaths Paths { get; }
        public StateStore Store { get; }
        public TempDir Root => _root;

        public DateOnly FindDay(bool stretch)
        {
            var catalog = CatalogLoader.Load(Paths.CatalogDir);
            for (var day = Monday; ; day = day.AddDays(1))
            {
                if (Picker.Choose(day, Level.Intermediate, 7, catalog, []).Stretch == stretch) return day;
            }
        }

        public void Dispose()
        {
            _data.Dispose();
            _root.Dispose();
        }
    }

    private static string Message(string? json)
    {
        Assert.IsNotNull(json);
        return JsonDocument.Parse(json).RootElement.GetProperty("systemMessage").GetString()!;
    }

    [TestMethod]
    public void FirstRunOfDay_ReturnsMessageWithLevelAndTopicName()
    {
        using var f = new Fixture();
        var day = f.FindDay(stretch: false);

        var json = Nudge.Run(f.Paths, day);

        Assert.AreEqual("☕ Today's C# challenge is ready (Intermediate · async/await) — /daily:start when you have 15 min", Message(json));
        var state = f.Store.LoadState();
        Assert.AreEqual(day, state.LastNudgeDate);
        Assert.AreEqual(ChallengeStatus.Picked, state.For(day)!.Status);
        Assert.AreEqual("async-await", state.For(day)!.TopicId);
    }

    [TestMethod]
    public void SecondRunSameDay_ReturnsNull()
    {
        using var f = new Fixture();
        Nudge.Run(f.Paths, Monday);

        Assert.IsNull(Nudge.Run(f.Paths, Monday));
    }

    [TestMethod]
    public void StretchDay_UsesStretchCopy()
    {
        using var f = new Fixture();
        var day = f.FindDay(stretch: true);

        var message = Message(Nudge.Run(f.Paths, day));

        StringAssert.Matches(message, new System.Text.RegularExpressions.Regex(
            @"^☕ Today's C# challenge is ready \((Intermediate|Advanced) · async/await — stretch day\) — /daily:start when you have 15 min$"));
        Assert.IsTrue(f.Store.LoadState().For(day)!.Stretch);
    }

    [TestMethod]
    public void ExistingEntryForToday_IsReused()
    {
        using var f = new Fixture();
        f.Store.Upsert(new HistoryEntry(Monday, "async-await", Level.Advanced, false, ChallengeStatus.Started));

        var message = Message(Nudge.Run(f.Paths, Monday));

        StringAssert.Contains(message, "(Advanced · async/await)");
        Assert.AreEqual(ChallengeStatus.Started, f.Store.LoadState().For(Monday)!.Status);
    }

    [TestMethod]
    public void NotConfigured_ReturnsSetupMessageOncePerDay()
    {
        using var f = new Fixture(configured: false);

        Assert.AreEqual("☕ Daily C# challenges are installed — run /daily:setup to pick your level", Message(Nudge.Run(f.Paths, Monday)));
        Assert.IsNull(Nudge.Run(f.Paths, Monday));
        Assert.IsNotNull(Nudge.Run(f.Paths, Monday.AddDays(1)));
    }

    [TestMethod]
    public void StaleStamp_AppendsSuffix()
    {
        using var f = new Fixture();
        f.Root.Write("src/Daily.Cli/Program.cs", "// v2");

        StringAssert.EndsWith(Message(Nudge.Run(f.Paths, Monday)), " (plugin updated — run /daily:setup to rebuild)");
    }

    [TestMethod]
    public void BuildStamp_ChangesWhenSourceChanges()
    {
        using var f = new Fixture();
        var before = BuildStamp.Compute(f.Paths.PluginRoot);
        Assert.IsFalse(BuildStamp.IsStale(f.Paths));

        f.Root.Write("src/Daily.Cli/New.cs", "class X {}");

        Assert.AreNotEqual(before, BuildStamp.Compute(f.Paths.PluginRoot));
        Assert.IsTrue(BuildStamp.IsStale(f.Paths));
    }

    [TestMethod]
    public void BuildStamp_IgnoresBinAndObj()
    {
        using var f = new Fixture();
        var before = BuildStamp.Compute(f.Paths.PluginRoot);

        f.Root.Write("src/Daily.Cli/obj/Generated.cs", "x");
        f.Root.Write("src/Daily.Cli/bin/Other.cs", "y");

        Assert.AreEqual(before, BuildStamp.Compute(f.Paths.PluginRoot));
    }

    [TestMethod]
    public void NudgeCommand_CorruptState_StillNudgesAndExitsZero()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Paths.StateFile, "{ broken");
        var stdout = new StringWriter();

        var exit = Program.Run(["nudge", "--data", f.Paths.DataDir, "--plugin-root", f.Paths.PluginRoot], new Dictionary<string, string?>(), stdout, new StringWriter());

        Assert.AreEqual(0, exit);
        StringAssert.Contains(Message(stdout.ToString()), "Today's C# challenge is ready");
    }

    [TestMethod]
    public void NudgeCommand_BrokenCatalog_IsSilentLogsAndExitsZero()
    {
        using var f = new Fixture();
        f.Root.Write("catalog/async-await.json", "{ not json");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = Program.Run(["nudge", "--data", f.Paths.DataDir, "--plugin-root", f.Paths.PluginRoot], new Dictionary<string, string?>(), stdout, stderr);

        Assert.AreEqual(0, exit);
        Assert.AreEqual("", stdout.ToString());
        Assert.AreEqual("", stderr.ToString());
        StringAssert.Contains(File.ReadAllText(f.Paths.HookErrorLog), "async-await.json");
    }

    [TestMethod]
    public void NudgeCommand_WithoutPaths_IsSilentAndExitsZero()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = Program.Run(["nudge"], new Dictionary<string, string?>(), stdout, stderr);

        Assert.AreEqual(0, exit);
        Assert.AreEqual("", stdout.ToString() + stderr.ToString());
    }

    [TestMethod]
    public void HookOutput_IsAsciiOnly()
    {
        using var f = new Fixture();

        var json = Nudge.Run(f.Paths, f.FindDay(stretch: false))!;

        Assert.IsTrue(json.All(c => c < 128), json);
        StringAssert.StartsWith(Message(json), "☕ Today's C# challenge");
    }
}
