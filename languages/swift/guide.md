# Swift language guide

Language-specific rules for the daily challenge. The skills reference these sections by heading.

## Layout

- Swift Package Manager project (package and library target `Challenge`, test target `ChallengeTests`), `swift-tools-version: 6.0`. Never edit `Package.swift`.
- `Sources/Challenge/` holds the stub (`*.swift`), `Tests/ChallengeTests/` holds the tests. Files may be nested in subfolders. There is no module-level `package` line; the library module is `Challenge`.
- Run with `swift test --parallel` from the challenge folder (the `--parallel` flag is required: without it Swift 6 writes no XCTest results file). The XCTest results go to `.build/results.xml`; a missing or empty file means a compile error, a crash or no tests ran: "does not build". Swift 6 also writes a separate `.build/results-swift-testing.xml`; it is ignored, so use XCTest only.
- Both targets use Swift 5 language mode (`swiftLanguageMode(.v5)`) so that ordinary classes, closures and async functions compile without Sendable errors. Challenges about concurrency still use `async`/`await`, actors and `Sendable` explicitly; they are just not forced to satisfy strict-concurrency diagnostics.
- Requires Swift 6.0+ (Xcode 16+ or a swift.org toolchain). macOS and Linux toolchains only; Foundation only, no UIKit/SwiftUI.

## Stub

- Everything the tests use is `public` (functions, types, initialisers, properties and protocol requirements), because the tests also work through `@testable import`. Every public declaration has a `///` doc comment giving the contract: parameters (`- Parameter`), result (`- Returns`), edge cases and errors (`- Throws`).
- Every function body to implement returns a placeholder value with a `// TODO: implement` comment, e.g. `return -1 // TODO: implement` or `return []`, `return ""`, `return nil`. Use `throw` only when the function is declared `throws` and the task is about error handling. Never use `fatalError`, `preconditionFailure`, `assertionFailure` or force-unwrap traps in a stub: a trap kills the whole test process instead of failing one test.
- The stub must compile: declare all types, protocols, enums and signatures the tests use. Supporting types the task doesn't ask them to write (structs, enums, protocols) may be complete.
- Anything time-based takes a clock closure (`() -> Date`), a `Clock` or a sleeper as a parameter or initialiser argument; never call `Date()`, `DispatchQueue.main` timers or `Task.sleep` inside the code under test unless it is injectable.

## Tests

- One or more files in `Tests/ChallengeTests/` using `import XCTest` and `@testable import Challenge`. Each test class is `final class XTests: XCTestCase`; each test is a method named `testSomething()` (it may be `throws` or `async`). Use `XCTAssertEqual`, `XCTAssertTrue`, `XCTAssertFalse`, `XCTAssertNil`, `XCTAssertThrowsError`, `XCTAssertNoThrow`. Do not use the Swift Testing framework (`import Testing`, `@Test`): only XCTest results are read.
- Async code is tested with `func testX() async throws { ... }`; never wait on real time. Do not use `XCTSkip` (a skipped test counts as a pass in the results).
- At least 3 tests, ideally 5-8, with descriptive method names. Every test must assert something that the naive solution gets wrong.
- Tests run in parallel by process, so each test must be independent (no shared mutable state between tests).
- Deterministic: no real time, network, file system or randomness; inject clocks and seeded generators as parameters.
- A compile error in the tests or stub produces no results, which counts as "does not build".

## Reference and naive

- `.verify/reference/` is a correct, idiomatic solution with the same file names and relative paths as the stub files under `Sources/Challenge/` (only files with code to implement), including the `public` modifiers.
- `.verify/naive/` has the same files with the same public API, each function returning `0`, `""`, `[]`, `nil`, `false` or echoing its input. It must compile, and must never contain `fatalError`, `preconditionFailure` or `throw` anywhere (not even in comments or strings). Async functions simply return the same trivial value; a function declared `throws` just returns without throwing.
- The reference must not crash: a trap (`fatalError`, array index out of range, failed force-unwrap) is never a pass.

## Allowed libraries

- The Swift standard library and Foundation only (plus `Swift Concurrency`, which is part of the toolchain). In tests, XCTest. No other packages, no UIKit/SwiftUI/Combine, no reflection tricks.

## Review: idiomatic Swift

- Compare with what the seed says a good solution shows. Look for: `let` over `var`, value types (`struct`, `enum`) where identity is not needed, optionals handled with `guard let`, `if let` and `??` (no `!`), enums with associated values and exhaustive `switch`, protocols and extensions, standard-library functions (`map`, `filter`, `reduce`, `compactMap`, `Dictionary(grouping:by:)`, `sorted(by:)`) instead of manual loops, `throws` and typed `Result` for errors, access control, `[weak self]` in escaping closures that could cycle, and in concurrent code `async`/`await`, actors and `Sendable` types. Mention a nicer construct only if it is genuinely better.

## Hint examples

- Name the concept or function that unlocks it, e.g. "look at `Dictionary(grouping:by:)` followed by `mapValues`", "an enum with associated values plus an exhaustive `switch` removes the default branch", "`compactMap` drops the `nil` results while transforming".
