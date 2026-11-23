namespace Daily.Cli.Tests;

[TestClass]
public sealed class DailyPathsTests
{
    [TestMethod]
    public void Resolve_PrefersOptionsOverEnv()
    {
        var env = new Dictionary<string, string?> { ["CLAUDE_PLUGIN_DATA"] = "/env/data", ["CLAUDE_PLUGIN_ROOT"] = "/env/root" };

        var paths = DailyPaths.Resolve(["nudge", "--data", "/opt/data", "--plugin-root", "/opt/root"], env);

        Assert.AreEqual(Path.GetFullPath("/opt/data"), paths.DataDir);
        Assert.AreEqual(Path.GetFullPath("/opt/root"), paths.PluginRoot);
    }

    [TestMethod]
    public void Resolve_FallsBackToEnv()
    {
        var env = new Dictionary<string, string?> { ["CLAUDE_PLUGIN_DATA"] = "/env/data", ["CLAUDE_PLUGIN_ROOT"] = "/env/root" };

        var paths = DailyPaths.Resolve(["nudge"], env);

        Assert.AreEqual(Path.GetFullPath("/env/data"), paths.DataDir);
        Assert.AreEqual(Path.GetFullPath("/env/root"), paths.PluginRoot);
        Assert.AreEqual(Path.Combine(paths.DataDir, "config.json"), paths.ConfigFile);
        Assert.AreEqual(Path.Combine(paths.DataDir, "state.json"), paths.StateFile);
        Assert.AreEqual(Path.Combine(paths.DataDir, "bin"), paths.BinDir);
        Assert.AreEqual(Path.Combine(paths.DataDir, "references"), paths.ReferencesDir);
        Assert.AreEqual(Path.Combine(paths.DataDir, "hook-errors.log"), paths.HookErrorLog);
    }

    [TestMethod]
    public void Resolve_ThrowsWhenNeitherSet()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => DailyPaths.Resolve(["nudge"], new Dictionary<string, string?>()));
    }

    [TestMethod]
    public void StripGlobalOptions_RemovesDataAndPluginRoot()
    {
        var stripped = DailyPaths.StripGlobalOptions(["verify", "x", "--data", "/d", "--plugin-root", "/r"]);

        CollectionAssert.AreEqual(new[] { "verify", "x" }, stripped);
    }
}
