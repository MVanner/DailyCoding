namespace Daily.Cli.Tests;

[TestClass]
public sealed class CatalogTests
{
    private const string LinqTopic = """
        { "id": "linq", "name": "LINQ",
          "seeds": { "Beginner": { "learningGoal": "Filter and project", "gotchas": ["deferred execution"], "goodSolutionShows": ["Where/Select"], "shapeHints": ["orders"] } } }
        """;

    private const string AsyncTopic = """
        { "id": "async-await", "name": "async/await",
          "seeds": { "Intermediate": { "learningGoal": "Await correctly", "gotchas": ["async void"], "goodSolutionShows": ["Task-returning"], "shapeHints": [] },
                     "Advanced": { "learningGoal": "Cancellation", "gotchas": ["ignored token"], "goodSolutionShows": ["token passed down"] } } }
        """;

    [TestMethod]
    public void Load_ReadsAllTopics()
    {
        using var dir = new TempDir();
        dir.Write("linq.json", LinqTopic);
        dir.Write("async-await.json", AsyncTopic);

        var topics = CatalogLoader.Load(dir.Path);

        Assert.HasCount(2, topics);
        var asyncTopic = topics.Single(t => t.Id == "async-await");
        Assert.AreEqual("async/await", asyncTopic.Name);
        CollectionAssert.AreEquivalent(new[] { Level.Intermediate, Level.Advanced }, asyncTopic.Seeds.Keys.ToArray());
        Assert.AreEqual("ignored token", asyncTopic.Seeds[Level.Advanced].Gotchas.Single());
        Assert.IsEmpty(asyncTopic.Seeds[Level.Advanced].ShapeHints);
    }

    [TestMethod]
    public void Load_ThrowsOnDuplicateId()
    {
        using var dir = new TempDir();
        dir.Write("linq.json", LinqTopic);
        dir.Write("sub/linq.json", LinqTopic);

        var ex = Assert.ThrowsExactly<CatalogException>(() => CatalogLoader.Load(dir.Path));
        StringAssert.Contains(ex.Message, "linq");
    }

    [TestMethod]
    public void Load_ThrowsWhenTopicHasNoSeeds()
    {
        using var dir = new TempDir();
        dir.Write("empty.json", """{ "id": "empty", "name": "Empty", "seeds": {} }""");

        var ex = Assert.ThrowsExactly<CatalogException>(() => CatalogLoader.Load(dir.Path));
        StringAssert.Contains(ex.Message, "empty.json");
    }

    [TestMethod]
    public void Load_ThrowsWhenIdDoesNotMatchFileName()
    {
        using var dir = new TempDir();
        dir.Write("generics.json", LinqTopic);

        var ex = Assert.ThrowsExactly<CatalogException>(() => CatalogLoader.Load(dir.Path));
        StringAssert.Contains(ex.Message, "generics.json");
    }

    [TestMethod]
    public void Load_ThrowsOnInvalidJson()
    {
        using var dir = new TempDir();
        dir.Write("broken.json", "{ \"id\": ");

        var ex = Assert.ThrowsExactly<CatalogException>(() => CatalogLoader.Load(dir.Path));
        StringAssert.Contains(ex.Message, "broken.json");
    }

    [TestMethod]
    public void RealCatalog_IsValid()
    {
        var topics = CatalogLoader.Load(Path.Combine(RepoPaths.PluginRoot, "catalog"));

        Assert.IsGreaterThanOrEqualTo(3, topics.Count);
        foreach (var topic in topics)
        {
            Assert.IsGreaterThanOrEqualTo(2, topic.Seeds.Count, $"{topic.Id} should cover at least 2 levels");
            foreach (var (level, seed) in topic.Seeds)
            {
                var where = $"{topic.Id}/{level}";
                Assert.IsFalse(string.IsNullOrWhiteSpace(seed.LearningGoal), where);
                Assert.IsNotEmpty(seed.Gotchas, where);
                Assert.IsNotEmpty(seed.GoodSolutionShows, where);
            }
        }
    }

