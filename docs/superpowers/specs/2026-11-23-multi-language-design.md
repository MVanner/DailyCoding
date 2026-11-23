# Multi-language support: Swift, Kotlin, Lua

Status: draft for approval · 2026-11-23

## Why

The plugin gives each developer one short, verified C# challenge a day. Two new audiences want the same thing in other languages:

- **Swift and Kotlin** — the company's mobile developers (iOS and Android).
- **Lua** — the plugin's author, who uses Lua for Neovim configuration/plugins and wants to get better at game development.

C# stays exactly as good as it is today. Developers choose which languages they want to practise during `/daily:setup`.

## Goals

- `/daily:setup` lets a developer pick any subset of C#, Swift, Kotlin, Lua, with a **level per language**.
- Still **one challenge a day, one nudge a day**. When several languages are selected, the day's language is the one practised least recently.
- Every language goes through the same quality gate as C# today (stub mostly fails, reference passes all ≥ 3 tests, naive version mostly fails) before the developer sees a challenge.
- Adding a fifth language later = a language descriptor file + a template + catalog topics + a guide, via PR, with no C# changes (unless the language needs a new results format).
- Existing C# users keep their history and settings; after `git pull` + `/daily:setup` they continue as before with C# selected.

## Non-goals

- UI frameworks or platform SDKs: no SwiftUI/UIKit, no Android SDK/Compose, no LÖVE runtime, no running inside Neovim. Challenges exercise the language and its standard library (plus a few named libraries below) through headless tests.
- Rewriting the helper off .NET. The helper and the SessionStart hook keep needing the .NET SDK, for every user (accepted constraint, see Constraints).
- Several challenges per day, or a separate challenge per language per day.
- Automatic level changes (unchanged from today).
- Luau/Roblox, Lua 5.4-only features.

## Anti-goals (would be a failure even if it works)

- Generating a challenge becomes so slow (more than ~5 minutes end to end on a warm machine) that developers stop using it.
- A broken challenge reaches the developer because a language's results weren't read correctly (e.g. a crashed test run counted as "all passed").
- Existing C# history or config is lost or reset by the upgrade.
- The C# experience gets worse (vaguer skill instructions, slower verify).

## Constraints

- Helper is a .NET 10 console app run as `dotnet Daily.Cli.dll`; the SessionStart hook runs it. Swift/Kotlin/Lua users therefore need the .NET 10 SDK too. Setup still checks for it first.
- Windows is supported for C# today (`install.ps1`). New languages must not break Windows for C#-only users. Swift on Windows is not a target (it may work if a toolchain is present; untested).
- Tests must be deterministic and headless (same rule as today).
- Verify runs the test suite three times (stub, reference, naive), each in a fresh temp copy, with a 5-minute timeout per run.
- Claude writes the challenge from a skill prompt; everything language-specific the skills need must reach them through the helper's JSON output or a file the helper points at.

## Approaches considered

- **A. Language descriptor files + one shared test runner (chosen).** Each language has a `language.json` (test command, results location and format, stub folder, forbidden-in-naive keywords, tool checks, IDE hint) plus a template and a guide. The helper has one generic runner that reads JUnit XML or TRX. *Picked because* the three new toolchains all emit JUnit XML (`swift test --xunit-output`, Gradle by default, our own Lua runner), and it keeps the repo's "add a file and open a PR" model. Trade-off: a language with unusual needs must fit "run a command, read a results file".
- **B. One C# class per language (`ILanguage`).** Maximum flexibility and fully unit-tested, but every language is a code change and a helper rebuild, and more code to maintain.
- **C. Separate plugin per language in the same marketplace.** Rejected: one nudge per plugin, separate histories, duplicated picker/verifier, and no "least recently practised" across languages.

## Design

### Repository layout

```
catalog/*.json      C# topics, unchanged location and content (see "Upgrade window")
templates/challenge/  C# template, unchanged location
languages/
  csharp/   language.json  guide.md            (catalogDir → ../../catalog, templateDir → ../../templates/challenge)
  swift/    language.json  guide.md  template/  catalog/*.json
  kotlin/   language.json  guide.md  template/  catalog/*.json
  lua/      language.json  guide.md  template/  catalog/*.json
```

