using System.Text.Json;

namespace Daily.Cli;

/// <param name="Salt">Random per-user number so developers don't all get the same pick on the same day.</param>
public sealed record DailyConfig(Level Level, string Workspace, int SdkMajor, int Salt);

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
    public DailyConfig? LoadConfig() => Read<DailyConfig>(paths.ConfigFile);

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

    private static T? Read<T>(string file) where T : class
    {
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return Json.Deserialize<T>(File.ReadAllText(file));
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