    [TestMethod]
    public void RealCatalogs_AreValid()
    {
        var languages = LanguageLoader.Load(Path.Combine(RepoPaths.PluginRoot, "languages"));
        var catalogs = Catalogs.Load(languages);

        foreach (var language in languages)
        {
            var topics = catalogs[language.Id];
            Assert.IsGreaterThanOrEqualTo(10, topics.Count, $"{language.Id} needs at least 10 topics");
            foreach (var topic in topics)
            {
                Assert.IsGreaterThanOrEqualTo(2, topic.Seeds.Count, $"{language.Id}/{topic.Id} should cover at least 2 levels");
                foreach (var (level, seed) in topic.Seeds)
                {
                    var where = $"{language.Id}/{topic.Id}/{level}";
                    Assert.IsFalse(string.IsNullOrWhiteSpace(seed.LearningGoal), where);
                    Assert.IsNotEmpty(seed.Gotchas, where);
                    Assert.IsNotEmpty(seed.GoodSolutionShows, where);
                    Assert.IsNotEmpty(seed.ShapeHints, where);
                }
            }
        }
    }

    [TestMethod]
    [DataRow("csharp", Level.Beginner)]
    [DataRow("csharp", Level.Intermediate)]
    [DataRow("csharp", Level.Advanced)]
    [DataRow("csharp", Level.Expert)]
    [DataRow("swift", Level.Beginner)]
    [DataRow("swift", Level.Intermediate)]
    [DataRow("swift", Level.Advanced)]
    [DataRow("swift", Level.Expert)]
    [DataRow("kotlin", Level.Beginner)]
    [DataRow("kotlin", Level.Intermediate)]
    [DataRow("kotlin", Level.Advanced)]
    [DataRow("kotlin", Level.Expert)]
    [DataRow("lua", Level.Beginner)]
    [DataRow("lua", Level.Intermediate)]
    [DataRow("lua", Level.Advanced)]
    [DataRow("lua", Level.Expert)]
    public void EveryLanguage_HasAtLeastThreeTopicsAtEveryLevel(string languageId, Level level)
    {
        var languages = LanguageLoader.Load(Path.Combine(RepoPaths.PluginRoot, "languages"));
        var count = Catalogs.Load(languages)[languageId].Count(t => t.Seeds.ContainsKey(level));

        Assert.IsGreaterThanOrEqualTo(3, count, $"{languageId}/{level}: {count} topics");
    }

    [TestMethod]
    public void RealCatalog_HasTheStarterTopics()
    {
        var ids = CatalogLoader.Load(Path.Combine(RepoPaths.PluginRoot, "catalog")).Select(t => t.Id).ToHashSet();

        string[] expected =
        [
            "linq", "async-await", "pattern-matching", "generics", "records-immutability", "nullable-reference-types",
            "collections", "span-memory", "delegates-events-lambdas", "disposable", "exceptions", "interfaces-default-impl",
            "dependency-injection", "strings-formatting", "dates-times", "concurrency-primitives", "iterators-yield",
            "extension-methods", "equality-hashing", "reflection-attributes", "source-generators", "performance-allocations",
            "testability",
        ];
        CollectionAssert.AreEquivalent(expected, ids.ToArray());
    }

    [TestMethod]
    [DataRow(Level.Beginner, 10)]
    [DataRow(Level.Intermediate, 8)]
    [DataRow(Level.Advanced, 8)]
    [DataRow(Level.Expert, 8)]
    public void EveryLevel_HasEnoughTopics(Level level, int minimum)
    {
        var count = CatalogLoader.Load(Path.Combine(RepoPaths.PluginRoot, "catalog")).Count(t => t.Seeds.ContainsKey(level));

        Assert.IsGreaterThanOrEqualTo(minimum, count, $"{level}: {count} topics");
    }

    [TestMethod]
    public void OldRecursiveLoad_SeesOnlyCsharpTopics()
    {
        var root = RepoPaths.PluginRoot;
        var csharp = Catalogs.Load(LanguageLoader.Load(Path.Combine(root, "languages")))["csharp"];

        Assert.HasCount(csharp.Count, CatalogLoader.Load(Path.Combine(root, "catalog")));
        var catalogDir = Path.Combine(root, "catalog");
        var nested = Directory.EnumerateFiles(catalogDir, "*.json", SearchOption.AllDirectories)
            .Where(f => Path.GetDirectoryName(f) != catalogDir);
        Assert.IsFalse(nested.Any(), "catalog/ must only hold C# topics at its top level");
    }
}
