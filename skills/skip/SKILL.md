---
name: skip
description: Skip today's daily challenge — ends today with no challenge; the topic can come up again later. Use when the developer runs /daily:skip or says they don't have time for the daily challenge today.
allowed-tools: Bash(dotnet *)
---

# Daily: Skip

Run:

```
dotnet "${CLAUDE_PLUGIN_DATA}/bin/Daily.Cli.dll" --data "${CLAUDE_PLUGIN_DATA}" --plugin-root "${CLAUDE_PLUGIN_ROOT}" record skipped
```

- Exit 0 → one friendly line: skipped, the topic goes back in the pool, see you tomorrow.
- Exit 1 because today is already `Completed` → say today's challenge is already done, nothing to skip.
- Exit 2 → suggest `/daily:setup`.
