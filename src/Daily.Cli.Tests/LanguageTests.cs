namespace Daily.Cli.Tests;

[TestClass]
public sealed class LanguageTests
{
    private const string Minimal = """
        {
          "id": "lua", "name": "Lua", "stubDir": "challenge", "testDir": "tests",
          "sourceExtensions": [".lua"], "ignoreDirs": [],
          "test": { "commands": [ { "args": ["busted"] } ], "results": "out/*.xml", "format": "junit" },
          "naiveForbidden": "\\berror\\b", "requires": [], "ide": "Open in any editor.",
          "runnerHint": "Install busted.", "levelHints": { "Beginner": "a", "Intermediate": "b", "Advanced": "c", "Expert": "d" }
          EXTRA
        }
        """;

    private static string Descriptor(string extra = "", string? replace = null, string? with = null)
    {
        var json = Minimal.Replace("EXTRA", extra);
        return replace is null ? json : json.Replace(replace, with!);
    }

    [TestMethod]
    public void Load_ResolvesDefaultsNextToDescriptor()
    {
        using var temp = new TempDir("lang");
        temp.Write("languages/lua/language.json", Descriptor());

        var language = LanguageLoader.Get(LanguageLoader.Load(Path.Combine(temp.Path, "languages")), "lua");

        var dir = Path.Combine(temp.Path, "languages", "lua");
        Assert.AreEqual(dir, language.Dir);
        Assert.AreEqual(Path.Combine(dir, "catalog"), language.CatalogDir);
        Assert.AreEqual(Path.Combine(dir, "template"), language.TemplateDir);
        Assert.AreEqual(Path.Combine(dir, "guide.md"), language.GuidePath);
        Assert.AreEqual("Lua", language.Name);
    }

    [TestMethod]
    public void Load_ResolvesRelativeOverrides()
    {
        using var temp = new TempDir("lang");
        temp.Write("languages/lua/language.json", Descriptor(""","catalogDir": "../../catalog", "templateDir": "../../templates/x" """));

        var language = LanguageLoader.Get(LanguageLoader.Load(Path.Combine(temp.Path, "languages")), "lua");

        Assert.AreEqual(Path.Combine(temp.Path, "catalog"), language.CatalogDir);
        Assert.AreEqual(Path.Combine(temp.Path, "templates", "x"), language.TemplateDir);
    }

    [TestMethod]
    public void Load_ThrowsWhenIdDoesNotMatchFolder()
    {
        using var temp = new TempDir("lang");
        temp.Write("languages/other/language.json", Descriptor());

        var ex = Assert.ThrowsExactly<LanguageException>(() => LanguageLoader.Load(Path.Combine(temp.Path, "languages")));
        StringAssert.Contains(ex.Message, "lua");
    }

    [TestMethod]
    public void Load_ThrowsOnUnknownFormat()
    {
        using var temp = new TempDir("lang");
        temp.Write("languages/lua/language.json", Descriptor(replace: "\"junit\"", with: "\"xml\""));

        var ex = Assert.ThrowsExactly<LanguageException>(() => LanguageLoader.Load(Path.Combine(temp.Path, "languages")));
        StringAssert.Contains(ex.Message, "format");
    }

    [TestMethod]
    public void Load_ThrowsWhenNaiveForbiddenIsNotARegex()
    {
        using var temp = new TempDir("lang");
        temp.Write("languages/lua/language.json", Descriptor(replace: "\\\\berror\\\\b", with: "(unclosed"));

        var ex = Assert.ThrowsExactly<LanguageException>(() => LanguageLoader.Load(Path.Combine(temp.Path, "languages")));
        StringAssert.Contains(ex.Message, "naiveForbidden");
    }

    [TestMethod]
    public void Load_ThrowsWithoutTestCommands()
    {
        using var temp = new TempDir("lang");
        temp.Write("languages/lua/language.json", Descriptor(replace: "[ { \"args\": [\"busted\"] } ]", with: "[]"));

        var ex = Assert.ThrowsExactly<LanguageException>(() => LanguageLoader.Load(Path.Combine(temp.Path, "languages")));
        StringAssert.Contains(ex.Message, "commands");
    }

    [TestMethod]
    public void Load_RejectsNullCommandElement()
    {
        using var temp = new TempDir("lang");
        temp.Write("languages/lua/language.json", Descriptor(replace: "[ { \"args\": [\"busted\"] } ]", with: "[ null ]"));

        var ex = Assert.ThrowsExactly<LanguageException>(() => LanguageLoader.Load(Path.Combine(temp.Path, "languages")));
        StringAssert.Contains(ex.Message, "language.json");
        StringAssert.Contains(ex.Message, "test.commands");
    }

