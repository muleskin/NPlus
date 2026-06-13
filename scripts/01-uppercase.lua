-- Uppercase the current selection, or the whole document if nothing is selected.
-- Run via  Scripts -> Run Lua Script File...  (or paste into the Script Console).

local sel = editor.GetSelectedText()

if sel ~= "" then
  editor.ReplaceSelection(sel:upper())
else
  editor.SetText(editor.GetText():upper())
end
