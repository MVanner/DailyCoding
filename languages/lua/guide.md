# Lua language guide

Language-specific rules for the daily challenge. The skills reference these sections by heading.

## Layout

- Run the tests: `luajit spec/runner.lua` from the challenge folder (fallbacks: `lua spec/runner.lua`, `nvim -l spec/runner.lua`). It prints a summary line such as `3 tests, 1 failures` to stdout and writes `results.xml`.
- `src/` holds the stub modules (`*.lua`), `spec/challenge_spec.lua` holds the tests, `spec/runner.lua` is the provided test runner (never edit it).
- Run with `luajit spec/runner.lua` (fallbacks: `lua spec/runner.lua`, `nvim -l spec/runner.lua`) from the challenge folder; results go to `results.xml`.
- The runner puts `./src/?.lua` and `./src/?/init.lua` on `package.path`, so tests use `require("calc")` for `src/calc.lua`, and `require("pkg.mod")` maps to `src/pkg/mod.lua`.
- Target Lua 5.1 / LuaJIT semantics: no `goto`, no `//`, no bitwise operators, no integer subtype (`math.floor` for integer division). When unpacking, write `local unpack = table.unpack or unpack` (the `lua` fallback may be 5.2+). Never use `loadstring`, `setfenv`/`getfenv` or `module`.

## Stub

- Each module is `local M = {}` ... `return M`, with LuaDoc-style `---` comments giving signature, parameters and behaviour for every function.
- Every function body to implement is `error("not implemented")`.
- Neovim challenges take a fake `vim` table as a parameter (e.g. `M.setup(vim, opts)`); the fake is defined inside `spec/challenge_spec.lua` and passed into the module's functions; never call the real `vim` global. Game challenges are pure logic with `dt` passed in; no real clock, no rendering.
- Supporting constants or helper tables the task doesn't ask them to write may be complete.

## Tests

- `spec/challenge_spec.lua` only, using the provided runner API: `describe(name, fn)`, `it(name, fn)`, `assert.equals(expected, actual)`, `assert.same(expected, actual)` (deep table compare), `assert.is_true(v)`, `assert.is_false(v)`, `assert.is_nil(v)`, `assert.has_error(fn, [substring])`.
- Every `it` must sit inside a `describe`.
- At least 3 tests, ideally 5-8, with names that describe behaviour. Every test must assert something that the naive solution gets wrong.
- Deterministic: no real time, network, file system or randomness; pass clocks, `dt`, random functions and a fake `vim` in as parameters.
- A syntax or load error in the spec produces no `results.xml`, which counts as "does not build".

## Reference and naive

- `.verify/reference/` is a correct, idiomatic solution with the same file names and relative paths as the stub files in `src/` (only files with code to implement).
- `.verify/naive/` has the same files with the same public functions, each returning `nil`, `0`, `""`, `{}` or echoing its input. It must load, and must not contain `error(` or `assert(` at all.

## Allowed libraries

- The Lua standard library only (`string`, `table`, `math`); never use `os.time`, `os.clock` or `math.random` in tests. No LuaRocks packages, no `vim` API.

## Review: idiomatic Lua

- Compare with what the seed says a good solution shows. Look for: local functions and variables (no accidental globals), `ipairs` for arrays and `pairs` for maps, `table.concat` instead of repeated string concatenation in loops, `#` only on proper sequences, method syntax `obj:method()` with metatables `__index`, and `select("#", ...)` / `{...}` for varargs. Mention a nicer construct only if it is genuinely better.

## Hint examples

- Name the concept or function that unlocks it, e.g. "look at `string.gmatch` with a capture", "a metatable with `__index` gives you inheritance", "`table.sort` takes a comparison function".
