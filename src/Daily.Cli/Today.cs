namespace Daily.Cli;

/// <summary>Today's challenge entry: shared by the hook and the `today` command.</summary>
public static class Today
{
    public static HistoryEntry CreatePickedEntry(DateOnly today, DailyConfig config, IReadOnlyList<Topic> catalog, IReadOnlyList<HistoryEntry> history)
    {
        var pick = Picker.Choose(today, config.Level, config.Salt, catalog, history);
        return new HistoryEntry(today, pick.TopicId, pick.Level, pick.Stretch, ChallengeStatus.Picked);
    }
}
