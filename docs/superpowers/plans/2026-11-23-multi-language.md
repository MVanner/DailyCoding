# Multi-language Support (Swift, Kotlin, Lua) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let developers pick any of C#, Swift, Kotlin and Lua (each at its own level), keep one verified challenge per day, and drive every language through the same quality gate.

**Architecture:** Each language is a folder `languages/<id>/` with a `language.json` descriptor, a `guide.md` for the skills, a template and a catalog (C# points its descriptor at the existing `catalog/` and `templates/challenge/`). The helper loads descriptors at runtime; one generic test runner runs the descriptor's command and reads JUnit XML or TRX. Config/state gain a per-language level list and a `language` per history entry, migrated on read.

**Tech Stack:** .NET 10 console helper + MSTest 4 tests; SwiftPM + XCTest; Kotlin/JVM + Gradle wrapper + JUnit 5 + kotlinx-coroutines; Lua 5.1 semantics with a bundled pure-Lua runner.

**Spec:** `docs/superpowers/specs/2026-11-23-multi-language-design.md`

## Global Constraints

- Helper stays .NET 10 (`net10.0`, `RollForward=Major`); hook stays `dotnet Daily.Cli.dll nudge` with a 10 s timeout. No tool checks (`swift`/`java`/`lua`) inside `nudge`.
- `catalog/*.json` (C# topics) and `templates/challenge/` stay where they are, contents unchanged. New languages' catalogs live in `languages/<id>/catalog/`, never under `catalog/`.
- Quality gate values unchanged: `MinimumTests = 3`, `NaiveMustFail = 0.7` (stub and naive), 5-minute timeout per test run, at most 3 verify runs and 1 regeneration (skill text).
- "No results file or zero test cases" = does not build / no tests ran → `null`, never a pass.
- Language ids: `csharp`, `swift`, `kotlin`, `lua`. History entries without `language` are `csharp`; old config `level` becomes `languages: [{ "id": "csharp", "level": <level> }]`.
- Challenge folder name: `<yyyy-MM-dd>-<language>-<slug>`; slug rule unchanged (`^[a-z0-9-]{3,40}$`). Scaffold writes `.daily-language` (content: language id) in the folder root.
- Swift: swift-tools-version 6.0, XCTest, Foundation only, results `--xunit-output .build/results.xml`; stubs never use `fatalError`/`preconditionFailure`. Requires `swift` ≥ 6.0.
- Kotlin: JVM, Gradle Kotlin DSL, Gradle wrapper committed in the template, JUnit 5 + `kotlin("test")`, `kotlinx-coroutines-core` + `kotlinx-coroutines-test`; versions pinned to current stable. Requires `java` ≥ 17.
- Lua: Lua 5.1 semantics (no `goto`, `//`, bitwise operators, integer subtype). Runner alternatives in order: `luajit`, `lua`, `nvim -l`. One spec file `spec/challenge_spec.lua`.
- Tests in challenges are deterministic: no real time, network, file system or randomness (unchanged rule, now in every guide).
- Nudge copy: `☕ Today's {LanguageName} challenge is ready ({Level} · {topicName}{stretch}) — /daily:start when you have 15 min`. Not-configured copy: `☕ Daily coding challenges are installed — run /daily:setup to pick your languages`.

## Review Focus

1. **A test run that crashes or writes partial results** (Swift process crash, Gradle writing one XML per class with some missing, Lua runner dying on a syntax error) must read as "does not build / no tests ran", never as passing. → Task 3 tests `Junit_NoTestcases_IsNull`, `Junit_NoFiles_IsNull`; Task 7/8/9 integration tests `BrokenStub_DoesNotBuild`.
2. **Workspace paths with spaces** (existing tests use `"ws dir"`) must work for `./gradlew`, `swift test` and the Lua runner. → every language integration fixture in Tasks 7–9 uses a workspace with a space.
3. **Windows command resolution**: `./gradlew` must become `gradlew.bat`, and a missing executable must fall through to the next alternative (Lua). → Task 3 tests `ResolveCommand_PicksOsVariant`, `ResolveCommand_FallsThroughToNextAvailable`.
4. **An unfinished earlier challenge in a language the developer has since removed** must still work with `/daily:hint` and `/daily:check`. → Task 5 test `RunTests_UsesFolderMarker_EvenWhenLanguageNotInConfig`.
5. **Upgrading with an old config and history** (old `level`, entries without `language`, a `Picked` entry for today) must keep everything. → Task 2 `LoadConfig_MigratesOldLevel`, `LoadState_OldEntriesAreCsharp`; Task 4 `Ensure_PickedEntryWithUnknownTopic_IsRepicked`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Daily.Cli/Languages.cs` (new) | `LanguageDescriptor` records, `Language` (descriptor + resolved paths), `LanguageLoader` |
| `src/Daily.Cli/ToolCheck.cs` (new) | Runs a descriptor's `requires` checks, version comparison |
| `src/Daily.Cli/TestResults.cs` (new) | `TestRunResult`, `JUnitResults`, `TrxResults` parsers |
| `src/Daily.Cli/TestRunner.cs` (new) | Command resolution, results glob, run tests in a folder |
| `src/Daily.Cli/LanguagePicker.cs` (new) | Least-recently-practised language choice |
| `src/Daily.Cli/Catalog.cs` | adds `Catalogs.Load` (per-language map) |
| `src/Daily.Cli/State.cs` | `LanguageSetting`, new `DailyConfig`, `HistoryEntry.Language`, config migration |
| `src/Daily.Cli/Today.cs` | `CreatePickedEntry`, `NeedsRepick`, `Ensure` |
| `src/Daily.Cli/Commands.cs` | `today`/`record` via `Today.Ensure`; `config` keys; new `repick`, `languages`, `run-tests` |
| `src/Daily.Cli/Verifier.cs`, `Scaffolder.cs`, `ProcessRunner.cs`, `Nudge.cs`, `DailyPaths.cs`, `Program.cs` | made language-aware |
| `languages/{csharp,swift,kotlin,lua}/` | descriptors, guides, templates, catalogs |
| `skills/*/SKILL.md` | language-neutral skill text |
| `src/Daily.Cli.Tests/LanguageTests.cs`, `TestResultsTests.cs`, `TestRunnerTests.cs`, `LanguagePickerTests.cs`, `LanguageIntegrationTests.cs` (new) | tests |

---

### Task 1: Language descriptors + C# descriptor

**Files:**
- Create: `src/Daily.Cli/Languages.cs`, `languages/csharp/language.json`, `languages/csharp/guide.md`
- Modify: `src/Daily.Cli/DailyPaths.cs` (add `LanguagesDir`; remove `CatalogDir`/`TemplatesDir` in Task 5 once callers move), `src/Daily.Cli/ProcessRunner.cs`
- Test: `src/Daily.Cli.Tests/LanguageTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TestCommand(string[] Args, string? Os)` — `Os` is `"windows"`, `"unix"` or null (any).
  - `public sealed record TestSpec(TestCommand[] Commands, string Results, string Format, Dictionary<string,string>? Env)` — `Format` is `"junit"` or `"trx"`.
  - `public sealed record ToolRequirement(string Command, string[] Args, string? MinVersion, string InstallHint)`
  - `public sealed record LanguageDescriptor(string Id, string Name, string StubDir, string TestDir, string[] SourceExtensions, string[] IgnoreDirs, TestSpec Test, string NaiveForbidden, ToolRequirement[] Requires, string Ide, string RunnerHint, Dictionary<Level,string> LevelHints, string? CatalogDir, string? TemplateDir)` — `RunnerHint` is the install hint shown when no test command resolves.
  - `public sealed record Language(LanguageDescriptor Descriptor, string Dir, string CatalogDir, string TemplateDir, string GuidePath)` with `Id`/`Name` shortcuts.
  - `public static class LanguageLoader { IReadOnlyList<Language> Load(string languagesDir); Language Get(IReadOnlyList<Language> all, string id); }` — throws `LanguageException(string)` on invalid descriptors; `Get` throws on unknown id.
  - `ProcessRunner.Run(string executable, IReadOnlyList<string> args, string workingDir, IReadOnlyDictionary<string,string>? env, TimeSpan timeout) -> (int Exit, string Output)`; `ProcessRunner.IsOnPath(string executable) -> bool`. `Dotnet(...)` stays as a wrapper over `Run` (tests use it). Keep the "never wait on pipes forever" behaviour.
  - `DailyPaths.LanguagesDir => Path.Combine(PluginRoot, "languages")`.

- [ ] **Step 1: Write failing tests** in `LanguageTests.cs`:
  - `Load_ResolvesDefaultsNextToDescriptor`: temp `languages/lua/language.json` without `catalogDir`/`templateDir` → `CatalogDir == <dir>/catalog`, `TemplateDir == <dir>/template`, `GuidePath == <dir>/guide.md`.
  - `Load_ResolvesRelativeOverrides`: `"catalogDir": "../../catalog"` → absolute path `<root>/catalog`.
  - `Load_ThrowsWhenIdDoesNotMatchFolder`, `Load_ThrowsOnUnknownFormat` (`"format": "xml"` → `LanguageException` mentioning `format`), `Load_ThrowsWhenNaiveForbiddenIsNotARegex`, `Load_ThrowsWithoutTestCommands`.
  - `RealCsharpDescriptor_PointsAtExistingCatalogAndTemplate`: `LanguageLoader.Get(Load(RepoPaths.PluginRoot + "/languages"), "csharp")` → `CatalogDir` ends with `catalog`, `Directory.Exists(TemplateDir)`, `File.Exists(GuidePath)`, `Test.Format == "trx"`.
  - (moved to Task 3) `VersionAtLeast` data rows: `("openjdk version \"25.0.2\" 2026-01-20", "17") → true`, `("java version \"1.8.0_402\"", "17") → false`, `("Apple Swift version 6.4.2 (swiftlang...)", "6.0") → true`, `("Swift version 5.10", "6.0") → false`, `("no digits", "1") → false`. (Rule: first `\d+(\.\d+)*` in output; Java's `1.x` means major `x`.)
- [ ] **Step 2: Run** `dotnet test src/Daily.Cli.Tests --filter FullyQualifiedName~LanguageTests` → FAIL (types missing).
- [ ] **Step 3: Implement** `Languages.cs` (descriptor JSON uses `Json.Options`: camelCase, string enums) and `ProcessRunner.Run`/`IsOnPath` (`IsOnPath` searches `PATH`, adding `PATHEXT` extensions on Windows).
- [ ] **Step 4: Write `languages/csharp/language.json`**: `stubDir "Challenge"`, `testDir "Challenge.Tests"`, `sourceExtensions [".cs", ".csproj", ".slnx"]`, `ignoreDirs ["bin","obj","TestResults"]`, command `["dotnet","test","--logger","trx;LogFileName=result.trx","--results-directory","TestResults"]`, `results "TestResults/**/*.trx"`, `format "trx"`, env = the five variables currently set in `ProcessRunner.Dotnet`, `naiveForbidden "\\bthrow\\b"`, requires `dotnet --version` ≥ 10 (hint: install .NET 10 SDK from https://dotnet.microsoft.com/download), `ide "Open Challenge.slnx in Rider."`, `runnerHint` = the dotnet install hint, `levelHints` = the four lines in `skills/setup/SKILL.md` step 4, `catalogDir "../../catalog"`, `templateDir "../../templates/challenge"`.
- [ ] **Step 5: Write `languages/csharp/guide.md`** with these fixed headings (every guide uses them; skills reference them by name): `## Layout`, `## Stub`, `## Tests`, `## Reference and naive`, `## Allowed libraries`, `## Review: idiomatic C#`, `## Hint examples`. Content = the C#-specific sentences moved verbatim from `skills/start/SKILL.md` §2–3 (namespace `Challenge`, XML doc comments, `throw new NotImplementedException();`, MSTest attributes, `Challenge.Tests` namespace, naive returns `default`/empty/echo and no `throw`, ".NET base library only"), `skills/check/SKILL.md` ("idiomatic C#") and `skills/hint/SKILL.md` (the `Task.WhenAll` / switch-expression examples).
- [ ] **Step 6: Run** the filter again → PASS. Run the full suite → PASS.
- [ ] **Step 7: Commit** `feat: language descriptors and C# descriptor`.

---

### Task 2: Config, history and migration

**Files:**
- Modify: `src/Daily.Cli/State.cs`, `src/Daily.Cli/Commands.cs` (`ConfigCommand`, `Defaults`), every test that constructs `DailyConfig` (`VerifierTests`, `ScaffolderTests`, `NudgeTests`, `CommandsTests`, `StateStoreTests`)
- Test: `src/Daily.Cli.Tests/StateStoreTests.cs`, `src/Daily.Cli.Tests/CommandsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record LanguageSetting(string Id, Level Level)`
  - `public sealed record DailyConfig(List<LanguageSetting> Languages, string Workspace, int SdkMajor, int Salt)` with `Level LevelFor(string id)` (throws if not selected).
  - `HistoryEntry` gains `public string Language { get; init; } = "csharp";`
  - CLI: `config set languages <id,id,...>` (validates against `LanguageLoader`; keeps levels of kept ids, `Beginner` for new ones, order as given), `config set level <languageId> <Level>`. `config set level <Level>` (2 args) → exit 1 with usage. `workspace`/`sdkMajor` unchanged. `Defaults()` → `Languages = [csharp Beginner]`.

- [ ] **Step 1: Write failing tests**:
  - `LoadConfig_MigratesOldLevel`: write `{"level":"Advanced","workspace":"/w","sdkMajor":10,"salt":5}` → `Languages` is `[("csharp", Advanced)]`, workspace/sdk/salt kept; after `SaveConfig(loaded)` the file contains `"languages"` and not `"level"`.
  - `LoadState_OldEntriesAreCsharp`: state JSON with an entry lacking `language` → `entry.Language == "csharp"`.
  - `SaveThenLoad_RoundTrips` updated to the new shape with two languages.
  - `ConfigSetLanguages_KeepsExistingLevels`: config `[csharp Expert]`, run `config set languages csharp,lua` → `[csharp Expert, lua Beginner]`.
  - `ConfigSetLanguages_RejectsUnknownId` (`cobol` → exit 1, config unchanged), `ConfigSetLevel_PerLanguage` (`config set level lua Intermediate`), `ConfigSetLevel_WithoutLanguage_IsRejected`, `ConfigSetLevel_ForUnselectedLanguage_IsRejected`.
- [ ] **Step 2: Run** them → FAIL.
- [ ] **Step 3: Implement.** Migration lives in `StateStore.LoadConfig`: parse as `JsonNode`; when `languages` is absent and `level` is a valid `Level`, add `languages: [{id:"csharp", level}]` and remove `level` before deserializing. Keep the existing corrupt-file behaviour (`.bad`). Update all test constructions of `DailyConfig` to `new DailyConfig([new("csharp", Level.Beginner)], ws, sdk, salt)`. Code still compiles against `config.Level` in `Today`/`Picker` callers: replace with `config.LevelFor("csharp")` temporarily (Task 4 removes it).
- [ ] **Step 4: Run** full suite → PASS.
- [ ] **Step 5: Commit** `feat: per-language config with migration from single level`.

---

### Task 3: Generic test runner and result parsing

**Files:**
- Create: `src/Daily.Cli/TestResults.cs`, `src/Daily.Cli/TestRunner.cs`, `src/Daily.Cli/ToolCheck.cs`
- Test: `src/Daily.Cli.Tests/TestResultsTests.cs`, `src/Daily.Cli.Tests/TestRunnerTests.cs`, `src/Daily.Cli.Tests/LanguageTests.cs` (VersionAtLeast rows from Task 1)

**Interfaces:**
- Consumes: `Language`, `TestSpec`, `TestCommand`, `ProcessRunner.Run`/`IsOnPath` (Task 1).
- Produces:
  - `public sealed record TestFailure(string Name, string Message)`
  - `public sealed record TestRunResult(int Passed, int Failed, int Total, TestFailure[] Failures)`
  - `public static class JUnitResults { TestRunResult? Read(IEnumerable<string> files); }` — counts `<testcase>` across all files; `<failure>`/`<error>` child → failed (message = its `message` attribute, else first line of its text); `<skipped>` → excluded from `Total`; zero test cases or no files → `null`; unparsable XML file → `null`.
  - `public static class TrxResults { TestRunResult? Read(IEnumerable<string> files); }` — today's `Counters` parsing plus failures from `UnitTestResult[@outcome='Failed']` (`testName`, `Output/ErrorInfo/Message`).
  - `public static class TestRunner { string[]? ResolveCommand(Language lang, string workDir, bool isWindows, Func<string,bool> isOnPath); IReadOnlyList<string> FindResults(string workDir, string pattern); (TestRunResult? Result, string Output) Run(Language lang, string workDir, TimeSpan timeout); }` — `Run` returns `(null, "<no runner found: …install hints…>")` when no command resolves.
  - `public sealed record ToolStatus(bool Available, string[] Missing)`; `public static class ToolCheck { ToolStatus Check(Language lang); static bool VersionAtLeast(string output, string min); }` — unavailable when any `Requires` entry is missing/below `MinVersion` (its `InstallHint` goes in `Missing`, 15 s timeout per check) **or** `TestRunner.ResolveCommand(lang, lang.TemplateDir, OperatingSystem.IsWindows(), ProcessRunner.IsOnPath)` is null (`RunnerHint` goes in `Missing`).

- [ ] **Step 1: Write failing tests** with inline XML strings (plus the `VersionAtLeast` rows listed in Task 1, and `ToolCheck_NoRunner_ReportsRunnerHint` using a temp descriptor whose only command is a nonexistent executable):
  - `Junit_CountsAcrossFiles` (two Gradle-style files, 3 + 2 cases, 1 failure, 1 error → Passed 3, Failed 2, Total 5, failure names `Class.method`), `Junit_SkippedExcluded`, `Junit_NoTestcases_IsNull` (`<testsuites/>`), `Junit_NoFiles_IsNull`, `Junit_InvalidXml_IsNull`, `Junit_ReadsSwiftXunitOutput` (sample shaped like `swift test --xunit-output`: `<testsuites><testsuite name="xctest" tests="2" failures="1"><testcase classname="ChallengeTests.CalcTests" name="testAdds"><failure message="XCTAssertEqual failed"/></testcase>…`).
  - `Trx_ReadsCountersAndFailures`.
  - `ResolveCommand_PicksOsVariant`: commands `[{args:["./gradlew","test"],os:"unix"},{args:["gradlew.bat","test"],os:"windows"}]`, `isWindows:true`, `gradlew.bat` exists in workDir → `["gradlew.bat","test"]`.
  - `ResolveCommand_FallsThroughToNextAvailable`: `luajit` not on path, `lua` on path → `["lua","spec/runner.lua"]`; none → `null`.
  - `FindResults_SupportsDoubleStar` (`TestResults/**/*.trx` finds nested file), `FindResults_SingleLevelGlob` (`build/test-results/test/*.xml`), `FindResults_ExactFile` (`.build/results.xml`).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** `TestResults.cs`, `TestRunner.cs`, `ToolCheck.cs`. Relative executables (starting `./`, or ending `.bat`/`.cmd`) count as available when the file exists in `workDir`; on unix, run `./gradlew` directly (the template ships it executable; if `File.GetUnixFileMode` lacks execute, set it before running). Glob: pattern = directory part + optional `**/` + file pattern; no other wildcard forms.
- [ ] **Step 4: Run** → PASS; full suite PASS.
- [ ] **Step 5: Commit** `feat: generic test runner with JUnit and TRX results`.

---

### Task 4: Per-language catalogs, language picking and re-picking

**Files:**
- Create: `src/Daily.Cli/LanguagePicker.cs`
- Modify: `src/Daily.Cli/Catalog.cs`, `src/Daily.Cli/Today.cs`, `src/Daily.Cli/Commands.cs` (`TodayCommand`, `RecordCommand` use `Today.Ensure`; remove `EnsureEntry`)
- Test: `src/Daily.Cli.Tests/LanguagePickerTests.cs`, `src/Daily.Cli.Tests/CatalogTests.cs`

**Interfaces:**
- Consumes: `Language` (Task 1), `DailyConfig`/`LanguageSetting`/`HistoryEntry.Language` (Task 2), `Picker.Choose` (unchanged signature).
- Produces:
  - `public static class Catalogs { IReadOnlyDictionary<string, IReadOnlyList<Topic>> Load(IReadOnlyList<Language> languages); }` — uses `CatalogLoader.Load(lang.CatalogDir)`; a missing catalog dir = empty list.
  - `public static class LanguagePicker { LanguageSetting? Choose(IReadOnlyList<LanguageSetting> selected, IReadOnlyDictionary<string, IReadOnlyList<Topic>> catalogs, IReadOnlyList<HistoryEntry> history, IReadOnlySet<string>? exclude = null); }`
  - `Today.CreatePickedEntry(DateOnly today, DailyConfig config, IReadOnlyDictionary<string, IReadOnlyList<Topic>> catalogs, IReadOnlyList<HistoryEntry> history, IReadOnlySet<string>? exclude = null) -> HistoryEntry?` (sets `Language`; history passed to `Picker.Choose` filtered to that language).
  - `Today.NeedsRepick(HistoryEntry entry, DailyConfig config, IReadOnlyDictionary<string, IReadOnlyList<Topic>> catalogs) -> bool` — only for `Picked`: language not selected, or topic not in that language's catalog.
  - `Today.Ensure(StateStore store, DailyConfig config, IReadOnlyDictionary<string, IReadOnlyList<Topic>> catalogs, DateOnly today) -> HistoryEntry?` — creates, or re-picks when `NeedsRepick`; null when no usable language.

- [ ] **Step 1: Write failing tests:**
  - `Choose_NeverPractisedWins`: selected `[csharp, lua]`, history has csharp Completed yesterday → `lua`.
  - `Choose_LeastRecentWins`: csharp 1 day ago, lua 3 days ago → `lua`.
  - `Choose_SkippedAndFailedDontCount`: lua Skipped yesterday + GenerationFailed 2 days ago, csharp Completed 5 days ago, lua Completed 10 days ago → `lua`.
  - `Choose_TieUsesConfigOrder`: neither practised, selected `[kotlin, csharp]` → `kotlin`.
  - `Choose_SkipsEmptyCatalog`, `Choose_RespectsExclude`, `Choose_NoneUsable_ReturnsNull`.
  - `CreatePickedEntry_FiltersHistoryToLanguage`: a C# topic id `generics` done yesterday must not suppress Swift `generics` as "recent" (assert via `Picker` behaviour: Swift catalog with only `generics` + another topic, deterministic salt, both picks possible → just assert entry.Language == "swift" and `TopicId` is in the Swift catalog).
  - `Ensure_PickedEntryWithRemovedLanguage_IsRepicked`, `Ensure_PickedEntryWithUnknownTopic_IsRepicked`, `Ensure_StartedEntry_IsNeverRepicked` (language removed but status Started → same entry returned).
  - `CatalogTests.OldRecursiveLoad_SeesOnlyCsharpTopics`: `CatalogLoader.Load(<root>/catalog)` count equals `Catalogs.Load(real languages)["csharp"]` count, and no `*.json` exists under `catalog/` outside its top level.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Language order rule: key = date of latest `Started`/`Completed` entry for that language (`DateOnly.MinValue` if none); lowest key wins; ties by index in `selected`. Remove the temporary `LevelFor("csharp")` from Task 2.
- [ ] **Step 4: Run** → PASS; full suite PASS.
- [ ] **Step 5: Commit** `feat: pick the least recently practised language`.

---

### Task 5: Language-aware scaffold, verify and new commands

**Files:**
- Modify: `src/Daily.Cli/Scaffolder.cs`, `src/Daily.Cli/Verifier.cs`, `src/Daily.Cli/Commands.cs`, `src/Daily.Cli/Program.cs`, `src/Daily.Cli/DailyPaths.cs` (drop `CatalogDir`, `TemplatesDir`)
- Test: `src/Daily.Cli.Tests/ScaffolderTests.cs`, `src/Daily.Cli.Tests/VerifierTests.cs`, `src/Daily.Cli.Tests/CommandsTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `Scaffolder.Create(DailyPaths paths, DailyConfig config, Language language, DateOnly date, string slug, bool force) -> string` — folder `<date>-<language.Id>-<slug>`; copies `language.TemplateDir`; replaces `{{TFM}}` (`net{SdkMajor}.0`) in files whose extension is in `SourceExtensions` ∪ {`.md`, `.json`}; writes `.daily-language`. Command `scaffold <slug> [--force] [--date d]` uses today's entry's language.
  - `Scaffolder.LanguageOf(string folder) -> string?` (reads `.daily-language`; null when absent → callers treat as `csharp` for folders made before this change).
  - `Verifier.Run(DailyPaths paths, Language language, string challengeDir, DateOnly date) -> VerifyResult` — overlays onto `StubDir`; naive scan = files with `SourceExtensions` checked against `NaiveForbidden`; temp copy skips `IgnoreDirs` + `.verify`; uses `TestRunner.Run`. Problem text for the naive check names the matched keyword: ``naive: {file} contains `{match}`; …``.
  - Commands: `today` output adds `language: { id, name, stubDir, testDir, ide, guide, available, missing }` (tool check for that one language only); `repick --exclude <id> [--date d]` (only when `Picked`; prints new entry; exit 1 with message when none usable); `languages` (prints `[{ id, name, available, missing, levelHints }]`); `run-tests <folder>` (prints `{ ok, passed, failed, total, failures, output }`, `ok` = results non-null and `failed == 0`; output trimmed to the last 4000 chars).

- [ ] **Step 1: Write failing tests:**
  - `ScaffolderTests.Create_NamesFolderWithLanguageAndWritesMarker` (`2026-11-16-csharp-sum`, marker content `csharp`); existing scaffold tests updated to the new signature and folder name.
  - `CommandsTests.Today_IncludesLanguage` (keys `id`, `guide` present; `guide` file exists).
  - `CommandsTests.Repick_ExcludesLanguage`: config `[csharp, lua]` with a Lua catalog in a temp plugin root, today Picked as lua → after `repick --exclude lua`, entry language is `csharp`; `Repick_StartedEntry_Fails`.
  - `CommandsTests.RunTests_UsesFolderMarker_EvenWhenLanguageNotInConfig` (Integration category: C# folder, config only `[lua]` → runs `dotnet test`, `ok` true for a passing challenge).
  - `VerifierTests`: existing tests pass unchanged in behaviour using `LanguageLoader.Get(..., "csharp")`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement**; register `repick`, `languages`, `run-tests` in `Program.Handlers`. Temp plugin roots in tests: copy `languages/csharp` descriptor with absolute `catalogDir`/`templateDir`, or build a minimal one with `TempDir.Write`.
- [ ] **Step 4: Run** full suite including Integration → PASS.
- [ ] **Step 5: Commit** `feat: language-aware scaffold, verify, run-tests, repick and languages`.

---

### Task 6: Nudge copy and no-language handling

**Files:**
- Modify: `src/Daily.Cli/Nudge.cs`
- Test: `src/Daily.Cli.Tests/NudgeTests.cs`

**Interfaces:**
- Consumes: `Today.Ensure`-equivalent logic (nudge keeps its own single `SaveState` write; use `Today.CreatePickedEntry`/`NeedsRepick`), `LanguageLoader`, `Catalogs`.

- [ ] **Step 1: Write failing tests:** `FirstRunOfDay_NamesLanguage` (C# config → message starts `☕ Today's C# challenge is ready (`), `LuaDay_NamesLua`, `NotConfigured_Copy` (exact Global Constraints string), `NoUsableLanguage_SuggestsSetup` (selected language with empty catalog → `☕ No daily challenge language is usable — run /daily:setup`), keep `HookOutput_IsAsciiOnly`, `NudgeCommand_BrokenCatalog_IsSilentLogsAndExitsZero` (break a Lua catalog file too).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** full suite → PASS.
- [ ] **Step 5: Commit** `feat: nudge names the day's language`.

---

### Task 7: Lua language

**Files:**
- Create: `languages/lua/language.json`, `languages/lua/guide.md`, `languages/lua/template/{README.md, src/.gitkeep, spec/runner.lua, spec/challenge_spec.lua, .gitignore}`
- Test: `src/Daily.Cli.Tests/LanguageIntegrationTests.cs` (Integration category)

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces: `runner.lua` API used by every Lua challenge: `describe(name, fn)`, `it(name, fn)`, `assert.equals(expected, actual)`, `assert.same(expected, actual)` (deep table compare), `assert.is_true(v)`, `assert.is_false(v)`, `assert.is_nil(v)`, `assert.has_error(fn, [substring])`. Runner prepends `./src/?.lua;./src/?/init.lua` to `package.path`, loads `spec/challenge_spec.lua` under `pcall`, runs each `it` under `pcall`, writes `results.xml` (JUnit, `classname` = describe path, `name` = it name, failures with `message`), exits 0 always. A load error of the spec file writes no `results.xml`.

- [ ] **Step 1: Descriptor**: `stubDir "src"`, `testDir "spec"`, `sourceExtensions [".lua"]`, `ignoreDirs []`, commands `luajit spec/runner.lua` → `lua spec/runner.lua` → `nvim -l spec/runner.lua`, `results "results.xml"`, `format "junit"`, `naiveForbidden "\\b(error|assert)\\s*\\("`, `requires []` (availability comes from command resolution), `runnerHint "Install LuaJIT (brew install luajit), or Lua 5.1+, or Neovim 0.9+"`, `ide "Open the folder in Neovim."`, level hints (Beginner: tables, strings, functions, modules; Intermediate: closures, metatables, varargs, string patterns; Advanced: coroutines, OOP with metatables, Neovim plugin structure, game loops; Expert: performance in LuaJIT, environments, weak tables, ECS design).
- [ ] **Step 2: Write failing integration tests** (`[TestCategory("Integration")]`, `Assert.Inconclusive` when no Lua runner is on PATH; workspace `TempDir("ws dir")`): `Lua_GoodChallenge_Verifies` (stub `src/calc.lua` with `error("not implemented")`, reference `sum`, naive returning 0, 4 tests → `Ok`), `Lua_LenientTests_Rejected`, `Lua_NaiveWithError_Rejected` (problem mentions `error(`), `Lua_BrokenStub_DoesNotBuild` (spec file with syntax error → `StubBuilds == false`), `Lua_RunTests_ReportsFailures`.
- [ ] **Step 3: Run** → FAIL.
- [ ] **Step 4: Write `runner.lua`** (Lua 5.1-compatible; escape XML `& < > "`), template README placeholder, and `guide.md` with the fixed headings: modules return a table (`local M = {} … return M`), stubs `error("not implemented")`, naive returns `nil`/`0`/`""`/`{}` or echoes input with no `error(`/`assert(`, Neovim challenges take a fake `vim` table as a parameter, game challenges are pure logic with `dt` passed in, Lua 5.1 rules from Global Constraints.
- [ ] **Step 5: Run** integration tests with `luajit`, then with `PATH` lacking `luajit` (so `lua` is used) → PASS both. Also run `nvim -l spec/runner.lua` once by hand in a scaffolded folder → `results.xml` written.
- [ ] **Step 6: Commit** `feat: Lua language support`.

---

### Task 8: Kotlin language

**Files:**
- Create: `languages/kotlin/language.json`, `languages/kotlin/guide.md`, `languages/kotlin/template/{settings.gradle.kts, build.gradle.kts, gradle.properties, gradlew, gradlew.bat, gradle/wrapper/gradle-wrapper.jar, gradle/wrapper/gradle-wrapper.properties, src/main/kotlin/.gitkeep, src/test/kotlin/.gitkeep, README.md, .gitignore}`
- Modify: `src/Daily.Cli.Tests/LanguageIntegrationTests.cs`

- [ ] **Step 1: Generate the wrapper** with a temporary Gradle distribution downloaded to the session scratchpad (do not install Gradle system-wide): `gradle wrapper --gradle-version <current stable>` in the template folder; commit `gradlew` with mode 755 (`git update-index --chmod=+x`).
- [ ] **Step 2: Build files**: root project `challenge`; `kotlin("jvm")` pinned; `jvmToolchain` **not** set (use whatever JDK ≥ 17 runs Gradle); dependencies `kotlinx-coroutines-core`, test `kotlin("test")`, `kotlinx-coroutines-test`; `tasks.test { useJUnitPlatform(); ignoreFailures = true }`; `gradle.properties`: `org.gradle.console=plain`, `kotlin.code.style=official`.
- [ ] **Step 3: Descriptor**: `stubDir "src/main/kotlin"`, `testDir "src/test/kotlin"`, `sourceExtensions [".kt", ".kts"]`, `ignoreDirs ["build", ".gradle", ".kotlin"]`, commands `./gradlew test --console=plain --quiet` (unix) / `gradlew.bat test --console=plain --quiet` (windows), `results "build/test-results/test/*.xml"`, `naiveForbidden "\\b(throw|TODO\\s*\\(|error\\s*\\()"`, requires `java -version` ≥ 17 (hint: install a JDK 17+, e.g. Temurin; Android Studio's bundled JDK works when `JAVA_HOME` points to it), `runnerHint` = same text, `ide "Open the folder in IntelliJ IDEA or Android Studio."`, level hints (Beginner: null safety, data classes, collections; Intermediate: sealed classes & `when`, extension/scope functions, lambdas; Advanced: coroutines, Flow, generics & variance, delegation; Expert: structured concurrency edge cases, inline/reified, DSL builders).
- [ ] **Step 4: Write failing integration tests** (Inconclusive without `java` ≥ 17; workspace with a space): `Kotlin_GoodChallenge_Verifies` (stub `fun sum(values: List<Int>): Int = TODO()` in package `challenge`, 4 `@Test` functions with `kotlin.test.assertEquals`), `Kotlin_NaiveWithTodo_Rejected`, `Kotlin_BrokenStub_DoesNotBuild` (compile error → `StubBuilds == false`), `Kotlin_CoroutineTest_Runs` (one `runTest { }` test in the good challenge's suite passes against the reference).
- [ ] **Step 5: Run** → FAIL, then write `guide.md` (package `challenge`, KDoc on public API, stub bodies `TODO()`, `runTest` and `TestScope`/virtual time for anything time-based, naive returns `0`/`""`/`emptyList()`/`null`/echo and never throws), then run → PASS. Record the wall time of `Kotlin_GoodChallenge_Verifies` (warm Gradle) in the commit message; if it exceeds 4 minutes, stop and report before continuing.
- [ ] **Step 6: Commit** `feat: Kotlin language support`.

---

### Task 9: Swift language

**Files:**
- Create: `languages/swift/language.json`, `languages/swift/guide.md`, `languages/swift/template/{Package.swift, Sources/Challenge/.gitkeep, Tests/ChallengeTests/.gitkeep, README.md, .gitignore}`
- Modify: `src/Daily.Cli.Tests/LanguageIntegrationTests.cs`

- [ ] **Step 1: Template**: `// swift-tools-version: 6.0`, package `Challenge`, library target `Challenge`, test target `ChallengeTests` depending on it; tests use `import XCTest` + `@testable import Challenge`.
- [ ] **Step 2: Descriptor**: `stubDir "Sources/Challenge"`, `testDir "Tests/ChallengeTests"`, `sourceExtensions [".swift"]`, `ignoreDirs [".build", ".swiftpm"]`, command `swift test --xunit-output .build/results.xml`, `results ".build/results.xml"`, `naiveForbidden "\\b(fatalError|preconditionFailure|throw)\\b"`, requires `swift --version` ≥ 6.0 (hint: install Xcode 16+ or a Swift 6 toolchain from swift.org), `runnerHint` = same text, `ide "Run `open Package.swift` to open it in Xcode."`, level hints (Beginner: optionals, structs vs classes, collections; Intermediate: protocols & extensions, enums with associated values, error handling, closures; Advanced: generics & associated types, async/await, actors, ARC & capture lists; Expert: Sendable & strict concurrency, copy-on-write types, result builders, performance).
- [ ] **Step 3: Write failing integration tests** (Inconclusive without `swift` ≥ 6.0; workspace with a space): `Swift_GoodChallenge_Verifies` (stub `public func sum(_ values: [Int]) -> Int { return -1 // TODO: implement }`), `Swift_NaiveWithFatalError_Rejected`, `Swift_BrokenStub_DoesNotBuild`, `Swift_CrashingTest_IsNotAPass` (reference that calls `fatalError` → result is "does not build / no tests ran" or failing, never `Ok`).
- [ ] **Step 4: Run** → FAIL, then write `guide.md` (public API marked `public`, doc comments `///`, stubs return a placeholder value with `// TODO: implement` or `throw` only when the function is declared `throws` and the task is about errors, never `fatalError`/`preconditionFailure`; async tests use `async` XCTest methods; naive returns `0`/`""`/`[]`/`nil`/echo), then run → PASS.
- [ ] **Step 5: Commit** `feat: Swift language support`.

---

### Task 10: Language-neutral skills

**Files:**
- Modify: `skills/setup/SKILL.md`, `skills/start/SKILL.md`, `skills/hint/SKILL.md`, `skills/check/SKILL.md`, `skills/skip/SKILL.md`
- Test: `src/Daily.Cli.Tests/SkillTextTests.cs` (new)

- [ ] **Step 1: Write failing test** `SkillTextTests.SkillsAreLanguageNeutral`: no `SKILL.md` contains `dotnet test`, `MSTest`, `NotImplementedException`, `Challenge.slnx` or `C# challenge`; `start`, `hint`, `check` each contain `language.guide` and the phrase `run /daily:setup` for the missing-`language` case; `setup` contains `CLI languages`.
- [ ] **Step 2: Rewrite:**
  - **setup**: .NET check + build (unchanged) → `CLI languages` → AskUserQuestion multiSelect "Which languages do you want daily challenges in?" (option descriptions say when a tool is missing, with its hint) → one AskUserQuestion call with one level question per selected language using `levelHints` → `CLI config set languages …` (only available ones; say which were left out and why) → `CLI config set level <id> <Level>` each → workspace (unchanged) → `sdkMajor` only if `csharp` selected → warm-up for each selected non-C# language: `CLI scaffold` is date-bound, so warm-up runs `run-tests` on a copy of the bare template in a temp folder via a new `CLI warmup <languageId>` command (add it in this task: copies the template to a temp dir, runs its test command once, prints `{ ok, seconds, output }`, ignores "no tests ran") → done message listing languages and levels.
  - **start**: after `CLI today`, if `language` is missing → "run /daily:setup" and stop; if `language.available` is false → say what's missing (hints) and run `CLI repick --exclude <id> --date <date>`, then continue with the new entry (stop if it fails). §2–3 refer to `language.guide` sections by heading instead of C# text; files go in `language.stubDir` / `language.testDir`; overlays mirror paths under `stubDir`. Hand-over uses `language.ide`.
  - **hint**: read `## Hint examples` from the guide; missing-`language` rule.
  - **check**: `CLI run-tests "<folder>"` replaces `dotnet test`; review section "idiomatic {language.name}" uses `## Review: idiomatic …` from the guide.
  - **skip**: description "Skip today's daily challenge".
  - Descriptions (front matter) drop "C#".
- [ ] **Step 3: Run** full suite → PASS.
- [ ] **Step 4: Manual dry run** (C#): with a temp data dir run `CLI today`, `CLI scaffold demo`, write a tiny challenge, `CLI verify`, `CLI run-tests` → JSON as the skills expect. Repeat `today` → `scaffold` → `run-tests` for Lua.
- [ ] **Step 5: Commit** `feat: language-neutral skills, setup picks languages and levels`.

---

### Task 11: Catalogs for Swift, Kotlin and Lua

**Files:**
- Create: `languages/swift/catalog/*.json`, `languages/kotlin/catalog/*.json`, `languages/lua/catalog/*.json`
- Modify: `src/Daily.Cli.Tests/CatalogTests.cs`, `src/Daily.Cli.Tests/LanguageTests.cs`

- [ ] **Step 1: Write failing tests**: `EveryLanguage_HasTemplateGuideAndCatalog` (for each real language: template dir exists, guide has all seven headings, catalog has ≥ 10 topics), `RealCatalogs_AreValid` (the existing `RealCatalog_IsValid` rules applied per language: ≥ 2 levels per topic, every seed has `learningGoal`, ≥ 1 gotcha, ≥ 1 `goodSolutionShows`).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Write topics** — exactly the lists in the spec's "Catalog for the new languages (v1)" section, one file per topic, ids kebab-case (e.g. `optionals`, `value-vs-reference`, `null-safety`, `sealed-when`, `metatables-oop`, `neovim-plugin-structure`, `game-state-machines`). Seeds follow the C# catalog's style; Lua game/Neovim seeds name the fake-`vim`/pure-logic approach in `shapeHints`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat: starter catalogs for Swift, Kotlin and Lua`.

---

### Task 12: Docs and packaging

**Files:**
- Modify: `README.md`, `.claude-plugin/plugin.json` (description; version `0.2.0`), `.claude-plugin/marketplace.json` (descriptions), `hooks/hooks.json` (description), `install.sh`, `install.ps1` (final message mentions choosing languages)

- [ ] **Step 1:** README: title/intro cover four languages; requirements table (all: .NET 10 SDK for the helper; Swift: Swift 6 / Xcode 16+, macOS recommended; Kotlin: JDK 17+; Lua: LuaJIT, Lua 5.1+ or Neovim 0.9+); "How it works" adds language rotation; "Adding or tuning topics" covers `languages/<id>/catalog/`; new "Adding a language" section (descriptor fields, template, guide headings, catalog); upgrade note: after `git pull`, run `/daily:setup` (old settings carry over as C#).
- [ ] **Step 2: Run** full suite `dotnet test src/Daily.Cli.Tests` → all PASS (Integration tests for missing toolchains Inconclusive, none here since all four are installed).
- [ ] **Step 3: Upgrade check by hand**: build the helper from `main` into a temp data dir, create config/state with it, switch to this branch, run the *old* DLL's `nudge` → still prints the C# nudge with the stale suffix; build the new helper → `config show` shows `languages: [csharp …]`, history intact.
- [ ] **Step 4: Commit** `docs: multi-language README and packaging`.
