local warmup = require("warmup")

describe("warmup", function()
  it("adds two numbers", function()
    assert.equals(5, warmup.add(2, 3))
  end)
end)
