using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace nplus
{
    // ============================================================================
    //  AI Action Protocol
    //
    //  Lets the assistant *act* on the active editor tab — but only through a
    //  preview-and-confirm gate. Because not every provider supports native tool
    //  calling, the protocol is plain JSON returned in the model's text response,
    //  so it works identically across OpenAI / Azure / Claude / Gemini / Ollama /
    //  Perplexity.
    //
    //  The model is asked to reply with:
    //      { "summary": "...", "actions": [ { "type": "...", ... }, ... ] }
    //  We parse it, SIMULATE the actions into the resulting document text, show a
    //  diff preview, and apply only on the user's confirmation (as one undo step).
    // ============================================================================

    internal sealed class AiAction
    {
        public string Type { get; set; }
        public string Text { get; set; }
        public int Line { get; set; }
        public int From { get; set; }
        public int To { get; set; }
        public string Find { get; set; }
        public string Replace { get; set; }
        public bool All { get; set; }
    }

    internal static class AiActionProtocol
    {
        // Sent as the system prompt for agent tasks. Deliberately strict so even
        // non-tool-calling models return parseable JSON.
        public const string SystemPrompt =
@"You are an automated editing agent inside the n+ text editor. You can modify the
user's ACTIVE document by returning edit actions.

Respond with ONE JSON object and NOTHING else — no prose, no markdown, no code
fences. Shape:

{""summary"":""<one short line describing the change>"",""actions"":[ <action>, ... ]}

Each <action> is one of:
{""type"":""replaceDocument"",""text"":""<entire new document>""}
{""type"":""replaceSelection"",""text"":""<replacement for the selected text>""}
{""type"":""insertAtCaret"",""text"":""...""}
{""type"":""appendText"",""text"":""...""}
{""type"":""replaceLine"",""line"":<1-based line number>,""text"":""...""}
{""type"":""deleteLines"",""from"":<1-based>,""to"":<1-based inclusive>}
{""type"":""replace"",""find"":""..."",""replace"":""..."",""all"":true}
{""type"":""message"",""text"":""<answer or explanation; makes no edit>""}

Rules:
- Output must be valid JSON, parseable as-is. Escape newlines inside strings as \n.
- Do NOT wrap the JSON in ``` fences.
- If the user only asks a question, return a single ""message"" action.
- Prefer ""replaceDocument"" for whole-file rewrites; prefer ""replaceSelection""
  when the user has text selected and wants it changed.
- Never include commentary outside the JSON object.";

        /// <summary>
        /// Parses a model response into a summary + action list. Tolerates code fences
        /// and surrounding prose by extracting the outermost JSON object/array.
        /// </summary>
        public static bool TryParse(string response, out string summary, out List<AiAction> actions, out string error)
        {
            summary = "";
            actions = new List<AiAction>();
            error = null;

            string json = ExtractJson(response);
            if (json == null) { error = "No JSON object found in the response."; return false; }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                JsonElement actionsEl;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    actionsEl = root;
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String)
                        summary = s.GetString();
                    if (!root.TryGetProperty("actions", out actionsEl) || actionsEl.ValueKind != JsonValueKind.Array)
                    {
                        error = "Response JSON has no \"actions\" array.";
                        return false;
                    }
                }
                else
                {
                    error = "Response is not a JSON object or array.";
                    return false;
                }

                foreach (JsonElement el in actionsEl.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var a = new AiAction
                    {
                        Type = GetStr(el, "type"),
                        Text = GetStr(el, "text"),
                        Find = GetStr(el, "find"),
                        Replace = GetStr(el, "replace"),
                        Line = GetInt(el, "line"),
                        From = GetInt(el, "from"),
                        To = GetInt(el, "to"),
                        All = GetBool(el, "all"),
                    };
                    if (!string.IsNullOrEmpty(a.Type)) actions.Add(a);
                }

                if (actions.Count == 0) { error = "No valid actions in the response."; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not parse JSON: " + ex.Message;
                return false;
            }
        }

        /// <summary>Applies the actions to a copy of the text and returns the result.</summary>
        public static string Simulate(string text, int selStart, int selEnd, int caret,
            IList<AiAction> actions, out List<string> messages)
        {
            messages = new List<string>();
            string eol = text.Contains("\r\n") ? "\r\n" : "\n";

            foreach (AiAction a in actions)
            {
                switch ((a.Type ?? "").ToLowerInvariant())
                {
                    case "replacedocument":
                    case "settext":
                    case "replaceall":
                        text = a.Text ?? "";
                        caret = text.Length; selStart = selEnd = caret;
                        break;

                    case "replaceselection":
                    {
                        int s = Clamp(selStart, text.Length), e = Clamp(selEnd, text.Length);
                        if (e < s) { int t = s; s = e; e = t; }
                        string ins = a.Text ?? "";
                        text = text.Substring(0, s) + ins + text.Substring(e);
                        selStart = s; selEnd = caret = s + ins.Length;
                        break;
                    }

                    case "insertatcaret":
                    {
                        int c = Clamp(caret, text.Length);
                        string ins = a.Text ?? "";
                        text = text.Substring(0, c) + ins + text.Substring(c);
                        caret = c + ins.Length;
                        break;
                    }

                    case "appendtext":
                        text += a.Text ?? "";
                        caret = text.Length;
                        break;

                    case "replaceline":
                    {
                        var lines = new List<string>(text.Split(new[] { eol }, StringSplitOptions.None));
                        int idx = a.Line - 1;
                        if (idx >= 0 && idx < lines.Count) { lines[idx] = a.Text ?? ""; text = string.Join(eol, lines); }
                        break;
                    }

                    case "deletelines":
                    {
                        var lines = new List<string>(text.Split(new[] { eol }, StringSplitOptions.None));
                        int from = Math.Max(1, a.From) - 1;
                        int to = Math.Min(lines.Count, a.To) - 1;
                        if (from >= 0 && to >= from && from < lines.Count)
                        {
                            lines.RemoveRange(from, to - from + 1);
                            text = string.Join(eol, lines);
                        }
                        break;
                    }

                    case "replace":
                        if (!string.IsNullOrEmpty(a.Find))
                            text = a.All ? text.Replace(a.Find, a.Replace ?? "")
                                         : ReplaceFirst(text, a.Find, a.Replace ?? "");
                        break;

                    case "message":
                        if (!string.IsNullOrEmpty(a.Text)) messages.Add(a.Text);
                        break;
                }
            }
            return text;
        }

        // --- helpers -------------------------------------------------------------

        private static string ExtractJson(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            // Strip a leading ```json / ``` fence if present.
            int fence = s.IndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                int nl = s.IndexOf('\n', fence);
                int close = s.LastIndexOf("```", StringComparison.Ordinal);
                if (nl >= 0 && close > nl) s = s.Substring(nl + 1, close - nl - 1);
            }
            int objStart = s.IndexOf('{');
            int arrStart = s.IndexOf('[');
            int start = (objStart < 0) ? arrStart : (arrStart < 0 ? objStart : Math.Min(objStart, arrStart));
            if (start < 0) return null;
            char open = s[start];
            char closeCh = open == '{' ? '}' : ']';
            int end = s.LastIndexOf(closeCh);
            if (end <= start) return null;
            return s.Substring(start, end - start + 1);
        }

        private static string GetStr(JsonElement el, string name)
            => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int GetInt(JsonElement el, string name)
            => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;

        private static bool GetBool(JsonElement el, string name)
            => el.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True);

        private static int Clamp(int v, int max) => v < 0 ? 0 : (v > max ? max : v);

        private static string ReplaceFirst(string text, string find, string repl)
        {
            int i = text.IndexOf(find, StringComparison.Ordinal);
            return i < 0 ? text : text.Substring(0, i) + repl + text.Substring(i + find.Length);
        }
    }

    // --- Line diff for the preview ------------------------------------------------

    internal enum DiffTag { Context, Added, Removed, Marker }

    internal sealed class DiffLine
    {
        public DiffTag Tag;
        public string Text;
        public DiffLine(DiffTag tag, string text) { Tag = tag; Text = text; }
    }

    internal static class AiDiff
    {
        /// <summary>LCS line diff, with long unchanged runs collapsed for readability.</summary>
        public static List<DiffLine> Build(string before, string after)
        {
            string[] a = Norm(before).Split('\n');
            string[] b = Norm(after).Split('\n');

            // Guard against quadratic blow-up on huge files.
            if ((long)a.Length * b.Length > 4_000_000 || a.Length > 6000 || b.Length > 6000)
            {
                return new List<DiffLine>
                {
                    new DiffLine(DiffTag.Marker,
                        $"Large change — detailed diff skipped ({a.Length} lines → {b.Length} lines)."),
                };
            }

            int[,] dp = new int[a.Length + 1, b.Length + 1];
            for (int i = a.Length - 1; i >= 0; i--)
                for (int j = b.Length - 1; j >= 0; j--)
                    dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            var raw = new List<DiffLine>();
            int x = 0, y = 0;
            while (x < a.Length && y < b.Length)
            {
                if (a[x] == b[y]) { raw.Add(new DiffLine(DiffTag.Context, a[x])); x++; y++; }
                else if (dp[x + 1, y] >= dp[x, y + 1]) { raw.Add(new DiffLine(DiffTag.Removed, a[x])); x++; }
                else { raw.Add(new DiffLine(DiffTag.Added, b[y])); y++; }
            }
            while (x < a.Length) raw.Add(new DiffLine(DiffTag.Removed, a[x++]));
            while (y < b.Length) raw.Add(new DiffLine(DiffTag.Added, b[y++]));

            return Collapse(raw);
        }

        private static string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n");

        // Collapse runs of >6 unchanged lines to 3 + marker + 3.
        private static List<DiffLine> Collapse(List<DiffLine> diff)
        {
            var outl = new List<DiffLine>();
            int i = 0;
            while (i < diff.Count)
            {
                if (diff[i].Tag != DiffTag.Context) { outl.Add(diff[i]); i++; continue; }
                int j = i;
                while (j < diff.Count && diff[j].Tag == DiffTag.Context) j++;
                int run = j - i;
                if (run <= 6)
                {
                    for (int k = i; k < j; k++) outl.Add(diff[k]);
                }
                else
                {
                    for (int k = i; k < i + 3; k++) outl.Add(diff[k]);
                    outl.Add(new DiffLine(DiffTag.Marker, $"… {run - 6} unchanged lines …"));
                    for (int k = j - 3; k < j; k++) outl.Add(diff[k]);
                }
                i = j;
            }
            return outl;
        }
    }

    /// <summary>Shows the proposed edit (summary + diff + any messages) and asks to apply.</summary>
    internal sealed class AiActionPreviewDialog : Form
    {
        public AiActionPreviewDialog(string summary, List<DiffLine> diff, List<string> messages, bool dark)
        {
            Text = "AI Agent — Preview Changes";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 520);
            MinimumSize = new Size(440, 320);
            ShowInTaskbar = false;

            Color bg = dark ? Color.FromArgb(30, 30, 30) : SystemColors.Window;
            Color fg = dark ? Color.Gainsboro : SystemColors.WindowText;

            var summaryLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(8, 8, 8, 4),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Text = string.IsNullOrWhiteSpace(summary) ? "Proposed changes:" : summary,
            };

            var view = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Font = new Font(FontFamily.GenericMonospace, 9.5f),
                BackColor = bg,
                ForeColor = fg,
                WordWrap = false,
            };
            RenderDiff(view, diff, messages, dark);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var apply = new Button { Text = "Apply", Left = 510, Top = 8, Width = 75, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 591, Top = 8, Width = 78, Height = 28, DialogResult = DialogResult.Cancel };
            bar.Controls.AddRange(new Control[] { apply, cancel });

            Controls.Add(view);
            Controls.Add(summaryLabel);
            Controls.Add(bar);
            AcceptButton = apply;
            CancelButton = cancel;
        }

        private static void RenderDiff(RichTextBox view, List<DiffLine> diff, List<string> messages, bool dark)
        {
            Color added = dark ? Color.FromArgb(120, 220, 120) : Color.FromArgb(0, 128, 0);
            Color removed = dark ? Color.FromArgb(240, 120, 120) : Color.FromArgb(180, 0, 0);
            Color context = dark ? Color.Gray : Color.DimGray;
            Color marker = dark ? Color.SteelBlue : Color.SteelBlue;

            if (messages != null && messages.Count > 0)
            {
                foreach (string m in messages) AppendLine(view, m, dark ? Color.Khaki : Color.DarkGoldenrod);
                AppendLine(view, "", context);
            }

            if (diff == null || diff.Count == 0)
            {
                AppendLine(view, "(No changes to the document.)", context);
                return;
            }

            foreach (DiffLine d in diff)
            {
                switch (d.Tag)
                {
                    case DiffTag.Added: AppendLine(view, "+ " + d.Text, added); break;
                    case DiffTag.Removed: AppendLine(view, "- " + d.Text, removed); break;
                    case DiffTag.Marker: AppendLine(view, "  " + d.Text, marker); break;
                    default: AppendLine(view, "  " + d.Text, context); break;
                }
            }
            view.SelectionStart = 0;
            view.ScrollToCaret();
        }

        private static void AppendLine(RichTextBox view, string text, Color color)
        {
            view.SelectionStart = view.TextLength;
            view.SelectionLength = 0;
            view.SelectionColor = color;
            view.AppendText(text + "\n");
        }
    }
}
