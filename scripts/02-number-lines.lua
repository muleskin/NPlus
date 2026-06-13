-- Prefix every line with its line number, right-aligned and padded.
-- Demonstrates the 1-based line API (GetLineCount / GetLine / SetLine).
-- The whole script is one undo step, so Ctrl+Z reverts it all at once.

local count = editor.GetLineCount()
local width = #tostring(count)             -- digits needed for the largest number

for i = 1, count do
  local num = string.format("%" .. width .. "d", i)
  editor.SetLine(i, num .. ": " .. editor.GetLine(i))
end

app.Print("Numbered " .. count .. " lines.")
