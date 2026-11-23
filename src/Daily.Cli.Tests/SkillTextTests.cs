namespace Daily.Cli.Tests;

[TestClass]
public sealed class SkillTextTests
{
    private static readonly string[] SkillNames = ["setup", "start", "hint", "check", "skip"];

    private static string Skill(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.PluginRoot, "skills", name, "SKILL.md"));

    [TestMethod]
    public void SkillsAreLanguageNeutral()
    {
        string[] forbidden = ["dotnet test", "MSTest", "NotImplementedException", "Challenge.slnx", "C# challenge"];
        foreach (var name in SkillNames)
        {
            var text = Skill(name);
            foreach (var phrase in forbidden)
            {
                Assert.IsFalse(text.Contains(phrase, StringComparison.Ordinal), $"skills/{name}/SKILL.md contains '{phrase}'");
            }
        }

        foreach (var name in new[] { "start", "hint", "check" })
        {
            var text = Skill(name);
            StringAssert.Contains(text, "language.guide", $"skills/{name}");
            StringAssert.Contains(text, "run /daily:setup", $"skills/{name}");
        }

        StringAssert.Contains(Skill("setup"), "CLI languages");
    }
}
