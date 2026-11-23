# daily — a coding challenge a day

A Claude Code plugin that gives each developer one short coding challenge (10–20 minutes) a day, in C#, Swift, Kotlin or Lua, at the level they choose for each language. Newer developers build fundamentals; experienced ones keep sharp and get nudged into parts of the language they don't use every day.

The first time you open Claude Code each day you see one line:

```
☕ Today's C# challenge is ready (Intermediate · async/await) — /daily:start when you have 15 min
```

The line names the day's language (C# here; other days it says Swift, Kotlin or Lua). Nothing else happens until you ask. Later sessions that day stay quiet.

| Command | What it does |
|---------|--------------|
| `/daily:setup` | Build the helper, choose your languages, a level per language and your workspace folder. Re-run to change them, or after `git pull`. |
| `/daily:start` | Generate today's challenge (task, stub, failing tests) in your workspace, or re-show it. |
| `/daily:hint` | One hint, a bit stronger each time. Never the solution. |
| `/daily:check` | Run the tests and get a short review: what's good, edge cases, idiomatic code for the language, what a senior dev would change. |
| `/daily:skip` | No challenge today. The topic comes back another day. |

## Install

Needs a clone of this repo and the .NET 10 SDK or newer (everyone: the helper and the nudge run on it, whichever languages you pick). Each language also needs its own toolchain:

| Language | Needs |
|----------|-------|
| C# | Nothing beyond the .NET 10 SDK. |
| Swift | Swift 6 toolchain (Xcode 16 or newer). macOS; Linux is untested. |
| Kotlin | JDK 17 or newer. The Gradle wrapper is included (Gradle 9.9.2, Kotlin 2.4.21, kotlinx-coroutines 1.11.0); the first Kotlin run downloads Gradle and the dependencies. |
| Lua | LuaJIT, or Lua 5.1 or newer, or Neovim 0.9 or newer (`nvim -l`). Challenges use Lua 5.1 semantics. |

`/daily:setup` checks which toolchains it finds and leaves out languages whose tools are missing, with the install hint.

Then install. macOS:

```bash
./install.sh
```

Windows (PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

(`-ExecutionPolicy Bypass` because Windows blocks unsigned scripts by default; it applies to this one run only.)

The script adds this repo as the `daily-coding` plugin marketplace, installs `daily` for your user, and builds the helper so the nudge works straight away. If the `claude` CLI isn't on your PATH (desktop app only), it prints the two `/plugin` commands to type in the app instead, and still builds the helper. Then run `/daily:setup` in Claude Code. It asks which languages you want, a level for each, and where challenge folders go, then warms up each selected non-C# toolchain (a first test run of a tiny known-good sample, so your first challenge isn't slow and a broken toolchain is caught now; a language whose warm-up fails is left out).

