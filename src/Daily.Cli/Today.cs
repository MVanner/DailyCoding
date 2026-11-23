namespace Daily.Cli;

/// <summary>Today's challenge entry: shared by the hook and the `today` command.</summary>
public static class Today
{
    /// <summary>Picks a language, then a topic from that language's catalog; null when no selected language is usable.</summary>
    public static HistoryEntry? CreatePickedEntry(
        DateOnly today,
        DailyConfig config,
        IReadOnlyDictionary<string, IReadOnlyList<Topic>> catalogs,
        IReadOnlyList<HistoryEntry> history,
        IReadOnlySet<string>? exclude = null)
    {
        var language = LanguagePicker.Choose(config.Languages, catalogs, history, exclude);
        if (language is null)
        {
            return null;
        }

        var languageHistory = history.Where(e => e.Language == language.Id).ToList();
        var pick = Picker.Choose(today, language.Level, config.Salt, catalogs[language.Id], languageHistory);
        return new HistoryEntry(today, pick.TopicId, pick.Level, pick.Stretch, ChallengeStatus.Picked) { Language = language.Id };
    }

    /// <summary>A still-Picked entry is stale when its language was deselected or its topic left the catalog.</summary>
    public static bool NeedsRepick(HistoryEntry entry, DailyConfig config, IReadOnlyDictionary<string, IReadOnlyList<Topic>> catalogs) =>
        entry.Status == ChallengeStatus.Picked
        && (!config.Languages.Any(l => l.Id == entry.Language)
            || !catalogs.TryGetValue(entry.Language, out var topics)
            || !topics.Any(t => t.Id == entry.TopicId));

    /// <summary>Today's entry: created if missing, re-picked if it is Picked and no longer valid; null when no language is usable.</summary>
    public static HistoryEntry? Ensure(StateStore store, DailyConfig config, IReadOnlyDictionary<string, IReadOnlyList<Topic>> catalogs, DateOnly today)
    {
        var state = store.LoadState();
        var existing = state.For(today);
        if (existing is not null && !NeedsRepick(existing, config, catalogs))
        {
            return existing;
        }

        var entry = CreatePickedEntry(today, config, catalogs, state.History.Where(e => e.Date != today).ToList());
        if (entry is not null)
        {
            store.Upsert(entry);
        }

        return entry;
    }
}
