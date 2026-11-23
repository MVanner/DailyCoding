# C# language guide

Language-specific rules for the daily challenge. The skills reference these sections by heading.

## Layout

- Run the tests: `dotnet test` in the challenge folder, or run `Challenge.Tests` in Rider.
- `Challenge/` holds the stub (`*.cs`), `Challenge.Tests/` holds the tests, `Challenge.slnx` ties them together.
- The scaffolded template targets the developer's .NET SDK and needs nothing outside the .NET base library.

## Stub

- Namespace `Challenge`, the full public signatures with XML doc comments, every body `throw new NotImplementedException();`.
- Supporting types the task doesn't ask them to write (records, interfaces) may be complete.

## Tests

- MSTest (`[TestClass]`, `[TestMethod]`, `Assert.*`), namespace `Challenge.Tests`, at least 3 tests, ideally 5-8, with clear names that describe behaviour.
- Tests must be deterministic: no real time, network, file system or randomness; pass clocks/delays/data in as parameters or small interfaces.

## Reference and naive

- `.verify/reference/` is a correct, idiomatic solution with the same file names and relative paths as the stub files in `Challenge/` (only the files with code to implement).
- `.verify/naive/` has the same files with the same public signatures, but each method returns `default`, an empty collection/string, or echoes its input. It must compile and must not contain `throw` at all.

## Allowed libraries

- The .NET base library only.

## Review: idiomatic C#

- Compare with what the seed says a good solution shows; mention a nicer construct only if it is genuinely better.

## Hint examples

- Name the concept or API that unlocks it, e.g. "look at `Task.WhenAll`", "a `switch` expression with a property pattern".
