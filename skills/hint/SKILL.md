---
name: hint
description: Give one progressively stronger hint for today's daily challenge without giving away the solution. Use when the developer runs /daily:hint or says they're stuck on the daily challenge.
allowed-tools: Bash(dotnet *), Read, Glob
---

# Daily: Hint

The helper CLI (use exactly this, quoted — call it `CLI`):

```
dotnet "${CLAUDE_PLUGIN_DATA}/bin/Daily.Cli.dll" --data "${CLAUDE_PLUGIN_DATA}" --plugin-root "${CLAUDE_PLUGIN_ROOT}"
```

1. Run `CLI today`. If the output has no `language` field (the helper is from an older plugin version), tell the developer to run /daily:setup and stop. Work on today's challenge if `entry.status` is `Started` or `Completed`. Otherwise, if `unfinished` isn't null (an earlier day's challenge they started but didn't finish), work on that one: use `unfinished.entry` and its `date`. Otherwise say there's no challenge in progress (suggest `/daily:start`) and stop.
   The challenge's language is the folder's: for today's challenge it is `language` from `CLI today`; for an earlier day's `unfinished` challenge run `CLI today --date <its date>` and use that output's `language`. If `language.available` is false, tell the developer what's missing (`language.missing`) and stop.
2. Run `CLI hint --date <that challenge's date>` → `hints` = how many hints they've now had on it.
3. Read `<folder>/README.md`, the test files in `<folder>/<language.testDir>/` and the developer's current code in `<folder>/<language.stubDir>/`. Read the `## Hint examples` section of the guide at `language.guide`. Don't read the reference solution.
4. Give **one** hint, sized by `hints`:
   - 1 → a direction: what to think about, which test to start with, which gotcha they're about to hit.
   - 2 → name the concept or API that unlocks it, in the style of the guide's `## Hint examples`.
   - 3 or more → outline the approach in steps or show a tiny generic snippet of the technique — never their method's actual solution.
   Base it on where their code is now (e.g. which tests fail and why). Keep it to a few lines and encouraging.