Each descriptor may set `catalogDir` and `templateDir` (default `catalog/` and `template/` next to it). Only C# uses the override. Topic ids only need to be unique **within** a language (`catalog/async-await.json` for C# and `languages/swift/catalog/async-await.json` can coexist). The language id is the folder name.

### Upgrade window (between `git pull` and `/daily:setup`)

The SessionStart hook keeps running the *old* helper DLL until setup rebuilds it, and that old helper loads every `*.json` under `catalog/` recursively and rejects duplicate ids. The layout above is chosen (my call) so that the old helper still sees exactly the C# catalog: it keeps nudging normally and keeps showing "plugin updated — run /daily:setup to rebuild". In the same window, the new skills call the old helper: when `CLI today` output has no `language` field, start/hint/check tell the developer to run `/daily:setup` and stop. As an extra guard, the new helper treats a still-`Picked` entry whose topic isn't in its language's catalog as re-pickable (see below).

### Language descriptor (`languages/<id>/language.json`)

Fields (names are a proposal; the builder may adjust):

| Field | Meaning | Example (Kotlin) |
|---|---|---|
| `id`, `name` | must equal folder; display name | `kotlin`, `Kotlin` |
| `stubDir` | folder (relative to the challenge root) holding the files the developer implements; reference/naive overlays copy onto it | `src/main/kotlin` |
| `testDir` | folder with tests (for skills and the check) | `src/test/kotlin` |
| `sourceExtensions` | code file extensions (naive scan, placeholder replacement) | `[".kt", ".kts"]` |
| `test.commands` | ordered alternatives; the first whose executable is found on PATH is used | `[["./gradlew", "test", "--console=plain"]]` (Windows: `gradlew.bat`) |
| `test.results` | glob for result files, relative to the challenge root | `build/test-results/test/*.xml` |
| `test.format` | `junit` or `trx` | `junit` |
| `test.env` | extra environment variables | `{}` |
| `naiveForbidden` | regex; a naive file matching it is rejected | `\b(throw|TODO\s*\(|error\s*\()` |
| `requires` | tool checks for setup: command, version args, minimum, install hint | `java -version` ≥ 17 |
| `placeholders` | template tokens and where their values come from | — |
| `ide` | one line for the hand-over message | "Open the folder in IntelliJ IDEA or Android Studio." |
| `levelHints` | one line per level for the setup question | Beginner: "null safety, data classes, collections" … |

Generic runner: copy challenge to temp (skipping build output dirs, configurable per language, e.g. `bin obj .build build .gradle`), overlay, run the first available command, collect every file matching `test.results`. **No results file or zero test cases = "does not build / no tests ran"** (protects the anti-goal of counting a crash as a pass). JUnit counting: count `<testcase>` elements; a test case with a `<failure>` or `<error>` child is failed, `<skipped>` is excluded from the total. TRX keeps today's parsing.

`ProcessRunner` generalises from `Dotnet(...)` to `Run(executable, args, env, timeout)`. The .NET-specific environment variables stay on the C# descriptor's `test.env`. The existing "never wait on pipes forever" behaviour is kept (Gradle's daemon outlives the build, like MSBuild nodes do).

### Per-language decisions

**C#** — unchanged behaviour: MSTest, `Challenge/` + `Challenge.Tests/`, `dotnet test` with TRX, `{{TFM}}` from `sdkMajor`, stub bodies `throw new NotImplementedException();`, naive forbids `throw`. IDE: Rider / `Challenge.slnx`.

