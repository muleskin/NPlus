# n+ (NPlus)

A lightweight text and code editor for Windows, built in C# / WinForms on top of [Scintilla 5](https://www.scintilla.org/) via the [Scintilla5.NET](https://github.com/desjarlais/Scintilla.NET) wrapper. Inspired by Notepad++, with a focus on session persistence, a built-in macro engine, and live file tailing.

The application targets **.NET 8** and ships as a single, self-contained `nplus.exe` — everything is inside that one file: all managed assemblies, Scintilla's native `Scintilla.dll` / `Lexilla.dll`, and a small native launcher. If the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) isn't installed, the launcher offers to download and install it (with your approval) on first run. No installer, no loose DLLs.

## Download

Grab the latest `nplus.exe` from the [GitHub Releases page](https://github.com/muleskin/NPlus/releases/latest). Just download and run — no installation required. If your PC doesn't have the .NET 8 Desktop Runtime yet, nplus will prompt to install it the first time you launch it.

## Features

### Editing
- **Tabbed multi-document interface** with drag-and-drop tab reordering
- **Syntax highlighting** for C#, C/C++, Java, JavaScript/TypeScript, Python, SQL, Visual Basic, VBScript, PowerShell, PHP, HTML, XML/XAML, JSON, and YAML
- **Column / block selection mode** (`Ctrl+Alt+A`) for multi-line edits
- **Word wrap**, indent guides, whitespace and EOL visualization
- **Light** and **Dark Matrix** themes — syntax colors update automatically

### Session & Files
- **Hot exit / session snapshots** — close the app any time; tabs, unsaved changes, window position, and zoom level are restored on next launch
- **Recent files** menu (last 10)
- **Revert to saved** to discard unsaved changes
- **Read-only Hex view** for binary/executable files
- **Read-only fallback** when a file is locked by another process
- **External file change detection** — prompted to reload, close, or track renamed files when they are modified, deleted, or renamed by another program
- **Tab save-status icons** — green dot for saved, orange dot for unsaved changes
- **Encoding support** — ANSI, UTF-8, UTF-8 BOM, UTF-16 BE BOM, UTF-16 LE BOM, with auto-detect and convert-to options

### Macros
- Record keystrokes, navigation, and Find/Replace actions
- Playback once, N times, or to end-of-file (great for log processing)
- **Save, load, and edit macros** step-by-step — saved macros persist between sessions

### Lua Scripting
- **Built-in Lua engine** ([MoonSharp](https://www.moonsharp.org/)) for programmable, document-aware automation — loops, conditionals, regex, and variables that the record/replay macro engine can't express
- **Lua Script Console** — a REPL window (`Ctrl+Enter` to run); globals persist between runs
- **Run Lua Script File...** — execute a `.lua` file against the active document
- User scripts live in `%AppData%\nplus\scripts` (open it from **Scripts → Open Scripts Folder**)
- Each script runs as a **single undo action**, so any change is one `Ctrl+Z` away
- Two globals are exposed to every script:
  - `editor.*` — read/mutate the active document: `GetText` / `SetText` / `AppendText`, `GetSelectedText` / `ReplaceSelection`, `GetLine` / `SetLine` / `GetLineCount` / `GotoLine` (1-based), caret and selection access, `GetFileName` / `GetTitle`
  - `app.*` — `MessageBox`, `Confirm`, `Prompt`, `NewTab`, clipboard access, plus `print(...)` collected into the output

  ```lua
  -- Uppercase the whole document
  editor.SetText(editor.GetText():upper())

  -- Prefix every line with its number
  for i = 1, editor.GetLineCount() do
    editor.SetLine(i, i .. ": " .. editor.GetLine(i))
  end
  ```

### AI Assistant (optional)
- **Off by default** — nothing runs until you turn it on in **AI → Enable AI Assistant** and pick a provider in **AI → Settings**
- **Bring your own backend** — choose **OpenAI (ChatGPT)**, **Azure OpenAI**, **Claude (Anthropic)**, **Gemini (Google)**, **Ollama (local)**, or **Perplexity**; each keeps its own key/model, with a **Test Connection** button in Settings
- **Selection actions** — *Explain*, *Improve / Rewrite*, or a *Custom Prompt* on the current selection (or whole document); results open in an editable dialog with **Replace Selection / New Tab / Copy**
- **Agent mode (Action Protocol)** — let the AI *perform tasks* on the active tab. The model replies with a provider-agnostic JSON action plan (`replaceDocument`, `replaceSelection`, `replaceLine`, `deleteLines`, `replace`, `insert`, …); n+ simulates it, shows a **diff preview**, and applies it **only after you confirm** — as a single undo step. Available from **AI → Run Agent Task on Tab…** or the chat panel's **Agent mode** checkbox. Works on every provider (no native tool-calling required).
- **Chat panel** — a dockable conversational panel with **streaming (token-by-token) responses** that can optionally attach the current document as context (`Ctrl+Enter` to send)
- Keys are stored locally in `%AppData%\nplus\ai_settings.json` and sent only to the provider you select. No provider SDKs are bundled — it's plain HTTPS, so the single-file build is unaffected.

### Find / Replace / Mark / Find in Files
- Normal, Extended (`\n`, `\t`), and Regex modes (`Ctrl+F`, `Ctrl+H`, `Ctrl+B`)
- **Mark** tab highlights all matches and can drop a bookmark on every matching line
- **Find in Files** (`Ctrl+Shift+F`) — search across all files in a directory with file-type filters, sub-folder recursion, and hidden-folder inclusion
- **Replace in Files** — bulk find-and-replace across matching files on disk
- Results appear in a dockable bottom panel; double-click any hit to open the file and jump to the line
- Per-session search history dropdowns (up to 20 entries)

### Bookmarks & Line Operations
- Toggle bookmarks (`Ctrl+F2`), navigate with `F2` / `Shift+F2`
- Copy, cut, or delete all bookmarked lines
- Duplicate, reverse, or randomize line order
- Sort lines: lexicographic, locale, integer, decimal (comma/dot), by length — ascending or descending, with case-insensitive options
- Split lines (`Ctrl+I`), join lines (`Ctrl+J`), shift lines up/down (`Ctrl+Shift+Up/Down`)
- Remove duplicate lines, remove empty lines, insert blank lines above/below
- **Blank operations** — trim leading/trailing/both whitespace, EOL to space, tabs ↔ spaces conversion

### JSON Tools
- **Pretty-print / format** dense or single-line JSON
- **Visual JSON tree explorer** in a dockable, resizable side panel

### Live File Monitoring (Tail)
- Toggle "Live" mode to auto-reload and auto-scroll on external file changes — tail rolling log files without leaving the editor

### Zoom
- `F11` / `F12` / `Ctrl+0` to zoom the entire UI (menu, toolbar, status bar, editor)
- `Ctrl+Mouse Wheel` to zoom only the current editor tab
- Zoom level persists between sessions

## Keyboard Shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+F` / `Ctrl+H` / `Ctrl+B` | Find / Replace / Mark |
| `F3` | Find Next |
| `Ctrl+Shift+F` | Find in Files |
| `Ctrl+Alt+A` | Toggle column selection mode |
| `Ctrl+F2` | Toggle bookmark on current line |
| `F2` / `Shift+F2` | Next / previous bookmark |
| `Ctrl+J` | Join selected lines |
| `Ctrl+Shift+Up/Down` | Move line up / down |
| `Ctrl+Shift+P` | Playback active macro |
| `F11` / `F12` / `Ctrl+0` | Zoom in / out / reset |

## Building from Source

Requirements:
- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio C++ build tools ("Desktop development with C++") — required to compile the Native AOT launcher.

For day-to-day development of the editor itself (no launcher), just build/run the app project:

```
dotnet build -c Release
```

To produce the distributable single-file build, run the build script from the repo root:

```
.\build.ps1
```

This writes a single `dist\nplus.exe`. The build has two parts wired together by the script:

1. **The app** (`nplus.csproj`) publishes as a framework-dependent single file. All managed assemblies are bundled by .NET's single-file publish; Scintilla's native `Scintilla.dll` / `Lexilla.dll` are carried as embedded resources and extracted to `%LOCALAPPDATA%\nplus\native` at startup (Scintilla.NET loads them from disk, so they can't live inside the bundle directly).
2. **The launcher** (`Bootstrap\nplus.bootstrap.csproj`) is a Native AOT exe — it runs without any installed runtime. It embeds the published app **GZip-compressed** (shaving ~1.8 MB off the final exe), checks for the .NET 8 Desktop Runtime, downloads + installs it (with the user's consent) if missing, then decompresses + extracts and launches the app.

The result is **framework-dependent** (the app needs the .NET 8 Desktop Runtime), but the launcher bootstraps that runtime automatically, so the end user only ever needs the one `nplus.exe`.

### Project layout

| Path | What it is |
| --- | --- |
| `nplus.csproj`, `*.cs` | The WinForms editor application. |
| `LuaScripting.cs` | The MoonSharp Lua engine: script host, the `editor`/`app` APIs, and the Script Console. |
| `AiAssistant.cs` | The optional AI assistant: multi-provider HTTP client, settings/result dialogs, and the chat panel. |
| `AiActions.cs` | The AI Action Protocol: JSON action parsing, edit simulation, line-diff, and the preview/confirm dialog. |
| `native\win-x64\` | Scintilla / Lexilla native DLLs, embedded into the app exe as resources. |
| `Bootstrap\nplus.bootstrap.csproj` | The Native AOT launcher (`Program.cs`). |
| `Bootstrap\RuntimeInstaller.cs` | Runtime detection / download / install logic, kept separate and UI-free. |
| `build.ps1` | Builds the whole thing into `dist\nplus.exe`. |

### Dependencies
- [Scintilla5.NET](https://www.nuget.org/packages/Scintilla5.NET) 6.1.2 — editor control (Scintilla 5.5.5 + Lexilla 5.4.3)
- [Be.Windows.Forms.HexBox.Net5](https://www.nuget.org/packages/Be.Windows.Forms.HexBox.Net5) 1.8.0 — read-only hex view
- [MoonSharp](https://www.nuget.org/packages/MoonSharp) 2.0.0 — pure-managed Lua interpreter for the scripting engine (no native deps, so it folds into the single-file build)
- `System.Text.Json` (built into .NET 8) for JSON formatting / tree view

## License

MIT License

Copyright (c) 2026 Mule Skinner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.