> **Installed through `/plugin` without the script, or reinstalled?** The nudge runs a helper that only exists after the install script or `/daily:setup` has built it. Until then Claude Code shows a `SessionStart hook error` at startup. Run `/daily:setup` once and it goes away. (The hook deliberately runs a pre-built helper rather than a C# script: a script needs the .NET SDK, and would fail in every repo whose `global.json` pins an SDK you don't have.)

The marketplace points at your clone, so the plugin is loaded in place: `git pull` picks up changes. When the helper's code changed, the nudge says "plugin updated — run /daily:setup to rebuild"; run `/daily:setup`.

**Upgrading from the C#-only version?** After `git pull`, run `/daily:setup` once. Your existing settings carry over as C# at your current level, and your history is kept. Setup then lets you add more languages.

## How it works

- **Languages:** you pick one or more. Each day uses one of them, rotating to the language you've practised least recently.
- **Levels:** Beginner, Intermediate, Advanced, Expert. About one day in four is a *stretch day*: a topic you haven't done yet at your level, or one level up. You set a level per language, and it never changes by itself.
- **Topics** come from each language's catalog (`catalog/` for C#, `languages/<id>/catalog/` for the others) — one JSON file per topic with a *seed* per level: what to learn, the gotchas to run into, and what a good solution shows. Claude turns the seed into a concrete challenge.
- **Quality gate:** before you see a challenge, the helper runs the tests against the stub you'll get (at least 70 % must fail), against Claude's hidden reference solution (all must pass, at least 3 tests) and against a naive implementation that returns empty/default values without throwing (at least 70 % must fail, so the tests aren't too lenient). If that can't be met, you get one regeneration of the same topic, then no challenge that day rather than a broken one.
- **One challenge per day.** `/daily:skip` ends the day.

## What's stored where

Everything is local to your machine; nothing is shared with the team.

- `~/.claude/plugins/data/daily-daily-coding/` (Windows: `%USERPROFILE%\.claude\plugins\data\daily-daily-coding\`): `config.json` (languages with levels, workspace), `state.json` (your history), `references/<date>/` (hidden reference solutions, used by `/daily:check`), `bin/` (the built helper), `hook-errors.log` (if the nudge ever failed silently).
- Challenge folders: your workspace, `~/DailyCoding` by default, one folder per day, named `<date>-<language>-<slug>` (`2026-11-16-csharp-async-retry-policy/`).

Uninstalling the plugin deletes the data folder (and your history); challenge folders stay.

## Adding or tuning topics

Add the topic file and open a PR: `catalog/<topic-id>.json` for C#, `languages/<id>/catalog/<topic-id>.json` for the other languages. The file name must equal `id`; the schema is the same everywhere:

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

Each topic needs at least two levels; every seed needs a `learningGoal`, at least one gotcha and one `goodSolutionShows`. `dotnet test src/Daily.Cli.Tests` checks every language's catalog.

## Adding a language

Each language lives in `languages/<id>/` (C# keeps its `catalog/` and `templates/challenge/` at the top level and points to them from its descriptor). You need five things:

1. **`language.json`**, the descriptor. Fields (see the existing ones): `id`, `name`; `stubDir` and `testDir` (where the stub and the tests live in a challenge); `sourceExtensions` and `ignoreDirs`; `test` (`commands` to run, optionally per `os`; `results` glob; `format` `trx` or `junit`; optional `env`); `naiveForbidden` (regex the naive implementation must not match, e.g. `throw`); `requires` (toolchain checks with `command`, `args`, `minVersion`, `installHint` and an optional `versionPattern`, a regex with one capture group that picks the version out of the command's output; without it the first number is used); `ide`, `runnerHint`, `levelHints` (one line per level, shown by setup). `catalogDir` and `templateDir` default to `catalog/` and `template/` inside the language folder.
2. **`template/`**, a bare project that builds and runs its tests with the `test` command.
3. **`guide.md`** with these headings: `## Layout`, `## Stub`, `## Tests`, `## Reference and naive`, `## Allowed libraries`, `## Review: idiomatic <Language>`, `## Hint examples`. The skills read the guide by heading.
4. **`catalog/`** with topic files (see above).
5. **`warmup/`** (every language except C#): a tiny known-good sample laid out like a challenge, with one source file under `stubDir` and one passing test under `testDir`. `/daily:setup` runs it on top of the template to prove the toolchain works.

`LanguageTests.EveryLanguage_HasTemplateGuideAndCatalog`, `LanguageTests.RealLanguages_NonCsharpHaveWarmupSampleMappingIntoStubAndTestDirs` and `CatalogTests.RealCatalogs_AreValid` check the template, guide, warm-up sample and catalog.

## Developing the plugin

- Open `DailyCoding.slnx` in Rider. Helper: `src/Daily.Cli` (no third-party dependencies). Tests: `dotnet test` (the `Integration` category builds real projects in every installed language and takes a few minutes; `--filter TestCategory!=Integration` for the fast ones).
- Hook: `hooks/hooks.json` runs `dotnet <data>/bin/Daily.Cli.dll nudge` in exec form (no shell), so it behaves the same on macOS and Windows. It never prints errors or exits non-zero.
- Skills: `skills/*/SKILL.md`.
- Swift runs its tests with `swift test --parallel --xunit-output .build/results.xml`; on Swift 6.4.2 the non-parallel form wrote no XCTest XML.

## Platform notes and first-run checklist

Verified on macOS with the Claude Code CLI: install script, hook through Claude Code (one line on the first session, silent afterwards, ~80 ms), and the full start → verify → check flow. Still to verify by hand:

- **Desktop app (macOS and Windows):** the hook runs `dotnet` from the app's own `PATH`. If you see a `SessionStart hook error` at startup instead of the nudge, the app can't find `dotnet`. On macOS the official .NET installer puts it in `/usr/local/share/dotnet` (on the GUI `PATH` via `/etc/paths.d/dotnet`); an SDK installed only under `~/.dotnet` isn't visible to apps launched from the Dock — install the SDK with the official installer, or link it: `sudo ln -s ~/.dotnet/dotnet /usr/local/bin/dotnet`. On Windows the .NET installer adds `C:\Program Files\dotnet\` to the system `PATH`; restart the app after installing.
- **Windows:** run `install.ps1`, then check: the nudge appears once on the first start of the day; `/daily:start` creates a folder under `%USERPROFILE%\DailyCoding` (including a user name with a space); `/daily:check` runs the tests; no hook error before `/daily:setup` has run.
- **Windows, non-C# languages:** Kotlin and Lua should work but are untested; Swift is not supported on Windows.
- **After `git pull`:** if the helper's code changed, the nudge ends with "plugin updated — run /daily:setup to rebuild".
- If the nudge never shows, look in `hook-errors.log` in the data folder.
