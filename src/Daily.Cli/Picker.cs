namespace Daily.Cli;

public enum StretchKind
{
    NewTopic,
    LevelUp,
}

public sealed record Pick(string TopicId, Level Level, bool Stretch, StretchKind? Kind);

/// <summary>Chooses the day's topic and level. Deterministic for a given user (salt), day, catalog and history.</summary>
public static class Picker
{
    private const int StretchOneIn = 4;
    private const int RecentWindow = 5;
    private const int PoolSize = 3;

    public static Pick Choose(DateOnly today, Level level, int salt, IReadOnlyList<Topic> catalog, IReadOnlyList<HistoryEntry> history)
    {
        if (catalog.Count == 0)
        {
            throw new InvalidOperationException("The topic catalog is empty.");
        }

        var rng = new Random(unchecked(today.DayNumber * 397) ^ salt);
        var done = history
            .Where(e => e.Status is ChallengeStatus.Started or ChallengeStatus.Completed)
            .Select(e => e.TopicId)
            .ToHashSet(StringComparer.Ordinal);

        var stretch = rng.Next(StretchOneIn) == 0;
        StretchKind? kind = null;
        var target = level;

        if (stretch)
        {
            var unseen = catalog.Where(t => t.Seeds.ContainsKey(level) && !done.Contains(t.Id)).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
            if (unseen.Count > 0)
            {
                return new Pick(unseen[rng.Next(unseen.Count)].Id, level, true, StretchKind.NewTopic);
            }

            if (level < Level.Expert && catalog.Any(t => t.Seeds.ContainsKey(level + 1)))
            {
                target = level + 1;
                kind = StretchKind.LevelUp;
            }
            else
            {
                stretch = false;
            }
        }

        var effective = NearestLevelWithSeeds(target, catalog);
        var candidates = catalog.Where(t => t.Seeds.ContainsKey(effective)).ToList();

        var recent = history
            .Where(e => e.Status != ChallengeStatus.Skipped)
            .OrderByDescending(e => e.Date)
            .Take(RecentWindow)
            .Select(e => e.TopicId)
            .ToHashSet(StringComparer.Ordinal);
        var fresh = candidates.Where(t => !recent.Contains(t.Id)).ToList();
        if (fresh.Count > 0)
        {
            candidates = fresh;
        }

        var pool = candidates
            .OrderBy(t => LastShown(t.Id, history))
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .Take(PoolSize)
            .ToList();

        return new Pick(pool[rng.Next(pool.Count)].Id, effective, stretch, kind);
    }

    /// <summary>The requested level if any topic has a seed there; otherwise the nearest lower level, then the nearest higher.</summary>
    private static Level NearestLevelWithSeeds(Level level, IReadOnlyList<Topic> catalog)
    {
        bool HasSeeds(Level l) => catalog.Any(t => t.Seeds.ContainsKey(l));

        for (var l = level; l >= Level.Beginner; l--)
        {
            if (HasSeeds(l)) return l;
        }

        for (var l = level + 1; l <= Level.Expert; l++)
        {
            if (HasSeeds(l)) return l;
        }

        throw new InvalidOperationException("No topic in the catalog has any seeds.");
    }

    private static DateOnly LastShown(string topicId, IReadOnlyList<HistoryEntry> history) =>
        history.Where(e => e.TopicId == topicId && e.Status != ChallengeStatus.Skipped)
            .Select(e => e.Date)
            .DefaultIfEmpty(DateOnly.MinValue)
            .Max();
}
