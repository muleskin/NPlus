using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MoonSharp.Interpreter;
using ScintillaNET;

namespace nplus
{
    // ============================================================================
    //  Lua scripting engine (MoonSharp).
    //
    //  Two objects are exposed to every script:
    //    editor.*  -> read / mutate the active document (see LuaEditorApi)
    //    app.*     -> dialogs, prompts, new tabs, status (see LuaAppApi)
    //  plus a redirected print() that collects output for display.
    //
    //  Scripts run synchronously on the UI thread, so they may touch Scintilla
    //  directly. A whole script is wrapped in one undo action.
    // ============================================================================

    /// <summary>
    /// Owns a MoonSharp <see cref="Script"/> and runs Lua source against the editor.
    /// One host = one persistent Lua state (globals survive between Run calls), which
    /// makes the Script Console behave like a REPL. The file runner uses a throwaway
    /// host per execution for a clean slate.
    /// </summary>
    internal sealed class LuaScriptHost
    {
        private static bool _typesRegistered;

        private readonly EditorForm _form;
        private readonly Script _script;
        private readonly StringBuilder _output = new StringBuilder();

        internal LuaScriptHost(EditorForm form)
        {
            _form = form;

            // UserData registration is process-global in MoonSharp; do it once.
            if (!_typesRegistered)
            {
                UserData.RegisterType<LuaEditorApi>();
                UserData.RegisterType<LuaAppApi>();
                _typesRegistered = true;
            }

            // Preset_SoftSandbox deliberately EXCLUDES the OS_System and IO modules,
            // so scripts cannot call os.execute (spawn processes), os.remove/os.rename,
            // or io.* (read/write arbitrary files). Without this, running an untrusted
            // .lua file would be a one-click arbitrary-code-execution hole. String,
            // table, math, os.time/date, and coroutine remain available for text work.
            _script = new Script(CoreModules.Preset_SoftSandbox);
            _script.Globals["editor"] = new LuaEditorApi(_form);
            _script.Globals["app"] = new LuaAppApi(_form, _output);

            // Redirect print()/io.write so output lands in our buffer, not the void.
            _script.Options.DebugPrint = s => _output.Append(s).Append('\n');
        }

        /// <summary>
        /// Executes <paramref name="code"/>. Returns the combined print()/error text.
        /// <paramref name="success"/> is false if the script raised an error.
        /// </summary>
        internal string Run(string code, out bool success)
        {
            _output.Clear();
            Scintilla active = _form.GetActiveEditor();
            bool undo = active != null;

            try
            {
                if (undo) active.BeginUndoAction();
                DynValue result = _script.DoString(code, codeFriendlyName: "script");

                // Surface a non-nil return value (handy for quick console expressions).
                if (result != null && result.Type != DataType.Void && result.Type != DataType.Nil)
                    _output.Append("=> ").Append(result.ToPrintString()).Append('\n');

                success = true;
            }
            catch (InterpreterException ex)
            {
                // DecoratedMessage carries the Lua line number; fall back to Message.
                _output.Append("Lua error: ")
                       .Append(ex.DecoratedMessage ?? ex.Message)
                       .Append('\n');
                success = false;
            }
            catch (Exception ex)
            {
                _output.Append("Error: ").Append(ex.Message).Append('\n');
                success = false;
            }
            finally
            {
                if (undo) active.EndUndoAction();
            }

            return _output.ToString();
        }
    }

    /// <summary>
    /// The <c>editor</c> global. All line numbers are 1-based for script authors;
    /// Scintilla's native 0-based indices stay hidden here.
    /// </summary>
    [MoonSharpUserData]
    internal sealed class LuaEditorApi
    {
        private readonly EditorForm _form;

        internal LuaEditorApi(EditorForm form) { _form = form; }

        private Scintilla E()
        {
            Scintilla e = _form.GetActiveEditor();
            if (e == null) throw new ScriptRuntimeException("No active editor tab.");
            return e;
        }

        // --- Whole-document text -------------------------------------------------
        public string GetText() => E().Text;
        public void SetText(string text) => E().Text = text ?? "";
        public void AppendText(string text) => E().AppendText(text ?? "");
        public int Length() => E().TextLength;

        // --- Selection -----------------------------------------------------------
        public string GetSelectedText() => E().SelectedText;
        public void ReplaceSelection(string text) => E().ReplaceSelection(text ?? "");
        public int GetSelectionStart() => E().SelectionStart;
        public int GetSelectionEnd() => E().SelectionEnd;
        public void SetSelection(int start, int end)
        {
            Scintilla e = E();
            e.SetSelection(Clamp(start, e.TextLength), Clamp(end, e.TextLength));
        }

        // --- Caret / insertion ---------------------------------------------------
        public int GetCaret() => E().CurrentPosition;
        public void SetCaret(int pos)
        {
            Scintilla e = E();
            e.GotoPosition(Clamp(pos, e.TextLength));
        }
        public void Insert(string text)
        {
            Scintilla e = E();
            e.InsertText(e.CurrentPosition, text ?? "");
        }
        public void InsertAt(int pos, string text)
        {
            Scintilla e = E();
            e.InsertText(Clamp(pos, e.TextLength), text ?? "");
        }

        // --- Lines (1-based) -----------------------------------------------------
        public int GetLineCount() => E().Lines.Count;

        public int GetCurrentLine() => E().CurrentLine + 1;

        public void GotoLine(int line)
        {
            Scintilla e = E();
            int idx = ClampLine(line, e.Lines.Count);
            e.Lines[idx].Goto();
            e.Lines[idx].EnsureVisible();
        }

        /// <summary>Returns a line's text WITHOUT its trailing EOL.</summary>
        public string GetLine(int line)
        {
            Scintilla e = E();
            string raw = e.Lines[ClampLine(line, e.Lines.Count)].Text ?? "";
            return raw.TrimEnd('\r', '\n');
        }

