---
name: setup
description: Set up the daily C# challenge for this developer — build the helper tool, choose a difficulty level and a workspace folder. Use on first install, to change level or workspace, or when the daily nudge says "run /daily:setup" (including "plugin updated — run /daily:setup to rebuild").
allowed-tools: Bash(dotnet *), AskUserQuestion
---

# Daily: Setup

Get this machine ready for `/daily:start`. Safe to re-run: it rebuilds the helper and lets the developer change level or workspace.

The helper CLI (use exactly this, quoted, in every command below — call it `CLI`):

```
dotnet "${CLAUDE_PLUGIN_DATA}/bin/Daily.Cli.dll" --data "${CLAUDE_PLUGIN_DATA}" --plugin-root "${CLAUDE_PLUGIN_ROOT}"
```

## 1. .NET SDK

Run `dotnet --version`. If `dotnet` isn't found or the major version is below 10, tell the developer the plugin needs the .NET 10 SDK or newer (https://dotnet.microsoft.com/download) and stop. Remember the major version (e.g. `11.0.100` → `11`).

## 2. Build the helper

```
dotnet build "${CLAUDE_PLUGIN_ROOT}/src/Daily.Cli/Daily.Cli.csproj" -c Release -o "${CLAUDE_PLUGIN_DATA}/bin" -v q
```

Then run `CLI stamp-write`. If the build fails, show the first compiler error and stop.

## 3. Current settings

Run `CLI config show`. Exit code 2 means first-time setup. Otherwise show the current level and workspace and ask whether they want to change either; if not, skip to step 6.

## 4. Level

Ask with AskUserQuestion ("Which level should most daily challenges be at?"), options in this order, current level marked if there is one:

- **Beginner** — new to C# or to professional development: core syntax, collections, LINQ basics, async basics.
- **Intermediate** — comfortable day to day: sharper idioms, concurrency with async, generics, pattern matching.
- **Advanced** — design trade-offs, performance, concurrency control, API design.
- **Expert** — deep language and runtime topics: `Span<T>`, `ValueTask`, source generators, allocation-free code.

Mention once: about one day in four is a stretch day (a level up, or a topic they haven't done yet). They can change level any time by re-running `/daily:setup`.

Run `CLI config set level <Level>`.

## 5. Workspace

Ask where challenge folders should go. Offer the default — the `DailyCoding` folder in their home directory, shown as a full absolute path for this OS — or another path via "Other". Run `CLI config set workspace "<absolute path>"`.

## 6. SDK version

Run `CLI config set sdkMajor <major from step 1>` (challenges target `net<major>.0`).

## 7. Done

Confirm in two or three lines: level, workspace, and that from tomorrow the first Claude Code session of the day shows a one-line nudge. They can run `/daily:start` right now if they have 15 minutes.
