namespace Daily.Cli;

/// <summary>Chooses which language today's challenge is in: the one practised least recently.</summary>
public static class LanguagePicker
{
    /// <summary>
    /// Among the selected languages that have topics (and aren't excluded), picks the one whose latest Started/Completed
    /// entry is oldest (never practised wins); ties go to the earlier language in <paramref name="selected"/>.
    /// </summary>
    public static LanguageSetting? Choose(
        IReadOnlyList<LanguageSetting> selected,
        IReadOnlyDictionary<string, IReadOnlyList<Topic>> catalogs,
        IReadOnlyList<HistoryEntry> history,
        IReadOnlySet<string>? exclude = null)
    {
        return selected
            .Select((setting, index) => (Setting: setting, Index: index))
            .Where(x => catalogs.TryGetValue(x.Setting.Id, out var topics) && topics.Count > 0)
            .Where(x => exclude is null || !exclude.Contains(x.Setting.Id))
            .OrderBy(x => LastPractised(x.Setting.Id, history))
            .ThenBy(x => x.Index)
            .Select(x => x.Setting)
            .FirstOrDefault();
    }

    private static DateOnly LastPractised(string languageId, IReadOnlyList<HistoryEntry> history) =>
        history
            .Where(e => e.Language == languageId && e.Status is ChallengeStatus.Started or ChallengeStatus.Completed)
            .Select(e => e.Date)
            .DefaultIfEmpty(DateOnly.MinValue)
            .Max();
}
