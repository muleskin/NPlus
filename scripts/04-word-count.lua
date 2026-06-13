-- Read-only example: report line / word / character counts without changing the
-- document. Shows app.MessageBox and the GetTitle helper.

local text = editor.GetText()

local chars = #text
local words = 0
for _ in text:gmatch("%S+") do words = words + 1 end
local lines = editor.GetLineCount()

app.MessageBox(
  (editor.GetTitle() or "document") .. "\n\n" ..
  "Lines:      " .. lines .. "\n" ..
  "Words:      " .. words .. "\n" ..
  "Characters: " .. chars
)
