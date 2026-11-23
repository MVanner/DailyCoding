# Kotlin language guide

Language-specific rules for the daily challenge. The skills reference these sections by heading.

## Layout

- Gradle Kotlin DSL project (root project `challenge`) on the JVM with the committed Gradle wrapper. Never edit `build.gradle.kts`, `settings.gradle.kts`, `gradle.properties` or the wrapper files.
- `src/main/kotlin/` holds the stub (`*.kt`), `src/test/kotlin/` holds the tests. Files may be nested in subfolders, but every file declares `package challenge`.
- Run with `./gradlew test` (`gradlew.bat test` on Windows) from the challenge folder. The first run downloads Gradle and the dependencies, so it needs network and is slow; later runs are fast. Results go to `build/test-results/test/*.xml`.
- Test failures do not fail the build (`ignoreFailures`), so a missing or empty `build/test-results/test/` means a compile error or no tests ran: "does not build".
- Requires a JDK 17+. Write plain Kotlin/JVM code: no Android SDK or Android-only APIs.

## Stub

- Package `challenge`. Every public declaration has KDoc (`/** ... */`) giving the contract: parameters (`@param`), result (`@return`), edge cases and exceptions.
- Every function body to implement is `TODO()`, e.g. `fun sum(values: List<Int>): Int = TODO()`. Property getters and class members likewise use `TODO()`.
- The stub must compile: declare all types, signatures, interfaces and sealed hierarchies the tests use. Supporting types the task doesn't ask them to write (data classes, interfaces, enums) may be complete.
- Anything time-based takes a `CoroutineScope`, `CoroutineDispatcher` or a clock function (`() -> Long`) as a parameter or constructor argument; never call `System.currentTimeMillis()`, `Thread.sleep` or `Dispatchers.Default` inside the code under test unless it is injectable.

## Tests

- One or more files in `src/test/kotlin/`, `package challenge`, using `kotlin.test` (`@Test`, `assertEquals`, `assertTrue`, `assertFalse`, `assertNull`, `assertFailsWith<T> { }`) on JUnit 5. Put `@Test` on functions in plain classes; no `@BeforeEach` magic beyond what `kotlin.test` provides (`@BeforeTest`, `@AfterTest`).
- Coroutine code is tested with `kotlinx.coroutines.test.runTest { }`; for delays and timeouts use virtual time (`advanceTimeBy`, `advanceUntilIdle`, `currentTime`, `TestScope`, `StandardTestDispatcher`), never real waiting.
- At least 3 tests, ideally 5-8, with descriptive names (backticked sentences are fine). Every test must assert something that the naive solution gets wrong.
- Deterministic: no real time, network, file system or randomness; inject clocks, dispatchers and `Random(seed)`-style sources as parameters.
- A compile error in the tests or stub produces no results, which counts as "does not build".

## Reference and naive

- `.verify/reference/` is a correct, idiomatic solution with the same file names and relative paths as the stub files under `src/main/kotlin/` (only files with code to implement).
- `.verify/naive/` has the same files with the same public API, each function returning `0`, `""`, `emptyList()`, `null`, `false` or echoing its input. It must compile, and must never contain `throw`, `TODO(` or `error(` anywhere (not even in comments or strings). Suspend functions simply return the same trivial value.

## Allowed libraries

- The Kotlin standard library, `kotlinx-coroutines-core` (including `Flow`), and in tests `kotlin.test` and `kotlinx-coroutines-test`. No other dependencies, no Android SDK, no reflection tricks.

## Review: idiomatic Kotlin

- Compare with what the seed says a good solution shows. Look for: `val` over `var`, immutable collections and expression bodies, null safety (`?.`, `?:`, no `!!`), `data class` and `sealed` hierarchies with exhaustive `when`, standard-library functions (`map`, `filter`, `groupBy`, `fold`, `associate`) instead of manual loops, scope functions used sparingly, extension functions, and in coroutine code structured concurrency (`coroutineScope`, no `GlobalScope`, no `runBlocking` in library code) with cancellation respected. Mention a nicer construct only if it is genuinely better.

## Hint examples

- Name the concept or function that unlocks it, e.g. "look at `groupBy` followed by `mapValues`", "a `sealed interface` plus an exhaustive `when` removes the else branch", "`withTimeoutOrNull` returns null instead of throwing".
