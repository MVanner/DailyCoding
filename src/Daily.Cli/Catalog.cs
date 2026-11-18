using System.Text.Json;

namespace Daily.Cli;

public enum Level
{
    Beginner,
    Intermediate,
    Advanced,
    Expert,
}

/// <summary>What a challenge for one topic at one level should teach and test.</summary>
public sealed record Seed(string LearningGoal, string[] Gotchas, string[] GoodSolutionShows, string[]? ShapeHints)
{
    public string[] ShapeHints { get; init; } = ShapeHints ?? [];
}

public sealed record Topic(string Id, string Name, Dictionary<Level, Seed> Seeds);

public sealed class CatalogException(string message) : Exception(message);

public static class CatalogLoader
{
    /// <summary>Loads every <c>*.json</c> topic under <paramref name="catalogDir"/>; the file name must equal the topic id.</summary>
    public static IReadOnlyList<Topic> Load(string catalogDir)
    {
        var topics = new List<Topic>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(catalogDir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            Topic? topic;
            try
            {
                topic = Json.Deserialize<Topic>(File.ReadAllText(file));
            }
            catch (JsonException ex)
            {
                throw new CatalogException($"{name}: invalid JSON ({ex.Message})");
            }

            if (topic is null || string.IsNullOrWhiteSpace(topic.Id) || string.IsNullOrWhiteSpace(topic.Name))
            {
                throw new CatalogException($"{name}: topic needs an id and a name");
            }

            if (topic.Id != Path.GetFileNameWithoutExtension(file))
            {
                throw new CatalogException($"{name}: id '{topic.Id}' must match the file name");
            }

            if (topic.Seeds is null || topic.Seeds.Count == 0)
            {
                throw new CatalogException($"{name}: topic '{topic.Id}' has no seeds");
            }

            if (!seen.TryAdd(topic.Id, file))
            {
                throw new CatalogException($"{name}: duplicate topic id '{topic.Id}' (also in {seen[topic.Id]})");
            }

            topics.Add(topic);
        }

        return topics;
    }
}
