---
name: setup
description: Set up the daily challenge for this developer — build the helper tool, choose languages, a difficulty level per language and a workspace folder. Use on first install, to change languages, levels or workspace, or when the daily nudge says "run /daily:setup" (including "plugin updated — run /daily:setup to rebuild").
allowed-tools: Bash(dotnet *), AskUserQuestion
---

# Daily: Setup

Get this machine ready for `/daily:start`. Safe to re-run: it rebuilds the helper and lets the developer change languages, levels or workspace.

The helper CLI (use exactly this, quoted, in every command below — call it `CLI`):

```
dotnet "${CLAUDE_PLUGIN_DATA}/bin/Daily.Cli.dll" --data "${CLAUDE_PLUGIN_DATA}" --plugin-root "${CLAUDE_PLUGIN_ROOT}"
```

## 1. .NET SDK

The helper itself runs on .NET, whichever languages the developer picks. Run `dotnet --version`. If `dotnet` isn't found or the major version is below 10, tell the developer the plugin needs the .NET 10 SDK or newer (https://dotnet.microsoft.com/download) and stop. Remember the major version (e.g. `11.0.100` → `11`); it only matters if they pick C#.

## 2. Build the helper

```
dotnet build "${CLAUDE_PLUGIN_ROOT}/src/Daily.Cli/Daily.Cli.csproj" -c Release -o "${CLAUDE_PLUGIN_DATA}/bin" -v q
```

Then run `CLI stamp-write`. If the build fails, show the first compiler error and stop.

## 3. Current settings

Run `CLI config show`. Exit code 2 means first-time setup. Otherwise show the current languages with their levels and the workspace and ask whether they want to change any of them; if not, skip to step 8 (the `sdkMajor` refresh, which still applies if `csharp` is configured) and finish with step 9.

## 4. Languages

Run `CLI languages` → a list of `{ id, name, available, missing, levelHints }` (`missing` holds install hints for tools that aren't installed).

Ask with AskUserQuestion, `multiSelect`, "Which languages do you want daily challenges in?": one option per language (at most 4 per question; if there are more, ask in several questions). Each option's description says it's ready, or "not installed yet" followed by its `missing` hints. Mark the currently configured languages when re-running setup. One day's challenge uses one of the selected languages, picked by rotation.

Only languages with `available: true` can be saved. If the developer picked some that aren't available, say which are left out and show their install hints (they can install them and re-run `/daily:setup`). If none of the picks is available, say so and ask again rather than saving an empty list; if nothing at all is available, show the install hints and stop.

## 5. Levels

One AskUserQuestion call with one question per language that will be saved (at most 4 questions per call): "Which level should most <Language> challenges be at?" with the options Beginner, Intermediate, Advanced, Expert, in this order, each described by that language's `levelHints` entry for the level, and the current level marked when re-running setup.

Mention once: about one day in four is a stretch day (a level up, or a topic they haven't done yet). They can change levels any time by re-running `/daily:setup`.

## 6. Warm-up, then save languages and levels

For each picked language other than `csharp` that is `available`, run `CLI warmup <id>` before saving anything. It copies that language's template, adds a tiny known-good sample, runs the tests once (so the first real challenge isn't slow: Kotlin downloads Gradle and its dependencies, Swift builds its package) and prints `{ ok, seconds, problem, output }`. Tell the developer first that it may take a few minutes per language. `ok` is true only when the sample built and its tests passed.

If `ok` is false, the toolchain is installed but doesn't work. Leave that language out, and tell the developer: `problem`, a short excerpt from the tail of `output`, and the language's install hint from `CLI languages`. Keep it anyway only if the developer explicitly says to. They can fix the toolchain and re-run `/daily:setup`. If every pick fails its warm-up, say so and go back to step 4 rather than saving an empty list.

Then run `CLI config set languages <id,id,...>` with the languages that remain, and `CLI config set level <id> <Level>` for each of them.

## 7. Workspace

Ask where challenge folders should go. Offer the default — the `DailyCoding` folder in their home directory, shown as a full absolute path for this OS — or another path via "Other". Run `CLI config set workspace "<absolute path>"`.

## 8. SDK version (C# only)

If `csharp` was saved (or, when nothing was changed in step 3, is already configured): run `CLI config set sdkMajor <major from step 1>` (C# projects target `net<major>.0`). Otherwise skip this step.

## 9. Done

Confirm in two or three lines: languages with their levels, workspace, and that from tomorrow the first Claude Code session of the day shows a one-line nudge. They can run `/daily:start` right now if they have 15 minutes.
