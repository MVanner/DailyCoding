# daily — a C# challenge a day

A Claude Code plugin that gives each developer one short C# coding challenge (10–20 minutes) a day, at the level they choose. Newer developers build fundamentals; experienced ones keep sharp and get nudged into parts of the language they don't use every day.

The first time you open Claude Code each day you see one line:

```
☕ Today's C# challenge is ready (Intermediate · async/await) — /daily:start when you have 15 min
```

Nothing else happens until you ask. Later sessions that day stay quiet.

| Command | What it does |
|---------|--------------|
| `/daily:setup` | Build the helper, choose your level and workspace folder. Re-run to change level, or after `git pull`. |
| `/daily:start` | Generate today's challenge (task, stub, failing MSTest tests) in your workspace, or re-show it. |
| `/daily:hint` | One hint, a bit stronger each time. Never the solution. |
| `/daily:check` | Run the tests and get a short review: what's good, edge cases, idiomatic C#, what a senior dev would change. |
| `/daily:skip` | No challenge today. The topic comes back another day. |

## Install

Needs the .NET 10 SDK or newer and a clone of this repo.

macOS:

```bash
./install.sh
```

Windows (PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

(`-ExecutionPolicy Bypass` because Windows blocks unsigned scripts by default; it applies to this one run only.)

The script adds this repo as the `daily-coding` plugin marketplace, installs `daily` for your user, and builds the helper so the nudge works straight away. If the `claude` CLI isn't on your PATH (desktop app only), it prints the two `/plugin` commands to type in the app instead, and still builds the helper. Then run `/daily:setup` in Claude Code.

> **Installed through `/plugin` without the script, or reinstalled?** The nudge runs a helper that only exists after the install script or `/daily:setup` has built it. Until then Claude Code shows a `SessionStart hook error` at startup. Run `/daily:setup` once and it goes away. (The hook deliberately runs a pre-built helper rather than a C# script: a script needs the .NET SDK, and would fail in every repo whose `global.json` pins an SDK you don't have.)

The marketplace points at your clone, so the plugin is loaded in place: `git pull` picks up changes. When the helper's code changed, the nudge says so; run `/daily:setup` to rebuild.

## How it works

- **Levels:** Beginner, Intermediate, Advanced, Expert. About one day in four is a *stretch day*: a topic you haven't done yet at your level, or one level up. Your level never changes by itself.
- **Topics** come from `catalog/` — one JSON file per topic with a *seed* per level: what to learn, the gotchas to run into, and what a good solution shows. Claude turns the seed into a concrete challenge.
- **Quality gate:** before you see a challenge, the helper runs the tests against the stub you'll get (at least 70 % must fail), against Claude's hidden reference solution (all must pass, at least 3 tests) and against a naive implementation that returns empty/default values without throwing (at least 70 % must fail, so the tests aren't too lenient). If that can't be met, you get one regeneration of the same topic, then no challenge that day rather than a broken one.
- **One challenge per day.** `/daily:skip` ends the day.

## What's stored where

Everything is local to your machine; nothing is shared with the team.

- `~/.claude/plugins/data/daily-daily-coding/` (Windows: `%USERPROFILE%\.claude\plugins\data\daily-daily-coding\`): `config.json` (level, workspace), `state.json` (your history), `references/<date>/` (hidden reference solutions, used by `/daily:check`), `bin/` (the built helper), `hook-errors.log` (if the nudge ever failed silently).
- Challenge folders: your workspace, `~/DailyCoding` by default, one folder per day (`2026-11-16-async-retry-policy/`).

Uninstalling the plugin deletes the data folder (and your history); challenge folders stay.

## Adding or tuning topics

Add `catalog/<topic-id>.json` (the file name must equal `id`) and open a PR:

```json
{
  "id": "async-await",
  "name": "async/await",
  "seeds": {
    "Intermediate": {
      "learningGoal": "Run independent async work concurrently and support cancellation.",
      "gotchas": ["Awaiting in a loop runs work sequentially", "A CancellationToken that is accepted but never passed down"],
      "goodSolutionShows": ["Task.WhenAll for independent work", "CancellationToken passed through to every async call"],
      "shapeHints": ["fetching several feeds at once with a timeout"]
    }
  }
}
```

Each topic needs at least two levels; every seed needs a `learningGoal`, at least one gotcha and one `goodSolutionShows`. `dotnet test src/Daily.Cli.Tests` checks the whole catalog.

## Developing the plugin

- Open `DailyCoding.slnx` in Rider. Helper: `src/Daily.Cli` (no third-party dependencies). Tests: `dotnet test` (the `Integration` category builds real projects and takes about a minute; `--filter TestCategory!=Integration` for the fast ones).
- Hook: `hooks/hooks.json` runs `dotnet <data>/bin/Daily.Cli.dll nudge` in exec form (no shell), so it behaves the same on macOS and Windows. It never prints errors or exits non-zero.
- Skills: `skills/*/SKILL.md`.

## Platform notes and first-run checklist

Verified on macOS with the Claude Code CLI: install script, hook through Claude Code (one line on the first session, silent afterwards, ~80 ms), and the full start → verify → check flow. Still to verify by hand:

- **Desktop app (macOS and Windows):** the hook runs `dotnet` from the app's own `PATH`. If you see a `SessionStart hook error` at startup instead of the nudge, the app can't find `dotnet`. On macOS the official .NET installer puts it in `/usr/local/share/dotnet` (on the GUI `PATH` via `/etc/paths.d/dotnet`); an SDK installed only under `~/.dotnet` isn't visible to apps launched from the Dock — install the SDK with the official installer, or link it: `sudo ln -s ~/.dotnet/dotnet /usr/local/bin/dotnet`. On Windows the .NET installer adds `C:\Program Files\dotnet\` to the system `PATH`; restart the app after installing.
- **Windows:** run `install.ps1`, then check: the nudge appears once on the first start of the day; `/daily:start` creates a folder under `%USERPROFILE%\DailyCoding` (including a user name with a space); `/daily:check` runs the tests; no hook error before `/daily:setup` has run.
- **After `git pull`:** if the helper's code changed, the nudge ends with "plugin updated — run /daily:setup to rebuild".
- If the nudge never shows, look in `hook-errors.log` in the data folder.
