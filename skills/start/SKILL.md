---
name: start
description: Start today's C# coding challenge — generate a verified 10–20 minute exercise (task, stub and failing MSTest tests) in the developer's workspace from today's topic and level, or re-show it if it already exists. Use when the developer runs /daily:start or asks for today's challenge.
allowed-tools: Bash(dotnet *), Read, Write, Edit, Glob
---

# Daily: Start

The helper CLI (use exactly this, quoted, in every command below — call it `CLI`):

```
dotnet "${CLAUDE_PLUGIN_DATA}/bin/Daily.Cli.dll" --data "${CLAUDE_PLUGIN_DATA}" --plugin-root "${CLAUDE_PLUGIN_ROOT}"
```

If the dll doesn't exist or `CLI today` exits 2, tell the developer to run `/daily:setup` and stop.

## 1. Today

Run `CLI today`. It returns JSON with `date`, `entry` (status, topicId, level, stretch, folder, generationAttempts), `topic` (`id`, `name`), `seed` (`learningGoal`, `gotchas`, `goodSolutionShows`, `shapeHints`), `config` and `recent` (last 14 days).

- `entry.status` is `Skipped` → "You skipped today's challenge — see you tomorrow." Stop.
- `GenerationFailed` → say today's challenge couldn't be generated reliably and a new one comes tomorrow. Stop.
- `Started` or `Completed` → read `<folder>/README.md` and re-show the summary and the folder path (and for Completed, that it's already done). Stop. Never generate a second challenge for the same day.
- `Picked` → continue.

Use that `date` for the rest of this run: pass `--date <date>` to every `scaffold`, `record` and `verify` call below, so a run that crosses midnight stays on the same day.

## 2. Design the challenge

This is learning, not assessment. Design one concrete task that:

- Teaches the seed's `learningGoal` at `entry.level`, and makes the developer run into the seed's `gotchas`. Use a `shapeHints` scenario or a similar realistic one (employee-app domains like users, notifications, feeds, devices and tenants work well).
- Takes **10–20 minutes** for someone at that level. Prefer one or two small methods/classes over a big design. If in doubt, make it smaller.
- Isn't a repeat of anything in `recent` (same topic is fine, same exercise is not).
- Needs nothing outside the .NET base library. Tests must be deterministic: no real time, network, file system or randomness — pass clocks/delays/data in as parameters or small interfaces.

Pick a short kebab-case slug (3–40 chars, `a-z0-9-`), e.g. `async-retry-policy`.

## 3. Write it

1. `CLI record generation-attempt --date <date>`
2. `CLI scaffold <slug> --date <date>` → JSON `folder`. (On a regeneration use `CLI scaffold <slug> --date <date> --force`.)
3. In that folder write:
   - `README.md`: title; the task in plain words; the public API they must implement; constraints; "Time box: 10–20 min"; "Done when: all tests in Challenge.Tests pass"; on a stretch day one line saying it's a stretch day. No solution hints.
   - `Challenge/*.cs` — the **stub**: namespace `Challenge`, the full public signatures with XML doc comments, every body `throw new NotImplementedException();`. Supporting types the task doesn't ask them to write (records, interfaces) may be complete.
   - `Challenge.Tests/*Tests.cs` — MSTest (`[TestClass]`, `[TestMethod]`, `Assert.*`), namespace `Challenge.Tests`, **at least 3 tests, ideally 5–8**, with clear names that describe behaviour. Before going on, check each seed gotcha is exercised by at least one test; add tests until it is.
   - `.verify/reference/` — a correct, idiomatic solution: the same file names and relative paths as the stub files in `Challenge/` (only the files with code to implement).
   - `.verify/naive/` — the same files with the same public signatures, but each method returns `default`, an empty collection/string, or echoes its input. It must compile and must not contain `throw` at all.
   Every file in `.verify/reference/` and `.verify/naive/` must replace a stub file of the same name in `Challenge/`.
4. `CLI verify "<folder>" --date <date>` → JSON `ok`, `problems`, counts. It runs the tests against the shipped stub (at least 70 % must fail), the reference (all must pass, at least 3 tests) and the naive version (at least 70 % must fail). It can take a minute or two.

## 4. Fix or give up cleanly

- `ok: false` → read `problems`, fix the tests/reference/naive/stub (lenient tests → make assertions stricter, never make the naive version throw; reference failing → fix whichever of test or reference is wrong), and verify again. At most **3 verify runs** per generation.
- Still failing after 3 → if `entry.generationAttempts` (from `CLI today`) was 0 before this attempt, regenerate once: new design of the **same topic**, back to step 3 with `--force`. Otherwise run `CLI record generation-failed --date <date>`, tell the developer plainly that today's challenge couldn't be generated reliably, and stop.

## 5. Hand over

When `ok: true` the reference has been moved out of the workspace. Run `CLI record started --folder "<folder>" --date <date>`, then tell the developer, briefly:

- the topic and level (and that it's a stretch day, if `entry.stretch`),
- the task in two or three sentences,
- the folder path, and that they can open `Challenge.slnx` in Rider,
- "Run `/daily:check` when the tests pass (or when you want feedback), `/daily:hint` if you're stuck."

Never show or describe the reference or naive code.