**Swift** — SwiftPM package (`Package.swift`, `Sources/Challenge/`, `Tests/ChallengeTests/`), swift-tools-version 6.0, Foundation only. Tests in **XCTest** (my call: `swift test --xunit-output` reliably writes XCTest results on every toolchain; Swift Testing's XML output is newer and less uniform — revisit later). Results: `--xunit-output .build/results.xml`.
  **Important:** in Swift a `fatalError()` stub crashes the whole test process, so no results would be written. Stub bodies therefore return a placeholder value with a `// TODO: implement` comment (or `throw` for functions declared `throws`), never `fatalError`/`preconditionFailure`. Naive forbids `fatalError`, `preconditionFailure`, `throw`. Requires `swift --version` ≥ 6.0. IDE: "`open Package.swift` in Xcode."

**Kotlin** — JVM project, Gradle Kotlin DSL with the **Gradle wrapper committed in the template** (no Gradle install needed; JDK 17+ required). Dependencies: `kotlin("jvm")`, `kotlin("test")` with JUnit 5, `kotlinx-coroutines-core`, `kotlinx-coroutines-test` (coroutines/Flow are core topics; `runTest` keeps them deterministic). Stub bodies `TODO()` (throws `NotImplementedError`, caught per test by JUnit). Naive forbids `throw`, `TODO(`, `error(`. IDE: IntelliJ IDEA / Android Studio. Versions of Kotlin/Gradle/coroutines: pick current stable at build time and pin them.

**Lua** — target **Lua 5.1 semantics** (Neovim and LÖVE both run LuaJIT): no `goto`, `//`, bitwise operators, or integer subtypes; `table.unpack or unpack` if needed. Runner commands in order: `luajit`, `lua` (5.1–5.4 all run 5.1-style code), `nvim -l` (Neovim ≥ 0.9; covers anyone who has Neovim). Layout: `src/*.lua` (stub dir), `spec/*_spec.lua`, `spec/runner.lua`.
  `runner.lua` is a small, dependency-free test runner shipped in the template (busted-like API: `describe`, `it`, `assert.equals`, `assert.same` (deep), `assert.is_true`, `assert.has_error`), runs each test under `pcall`, writes JUnit XML to `results.xml`, and sets `package.path` to `src/`. Stub bodies `error("not implemented")`. Naive forbids `error(` and `assert(`. Neovim-flavoured challenges pass in a small fake of the `vim` API they use (e.g. buffer lines, keymap registry) instead of needing Neovim. Game challenges are pure logic. IDE: "Open the folder in Neovim."

### Config, state and migration

```jsonc
// config.json
{
  "languages": [ { "id": "csharp", "level": "Advanced" }, { "id": "lua", "level": "Beginner" } ],
  "workspace": "...", "sdkMajor": 10, "salt": 123
}
```

- History entries gain `language` (string id).
- **Migration on read** (my call): a config with the old top-level `level` and no `languages` becomes `languages: [{ id: "csharp", level }]`; history entries without `language` are `csharp`. Written back in the new shape on the next save. Covered by tests with a real old-format file.
- `config set languages csharp,lua` (keeps existing levels for kept languages, Beginner for new ones) and `config set level <language> <Level>`. `config set level <Level>` without a language is rejected with a usage message.

### Picking the day's language and topic

1. **Language:** among selected languages, the one whose most recent *Started or Completed* entry is oldest (never practised = oldest). Skipped and generation-failed days don't count as practice. Ties: order in `config.languages`. Deterministic.
2. **Topic and stretch:** today's `Picker.Choose` logic, unchanged, with that language's level, that language's catalog, and history filtered to that language.

Languages with an empty catalog are never picked. Tool checks are **not** run in the SessionStart hook (it has a 10-second timeout and `java -version`/`swift --version` are slow); setup already keeps languages with missing tools out of the config.

**Re-picking today's entry** (my call). An entry that is still `Picked` (nothing generated yet) is re-picked by `today`/`start` when:
- its language is no longer in `config.languages` (removed in setup), or
- its topic isn't in that language's catalog (upgrade window, renamed topic), or
- `/daily:start` finds the language's tools missing: it says which tool is missing (with the install hint) and re-picks today among the *other* selected languages. If there are none, it stops without marking the day failed.

Once an entry is `Started`, `Completed`, `Skipped` or `GenerationFailed` it is never re-picked (unchanged). Because a missing tool only redirects that one day, a broken language can't block the others every day. It will come up again the next day until the developer fixes the tool or removes the language.

### Helper commands

- `today` adds `language` (`id`, `name`, `stubDir`, `testDir`, `ide`, and the absolute path to `guide.md`) to its output.
- `scaffold <slug>` uses the day's language template; folder name becomes `<date>-<language>-<slug>` (my call, so mixed-language workspaces stay readable).
- `verify` uses the descriptor (stubDir, results, naiveForbidden).
- **New `run-tests <folder>`**: runs that folder's language test command and prints `{ ok, passed, failed, total, failures: [{ name, message }], output }`. `/daily:check` uses it instead of calling `dotnet test` directly, so skills never hard-code toolchains. The folder's language comes from the history entry whose `folder` matches, or from a `.daily-language` marker file written by `scaffold` (builder's choice).
- **New `languages`**: lists descriptors with `available` (tool checks run) for setup.
- Nudge: "☕ Today's {Language} challenge is ready ({Level} · {topic}{stretch}) — /daily:start when you have 15 min". Not-configured message drops "C#". Build stamp also hashes nothing new (descriptors are read at runtime, not compiled in).

### Skills

- **setup:** check .NET (unchanged) → build helper → `CLI languages` → AskUserQuestion multi-select of languages (each option notes if its tool is missing, with the install hint) → one level question per selected language (AskUserQuestion takes up to 4 questions per call, which fits all four), using that descriptor's `levelHints` → workspace → sdkMajor (only if C# selected) → **warm-up** (my call): for each selected non-C# language, scaffold the bare template into a temp folder and run its tests once, so the first real challenge doesn't pay for the Gradle download / SwiftPM first build. Report anything that fails with the install hint. A selected language whose tools are missing is kept out of the config until fixed, and setup says so.
- **start, hint, check:** rewritten to be language-neutral. They read `language.guide` (from `CLI today`) for everything language-specific: file layout, stub conventions, test framework and assertion style, allowed libraries, naive rules, idioms the review should look for. The C# guide carries today's C#-specific instructions verbatim so C# output does not change.
- Descriptions and README stop saying "C#" only.

### Catalog for the new languages (v1)

Each topic needs ≥ 2 levels, same schema as today. Starting set (≈ 10 per language; the builder writes the seeds, the author reviews them in the PR):

- **Swift:** optionals & unwrapping, value vs reference types (struct/class, copy-on-write), protocols & extensions, generics & associated types, enums with associated values & pattern matching, error handling (`throws`, `Result`), closures & capture lists (ARC, `weak`/`unowned`), collections & higher-order functions, async/await & structured concurrency, actors & `Sendable`, Codable.
- **Kotlin:** null safety, data/sealed classes & `when`, collections & sequences, extension functions & scope functions, higher-order functions & lambdas, generics & variance, coroutines basics & structured concurrency, Flow, delegation (`by lazy`, delegated properties), exceptions & `Result`, immutability.
- **Lua:** tables as arrays/maps, metatables & OOP (`__index`, `__eq`, `__lt`), closures & upvalues, modules & `require`, string patterns & `gsub`, varargs & multiple returns, coroutines (Neovim async + game sequencing), error handling (`pcall`/`error`), Neovim-style plugin structure (config merging, autocommand/keymap registry against a fake `vim`), game logic (vector math & AABB collision, state machines, fixed-timestep update loop, entity/component system).

### Tests

- Unit tests (no toolchain needed): descriptor loading/validation, JUnit and TRX counting (incl. "no file" and "zero tests" → null), language picking (least recent, ties, skips ignored, empty catalog skipped), re-picking a `Picked` entry (language removed, topic missing, tool missing) and never re-picking other statuses, config/history migration from an old-format file, catalog loading per language with cross-language duplicate ids allowed, the old recursive loader over `catalog/` still seeing only C# topics, scaffold folder naming and placeholder replacement.
- Integration tests (existing `[TestCategory("Integration")]` pattern): per language a tiny known-good challenge run through `verify`, marked inconclusive when that toolchain isn't on PATH.
- Catalog test extended to every language folder; descriptor test checks each `languages/<id>` has template, guide and catalog.

## What's left to the builder

- Exact descriptor field names and JSON shape, and how `run-tests` finds a folder's language.
- Pinned versions of Kotlin, Gradle, coroutines, swift-tools.
- The Lua runner's exact assertion set beyond the ones listed, and its JUnit output details.
- Wording of guides, level hints and skill text (C# guide must preserve today's C# instructions).
- Order of implementation (suggested: language-neutral core + C# moved onto it with no behaviour change → Lua → Kotlin → Swift → catalogs → README).
