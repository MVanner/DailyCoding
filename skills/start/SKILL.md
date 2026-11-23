---
name: start
description: Start today's coding challenge — generate a verified 10–20 minute exercise (task, stub and failing tests) in the day's language in the developer's workspace from today's topic and level, or re-show it if it already exists. Use when the developer runs /daily:start or asks for today's challenge.
allowed-tools: Bash(dotnet *), Read, Write, Edit, Glob
---

# Daily: Start

The helper CLI (use exactly this, quoted, in every command below — call it `CLI`):

```
dotnet "${CLAUDE_PLUGIN_DATA}/bin/Daily.Cli.dll" --data "${CLAUDE_PLUGIN_DATA}" --plugin-root "${CLAUDE_PLUGIN_ROOT}"
```

If the dll doesn't exist or `CLI today` exits 2, tell the developer to run `/daily:setup` and stop.

## 1. Today

Run `CLI today`. It returns JSON with `date`, `entry` (status, language, topicId, level, stretch, folder, generationAttempts), `topic` (`id`, `name`), `seed` (`learningGoal`, `gotchas`, `goodSolutionShows`, `shapeHints`), `language` (`id`, `name`, `stubDir`, `testDir`, `ide`, `guide`, `available`, `missing`), `config` and `recent` (last 14 days). If it exits 1 with "No usable language", tell the developer to run `/daily:setup` and stop.

If the output has no `language` field (the helper is from an older plugin version), tell the developer to run /daily:setup and stop.

- `entry.status` is `Skipped` → "You skipped today's challenge — see you tomorrow." Stop.
- `GenerationFailed` → say today's challenge couldn't be generated reliably and a new one comes tomorrow. Stop.
- `Started` or `Completed` → read `<folder>/README.md` and re-show the summary and the folder path (and for Completed, that it's already done). Stop. Never generate a second challenge for the same day.
- `Picked` → continue.

If `language.available` is false (the tools for today's language aren't installed), tell the developer what's missing (`language.missing` are the install hints) and that you'll pick another language for today, run `CLI repick --exclude <language.id> --date <date>` (stop and say so if it fails), then run `CLI today --date <date>` again and continue with that new entry and its `language`.

Use that `date` for the rest of this run: pass `--date <date>` to every `scaffold`, `record`, `repick`, `verify` and `today` call below, so a run that crosses midnight stays on the same day.

## 2. Design the challenge

This is learning, not assessment. Design one concrete task that:

- Teaches the seed's `learningGoal` at `entry.level`, and makes the developer run into the seed's `gotchas`. Use a `shapeHints` scenario or a similar realistic one (employee-app domains like users, notifications, feeds, devices and tenants work well).
- Takes **10–20 minutes** for someone at that level. Prefer one or two small methods/classes over a big design. If in doubt, make it smaller.
- Isn't a repeat of anything in `recent` (same topic is fine, same exercise is not).
- Follows the guide's `## Allowed libraries` section. Tests must be deterministic: no real time, network, file system or randomness — pass clocks/delays/data in as parameters or small interfaces.

Read the language guide at `language.guide` now; it holds the language-specific rules referred to below.

Pick a short kebab-case slug (3–40 chars, `a-z0-9-`), e.g. `async-retry-policy`.

## 3. Write it

1. `CLI record generation-attempt --date <date>`
2. `CLI scaffold <slug> --date <date>` → JSON `folder`. (On a regeneration use `CLI scaffold <slug> --date <date> --force`.)
3. In that folder write:
   - `README.md`: title; the task in plain words; the public API they must implement; constraints; "Time box: 10–20 min"; "Done when: all the tests pass" (plus how to run them, from the guide's `## Layout` section); on a stretch day one line saying it's a stretch day. No solution hints.
   - the **stub** in `<language.stubDir>/` — follow the guide's `## Stub` section (and `## Layout` for file names and locations). Supporting types the task doesn't ask them to write may be complete.
   - the **tests** in `<language.testDir>/` — follow the guide's `## Tests` section: **at least 3 tests, ideally 5–8**, with clear names that describe behaviour. Before going on, check each seed gotcha is exercised by at least one test; add tests until it is.
   - `.verify/reference/` — a correct, idiomatic solution, following the guide's `## Reference and naive` section: the same file names and relative paths as the stub files in `<language.stubDir>/` (only the files with code to implement).
   - `.verify/naive/` — the same files with the same public API, built as the guide's `## Reference and naive` section describes. It must build and must not contain anything that section forbids.
   Every file in `.verify/reference/` and `.verify/naive/` must replace a stub file of the same name and relative path under `<language.stubDir>/`.
4. `CLI verify "<folder>" --date <date>` → JSON `ok`, `problems`, counts. It runs the tests against the shipped stub (at least 70 % must fail), the reference (all must pass, at least 3 tests) and the naive version (at least 70 % must fail). It can take a minute or two.

## 4. Fix or give up cleanly

- `ok: false` → read `problems`, fix the tests/reference/naive/stub (lenient tests → make assertions stricter, never make the naive version break the guide's naive rules; reference failing → fix whichever of test or reference is wrong), and verify again. At most **3 verify runs** per generation.
- Still failing after 3 → if `entry.generationAttempts` (from `CLI today`) was 0 before this attempt, regenerate once: new design of the **same topic**, back to step 3 with `--force`. Otherwise run `CLI record generation-failed --date <date>`, tell the developer plainly that today's challenge couldn't be generated reliably, and stop.

## 5. Hand over

When `ok: true` the reference has been moved out of the workspace. Run `CLI record started --folder "<folder>" --date <date>`, then tell the developer, briefly:

- the topic and level (and that it's a stretch day, if `entry.stretch`),
- the task in two or three sentences,
- the folder path, and how to open it (`language.ide` holds the sentence),
- "Run `/daily:check` when the tests pass (or when you want feedback), `/daily:hint` if you're stuck."

Never show or describe the reference or naive code.
