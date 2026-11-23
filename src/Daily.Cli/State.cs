using System.Text.Json;
using System.Text.Json.Nodes;

namespace Daily.Cli;

public sealed record LanguageSetting(string Id, Level Level);

/// <param name="Salt">Random per-user number so developers don't all get the same pick on the same day.</param>
public sealed record DailyConfig(List<LanguageSetting> Languages, string Workspace, int SdkMajor, int Salt)
{
    public Level LevelFor(string id) =>
        Languages.FirstOrDefault(l => l.Id == id)?.Level
        ?? throw new InvalidOperationException($"Language '{id}' is not selected.");

    // List equality is by reference by default; compare the languages by value.
    public bool Equals(DailyConfig? other) =>
        other is not null && Languages.SequenceEqual(other.Languages) && Workspace == other.Workspace && SdkMajor == other.SdkMajor && Salt == other.Salt;

    public override int GetHashCode() => HashCode.Combine(Languages.Count, Workspace, SdkMajor, Salt);
}

public enum ChallengeStatus
{
    Picked,
    Started,
    Completed,
    Skipped,
    GenerationFailed,
}

/// <summary>One day's challenge. There is at most one entry per date.</summary>
public sealed record HistoryEntry(DateOnly Date, string TopicId, Level Level, bool Stretch, ChallengeStatus Status)
{
    public string Language { get; init; } = "csharp";
    public bool? Passed { get; init; }
    public int Hints { get; init; }
    public int GenerationAttempts { get; init; }
    public string? Folder { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record DailyState(DateOnly? LastNudgeDate, List<HistoryEntry> History)
{
    public static DailyState Empty => new(null, []);

    public HistoryEntry? For(DateOnly date) => History.FirstOrDefault(e => e.Date == date);
}

/// <summary>Reads and writes config.json and state.json. Writes are atomic; unreadable files are set aside as *.bad.</summary>
public sealed class StateStore(DailyPaths paths)
{
    public DailyConfig? LoadConfig() => Read<DailyConfig>(paths.ConfigFile, MigrateConfig);

    public void SaveConfig(DailyConfig config) => Write(paths.ConfigFile, config);

    public DailyState LoadState()
    {
        var state = Read<DailyState>(paths.StateFile);
        return state is null ? DailyState.Empty : state with { History = state.History ?? [] };
    }

    public void SaveState(DailyState state) => Write(paths.StateFile, state);

    public void Upsert(HistoryEntry entry)
    {
        var state = LoadState();
        var history = state.History.Where(e => e.Date != entry.Date).Append(entry).OrderBy(e => e.Date).ToList();
        SaveState(state with { History = history });
    }

    /// <summary>Old configs had a single top-level <c>level</c>; it becomes the C# entry of <c>languages</c>.</summary>
    private static string MigrateConfig(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root || root.ContainsKey("languages"))
        {
            return json;
        }

        if (root["level"] is JsonValue value && value.TryGetValue<string>(out var text)
            && Enum.TryParse<Level>(text, ignoreCase: true, out var level) && Enum.IsDefined(level))
        {
            root.Remove("level");
            root["languages"] = new JsonArray(new JsonObject { ["id"] = "csharp", ["level"] = level.ToString() });
            return root.ToJsonString();
        }

        return json;
    }

    private static T? Read<T>(string file, Func<string, string>? migrate = null) where T : class
    {
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(file);
            return Json.Deserialize<T>(migrate is null ? text : migrate(text));
        }
        catch (JsonException)
        {
            File.Move(file, file + ".bad", overwrite: true);
            return null;
        }
    }

    private static void Write<T>(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, Json.Serialize(value));
        File.Move(temp, file, overwrite: true);
    }
}
