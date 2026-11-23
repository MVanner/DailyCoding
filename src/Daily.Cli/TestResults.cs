using System.Xml;
using System.Xml.Linq;

namespace Daily.Cli;

public sealed record TestFailure(string Name, string Message);

public sealed record TestRunResult(int Passed, int Failed, int Total, TestFailure[] Failures);

/// <summary>Reads JUnit XML (Gradle, busted, swift --xunit-output). Null means no usable results, never a pass.</summary>
public static class JUnitResults
{
    public static TestRunResult? Read(IEnumerable<string> files)
    {
        var passed = 0;
        var total = 0;
        var failures = new List<TestFailure>();
        var any = false;

        foreach (var file in files)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(file);
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                return null;
            }

            foreach (var testCase in doc.Descendants("testcase"))
            {
                if (testCase.Element("skipped") is not null)
                {
                    continue;
                }

                any = true;
                total++;
                var problem = testCase.Element("failure") ?? testCase.Element("error");
                if (problem is null)
                {
                    passed++;
                    continue;
                }

                var className = (string?)testCase.Attribute("classname");
                var name = (string?)testCase.Attribute("name") ?? "";
                failures.Add(new TestFailure(string.IsNullOrEmpty(className) ? name : $"{className}.{name}", MessageOf(problem)));
            }
        }

        return any ? new TestRunResult(passed, total - passed, total, [.. failures]) : null;
    }

    private static string MessageOf(XElement problem)
    {
        var message = (string?)problem.Attribute("message");
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        return problem.Value.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault(l => l.Length > 0) ?? "";
    }
}

/// <summary>Reads Visual Studio TRX files (dotnet test). Null means no usable results.</summary>
public static class TrxResults
{
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static TestRunResult? Read(IEnumerable<string> files)
    {
        int passed = 0, failed = 0, total = 0;
        var failures = new List<TestFailure>();
        var any = false;

        foreach (var file in files)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(file);
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                return null;
            }

            var counters = doc.Descendants(Trx + "Counters").FirstOrDefault();
            if (counters is null)
            {
                return null;
            }

            int Count(string attribute) => (int?)counters.Attribute(attribute) ?? 0;
            passed += Count("passed");
            failed += Count("failed");
            total += Count("total");
            any = true;

            failures.AddRange(doc.Descendants(Trx + "UnitTestResult")
                .Where(r => (string?)r.Attribute("outcome") == "Failed")
                .Select(r => new TestFailure(
                    (string?)r.Attribute("testName") ?? "",
                    (string?)r.Element(Trx + "Output")?.Element(Trx + "ErrorInfo")?.Element(Trx + "Message") ?? "")));
        }

        return any && total > 0 ? new TestRunResult(passed, failed, total, [.. failures]) : null;
    }
}
