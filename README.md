# n+ (NPlus)

A lightweight, single-executable text and code editor for Windows, built in C# / WinForms on top of [ScintillaNET](https://github.com/jacobslusser/ScintillaNET). Inspired by Notepad++, with a focus on session persistence, a built-in macro engine, and live file tailing.

The entire application ships as a single `nplus.exe` (dependencies merged via Costura.Fody) — no installer, no runtime to deploy beyond .NET Framework 4.7.2.

## Download

Grab the latest `nplus.exe` from the [GitHub Releases page](https://github.com/muleskin/NPlus/releases/latest). Just download and run — no installation required.

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
- Visual Studio 2019+ (or MSBuild 15+)
- .NET Framework 4.7.2 developer pack

```
nuget restore nplus.slnx
msbuild nplus.csproj /p:Configuration=Release
```

The build produces a self-contained `nplus.exe` in `bin\Release\` — Costura.Fody embeds all referenced assemblies into the executable.

### Dependencies
- [ScintillaNET](https://www.nuget.org/packages/jacobslusser.ScintillaNET) 3.6.3 — editor control
- [Costura.Fody](https://www.nuget.org/packages/Costura.Fody) 6.0.0 — embedded-assembly packaging
- `System.Text.Json` for JSON formatting / tree view

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