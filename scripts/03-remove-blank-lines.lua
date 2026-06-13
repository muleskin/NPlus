-- Remove blank lines (empty or whitespace-only) from the active document.
-- Rebuilds the text in one pass and writes it back with SetText.

local kept = {}
local removed = 0

for line in (editor.GetText() .. "\n"):gmatch("(.-)\n") do
  if line:match("%S") then          -- keeps any line with a non-space character
    kept[#kept + 1] = line
  else
    removed = removed + 1
  end
end

editor.SetText(table.concat(kept, "\n"))
app.MessageBox("Removed " .. removed .. " blank line(s).")