    [TestMethod]
    public void Load_RejectsNullRequirement()
    {
        using var temp = new TempDir("lang");
        temp.Write("languages/lua/language.json", Descriptor(replace: "\"requires\": []", with: "\"requires\": [ null ]"));

        var ex = Assert.ThrowsExactly<LanguageException>(() => LanguageLoader.Load(Path.Combine(temp.Path, "languages")));
        StringAssert.Contains(ex.Message, "language.json");
        StringAssert.Contains(ex.Message, "requires");
    }

    [TestMethod]
    public void Load_RejectsRequirementWithoutCommandOrHint()
    {
        using var temp = new TempDir("lang");
        temp.Write("languages/lua/language.json", Descriptor(replace: "\"requires\": []", with: "\"requires\": [ { \"command\": \"lua\", \"args\": [], \"installHint\": \"\" } ]"));

        var ex = Assert.ThrowsExactly<LanguageException>(() => LanguageLoader.Load(Path.Combine(temp.Path, "languages")));
        StringAssert.Contains(ex.Message, "requires");
        StringAssert.Contains(ex.Message, "installHint");
    }

    [TestMethod]
    public void IsOnPath_FindsExecutableInPathString()
    {
        using var temp = new TempDir("path");
        var name = OperatingSystem.IsWindows() ? "mytool.exe" : "mytool";
        temp.Write(name, "");
        var path = temp.Path + Path.PathSeparator + "/nonexistent-dir";

        Assert.IsTrue(ProcessRunner.IsOnPath("mytool", path));
        Assert.IsFalse(ProcessRunner.IsOnPath("no-such-tool-xyz", path));
    }

    [TestMethod]
    public void Get_ThrowsOnUnknownId()
    {
        using var temp = new TempDir("lang");
        temp.Write("languages/lua/language.json", Descriptor());

        Assert.ThrowsExactly<LanguageException>(() => LanguageLoader.Get(LanguageLoader.Load(Path.Combine(temp.Path, "languages")), "cobol"));
    }

    [TestMethod]
    public void RealCsharpDescriptor_PointsAtExistingCatalogAndTemplate()
    {
        var csharp = LanguageLoader.Get(LanguageLoader.Load(Path.Combine(RepoPaths.PluginRoot, "languages")), "csharp");

        Assert.IsTrue(csharp.CatalogDir.EndsWith("catalog", StringComparison.Ordinal), csharp.CatalogDir);
        Assert.IsTrue(Directory.Exists(csharp.CatalogDir));
        Assert.IsTrue(Directory.Exists(csharp.TemplateDir));
        Assert.IsTrue(File.Exists(csharp.GuidePath));
        Assert.AreEqual("trx", csharp.Descriptor.Test.Format);
        Assert.HasCount(4, csharp.Descriptor.LevelHints);
    }

    [TestMethod]
    public void EveryLanguage_HasTemplateGuideAndCatalog()
    {
        var languages = LanguageLoader.Load(Path.Combine(RepoPaths.PluginRoot, "languages"));
        CollectionAssert.IsSubsetOf(new[] { "csharp", "kotlin", "lua", "swift" }, languages.Select(l => l.Id).ToArray());

        foreach (var language in languages)
        {
            Assert.IsTrue(Directory.Exists(language.TemplateDir), $"{language.Id} template");
            Assert.IsTrue(File.Exists(language.GuidePath), $"{language.Id} guide");
            var guide = File.ReadAllLines(language.GuidePath);
            foreach (var heading in new[] { "## Layout", "## Stub", "## Tests", "## Reference and naive", "## Allowed libraries", "## Hint examples" })
                Assert.IsTrue(guide.Contains(heading), $"{language.Id} guide lacks {heading}");
            Assert.IsTrue(guide.Any(l => l.StartsWith("## Review: idiomatic ", StringComparison.Ordinal)), $"{language.Id} guide lacks review heading");
            Assert.IsTrue(Directory.Exists(language.CatalogDir), $"{language.Id} catalog dir");
            Assert.IsGreaterThanOrEqualTo(10, Directory.GetFiles(language.CatalogDir, "*.json").Length, $"{language.Id} catalog size");
        }
    }
}