        /// <summary>Replaces a line's content, preserving its existing line ending.</summary>
        public void SetLine(int line, string text)
        {
            Scintilla e = E();
            Line ln = e.Lines[ClampLine(line, e.Lines.Count)];
            string raw = ln.Text ?? "";
            string eol = raw.EndsWith("\r\n") ? "\r\n"
                       : raw.EndsWith("\n") ? "\n"
                       : raw.EndsWith("\r") ? "\r" : "";
            e.TargetStart = ln.Position;
            e.TargetEnd = ln.EndPosition;
            e.ReplaceTarget((text ?? "") + eol);
        }

        // --- File info -----------------------------------------------------------
        public string GetFileName() => _form.GetActiveFilePath() ?? "";
        public string GetTitle() => _form.GetActiveTitle() ?? "";

        private static int Clamp(int v, int max) => v < 0 ? 0 : (v > max ? max : v);

        // 1-based -> 0-based, clamped to valid line range.
        private static int ClampLine(int line, int count)
        {
            int idx = line - 1;
            if (idx < 0) idx = 0;
            if (idx > count - 1) idx = count - 1;
            return idx;
        }
    }

    /// <summary>
    /// The <c>app</c> global: dialogs, prompts, new tabs, and status-bar access.
    /// </summary>
    [MoonSharpUserData]
    internal sealed class LuaAppApi
    {
        private readonly EditorForm _form;
        private readonly StringBuilder _output;

        internal LuaAppApi(EditorForm form, StringBuilder output)
        {
            _form = form;
            _output = output;
        }

        /// <summary>Same as print(), but explicit — appends a line to script output.</summary>
        public void Print(string text) => _output.Append(text ?? "").Append('\n');

        public void MessageBox(string text)
            => System.Windows.Forms.MessageBox.Show(text ?? "", "n+ Script",
                   MessageBoxButtons.OK, MessageBoxIcon.Information);

        /// <summary>true / false via a Yes/No dialog.</summary>
        public bool Confirm(string text)
            => System.Windows.Forms.MessageBox.Show(text ?? "", "n+ Script",
                   MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        /// <summary>Single-line input box. Returns the entered text, or nil if cancelled.</summary>
        public string Prompt(string message, string defaultValue)
        {
            return LuaInputBox.Show(message ?? "", "n+ Script", defaultValue ?? "");
        }

        /// <summary>Opens the given text in a brand-new editor tab.</summary>
        public void NewTab(string title, string text)
            => _form.NewTabWithText(string.IsNullOrEmpty(title) ? "script output" : title, text ?? "");

        public string GetClipboard()
            => Clipboard.ContainsText() ? Clipboard.GetText() : "";

        public void SetClipboard(string text)
        {
            if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
        }
    }

    /// <summary>Minimal single-line input dialog (returns null on cancel).</summary>
    internal static class LuaInputBox
    {
        internal static string Show(string prompt, string title, string defaultValue)
        {
            using var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(420, 130),
                ShowInTaskbar = false
            };

            var label = new Label { Left = 12, Top = 12, Width = 396, Height = 40, Text = prompt };
            var input = new TextBox { Left = 12, Top = 56, Width = 396, Text = defaultValue };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 252, Top = 92, Width = 75 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 333, Top = 92, Width = 75 };

            form.Controls.AddRange(new Control[] { label, input, ok, cancel });
            form.AcceptButton = ok;
            form.CancelButton = cancel;

            return form.ShowDialog() == DialogResult.OK ? input.Text : null;
        }
    }

    /// <summary>
    /// A small REPL-style window: write Lua on top, hit Run (or Ctrl+Enter), see
    /// print()/error output below. Keeps one persistent <see cref="LuaScriptHost"/>
    /// so variables and functions survive between runs.
    /// </summary>
    internal sealed class LuaScriptConsole : Form
    {
        private readonly LuaScriptHost _host;
        private readonly TextBox _input;
        private readonly TextBox _output;

        internal LuaScriptConsole(EditorForm form)
        {
            _host = new LuaScriptHost(form);

            Text = "n+ — Lua Script Console";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 480);
            MinimumSize = new Size(420, 320);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 300
            };

            _input = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsTab = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 10f),
                Text = "-- Lua. 'editor' and 'app' are available. Ctrl+Enter to run.\n"
                     + "-- e.g.  editor.SetText(editor.GetText():upper())\n"
            };

            _output = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.Gainsboro,
                Font = new Font(FontFamily.GenericMonospace, 10f)
            };

            split.Panel1.Controls.Add(_input);
            split.Panel2.Controls.Add(_output);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            var run = new Button { Text = "Run (Ctrl+Enter)", Width = 140, Left = 8, Top = 6, Height = 28 };
            var clear = new Button { Text = "Clear Output", Width = 110, Left = 156, Top = 6, Height = 28 };
            run.Click += (s, e) => RunScript();
            clear.Click += (s, e) => _output.Clear();
            bar.Controls.AddRange(new Control[] { run, clear });

            Controls.Add(split);
            Controls.Add(bar);

            _input.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    RunScript();
                    e.SuppressKeyPress = true;
                }
            };
        }

        private void RunScript()
        {
            string code = _input.Text;
            if (string.IsNullOrWhiteSpace(code)) return;

            string result = _host.Run(code, out bool _);
            if (string.IsNullOrEmpty(result)) result = "(no output)\n";
            // Newest run on top.
            _output.Text = result + (_output.TextLength > 0 ? "\n" + new string('-', 40) + "\n" + _output.Text : "");
            _output.SelectionStart = 0;
            _output.ScrollToCaret();
        }
    }
}
