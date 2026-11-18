---
name: check
description: Check and review the developer's solution to today's C# challenge — run the tests, then give short, encouraging feedback on correctness, idiomatic C# and what a senior developer would change. Use when the developer runs /daily:check or says they've finished the daily challenge.
allowed-tools: Bash(dotnet *), Read, Glob
---

# Daily: Check

The helper CLI (use exactly this, quoted — call it `CLI`):

```
dotnet "${CLAUDE_PLUGIN_DATA}/bin/Daily.Cli.dll" --data "${CLAUDE_PLUGIN_DATA}" --plugin-root "${CLAUDE_PLUGIN_ROOT}"
```

1. Run `CLI today`. Check today's challenge if `entry.status` is `Started` or `Completed` (folder `entry.folder`, reference `referenceDir`). Otherwise, if `unfinished` isn't null — a challenge from an earlier day they started but didn't finish — check that one (`unfinished.entry.folder`, `unfinished.referenceDir`, and its `date`) and say which day's challenge you're checking. Otherwise say there's no challenge in progress (suggest `/daily:start`) and stop. Below, `<date>` is the date of the challenge being checked.
2. Run `dotnet test "<folder>"` and read the result.
3. **Some tests fail (or it doesn't build):** list which tests fail with a one-line reason each, pointing at the behaviour that's wrong — don't write the fix. Offer `/daily:hint`. Run `CLI record completed --passed false --date <date>`. Stop.
4. **All tests pass:** read the developer's code in `<folder>/Challenge/`, the README, the seed (`goodSolutionShows`, `gotchas` from `CLI today`; for an earlier day's challenge run `CLI today --date <date>` to get that day's seed) and the reference solution in the reference folder from step 1. Then review, in this order and briefly:
   - ✅ what they did well (one or two specific things),
   - correctness beyond the tests (edge cases the tests didn't cover, if any),
   - idiomatic C# — compare with what the seed says a good solution shows; mention a nicer construct only if it is genuinely better,
   - one or two "what a senior dev would change" points, with a short snippet where it helps.
   If the reference takes a different but not better approach, don't push it. Show reference code only if it teaches something their solution doesn't. This is learning, not grading — no scores.
5. Run `CLI record completed --passed true --date <date>`. Close with one line, e.g. "Nice — see you tomorrow."
