-- Minimal test runner (Lua 5.1 / LuaJIT / `nvim -l`). Run from the challenge root:
--   luajit spec/runner.lua
-- Loads spec/challenge_spec.lua and writes results.xml (JUnit). Always exits 0;
-- a spec that fails to load writes no results.xml.

package.path = "./src/?.lua;./src/?/init.lua;" .. package.path

local builtin_assert = assert
local stack, tests = {}, {}

local function show(v)
  if type(v) == "string" then return string.format("%q", v) end
  if type(v) ~= "table" then return tostring(v) end
  local keys = {}
  for k in pairs(v) do keys[#keys + 1] = k end
  table.sort(keys, function(a, b)
    local ta, tb = type(a), type(b)
    if ta ~= tb then return ta < tb end
    if ta == "number" or ta == "string" then return a < b end
    return tostring(a) < tostring(b)
  end)
  local parts = {}
  for _, k in ipairs(keys) do
    parts[#parts + 1] = "[" .. show(k) .. "]=" .. show(v[k])
  end
  return "{" .. table.concat(parts, ", ") .. "}"
end

local function same(a, b, seen)
  if a == b then return true end
  if type(a) ~= "table" or type(b) ~= "table" then return false end
  seen = seen or {}
  if seen[a] == b then return true end
  seen[a] = b
  for k, v in pairs(a) do
    if not same(v, b[k], seen) then return false end
  end
  for k in pairs(b) do
    if a[k] == nil then return false end
  end
  return true
end

local asserts = {}

function asserts.equals(expected, actual)
  if expected ~= actual then
    error("expected " .. show(expected) .. " but got " .. show(actual), 2)
  end
end

function asserts.same(expected, actual)
  if not same(expected, actual) then
    error("expected " .. show(expected) .. " but got " .. show(actual), 2)
  end
end

function asserts.is_true(v)
  if v ~= true then error("expected true but got " .. show(v), 2) end
end

function asserts.is_false(v)
  if v ~= false then error("expected false but got " .. show(v), 2) end
end

function asserts.is_nil(v)
  if v ~= nil then error("expected nil but got " .. show(v), 2) end
end

function asserts.has_error(fn, substring)
  local ok, err = pcall(fn)
  if ok then error("expected an error but none was raised", 2) end
  if substring ~= nil and not string.find(tostring(err), substring, 1, true) then
    error("expected error containing " .. show(substring) .. " but got " .. show(tostring(err)), 2)
  end
end

-- `assert` stays callable like the builtin and gains the helpers above.
assert = setmetatable(asserts, {
  __call = function(_, ...) return builtin_assert(...) end,
})

function describe(name, fn)
  stack[#stack + 1] = name
  local ok, err = pcall(fn)
  stack[#stack] = nil
  if not ok then error("describe '" .. name .. "' failed: " .. tostring(err), 0) end
end

function it(name, fn)
  tests[#tests + 1] = { classname = table.concat(stack, "."), name = name, fn = fn }
end

local function escape(s)
  s = tostring(s)
  s = s:gsub("[\1-\8\11\12\14-\31]", "?")
  s = s:gsub("&", "&amp;"):gsub("<", "&lt;"):gsub(">", "&gt;"):gsub('"', "&quot;")
  return s
end

local ok, err = pcall(dofile, "spec/challenge_spec.lua")
if not ok then
  io.stderr:write("Could not load spec/challenge_spec.lua: " .. tostring(err) .. "\n")
  os.exit(0)
end

local failures = 0
local out = {}
for _, t in ipairs(tests) do
  local passed, msg = pcall(t.fn)
  local head = '    <testcase classname="' .. escape(t.classname) .. '" name="' .. escape(t.name) .. '"'
  if passed then
    out[#out + 1] = head .. "/>"
  else
    failures = failures + 1
    msg = tostring(msg)
    out[#out + 1] = head .. '>\n      <failure message="' .. escape(msg) .. '">' .. escape(msg) .. "</failure>\n    </testcase>"
  end
end

local xml = '<?xml version="1.0" encoding="UTF-8"?>\n'
  .. '<testsuites>\n  <testsuite name="lua" tests="' .. #tests .. '" failures="' .. failures .. '">\n'
  .. table.concat(out, "\n") .. (#out > 0 and "\n" or "")
  .. "  </testsuite>\n</testsuites>\n"

local file = io.open("results.xml", "w")
if file then
  file:write(xml)
  file:close()
else
  io.stderr:write("Could not write results.xml\n")
end
print(string.format("%d tests, %d failures", #tests, failures))
os.exit(0)
