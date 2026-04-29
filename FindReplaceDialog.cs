using ScintillaNET;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace nplus
{
    public class FindReplaceDialog : Form
    {
        private EditorForm _mainForm;

        private TabControl tabControl;
        private ComboBox txtFind, txtReplace;
        private CheckBox chkMatchCase, chkWholeWord, chkWrap, chkBackward;
        private CheckBox chkBookmarkLine, chkPurgeMarks;
        private RadioButton radNormal, radExtended, radRegex;
        private Button btnFindNext, btnCount, btnReplace, btnReplaceAll, btnClose;
        private Button btnMarkAll, btnClearMarks, btnCopyMarked;
        private Label lblStatus;

        private const int MaxHistory = 20;
        private List<string> _findHistory = new List<string>();
        private List<string> _replaceHistory = new List<string>();
        private bool _centeredOnParent = false;

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FindReplaceDialog));
            this.SuspendLayout();
            // 
            // FindReplaceDialog
            // 
            this.ClientSize = new System.Drawing.Size(284, 261);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "FindReplaceDialog";
            this.ResumeLayout(false);

        }

        public FindReplaceDialog(EditorForm mainForm)
        {
            _mainForm = mainForm;
            InitializeUI();
            this.Icon = mainForm.Icon;
        }

        public void SetMode(int tabIndex)
        {
            if (tabIndex >= 0 && tabIndex <= 2)
            {
                tabControl.SelectedIndex = tabIndex;
            }

            // Auto-populate with selected text from the active editor
            var editor = _mainForm.GetActiveEditor();
            if (editor != null && editor.SelectedText.Length > 0 && editor.SelectedText.Length < 500 && !editor.SelectedText.Contains("\n"))
            {
                txtFind.Text = editor.SelectedText;
            }

            // Center on the main form the first time
            if (!_centeredOnParent)
            {
                this.StartPosition = FormStartPosition.Manual;
                this.Location = new Point(
                    _mainForm.Left + (_mainForm.Width - this.Width) / 2,
                    _mainForm.Top + (_mainForm.Height - this.Height) / 2);
                _centeredOnParent = true;
            }

            txtFind.Focus();
            txtFind.SelectAll();
        }

        #region UI Event Execution & Macro Logging

        private void ExecuteSearch(string action)
        {
            var editor = _mainForm.GetActiveEditor();
            if (editor == null || string.IsNullOrEmpty(txtFind.Text)) return;

            lblStatus.Text = "";
            lblStatus.ForeColor = Color.DarkSlateGray;

            // Add to history
            AddToHistory(_findHistory, txtFind);
            if (action == "Replace" || action == "ReplaceAll")
                AddToHistory(_replaceHistory, txtReplace);

            SearchFlags flags = SearchFlags.None;
            if (chkMatchCase.Checked) flags |= SearchFlags.MatchCase;
            if (chkWholeWord.Checked) flags |= SearchFlags.WholeWord;
            if (radRegex.Checked) flags |= SearchFlags.Regex;

            string searchFor = txtFind.Text;
            string replaceWith = txtReplace?.Text ?? "";

            if (radExtended.Checked)
            {
                try
                {
                    searchFor = Regex.Unescape(searchFor);
                    replaceWith = Regex.Unescape(replaceWith);
                }
                catch { }
            }

            // Capture UI state into local variables for the Macro closure
            string mSearch = searchFor;
            string mReplace = replaceWith;
            SearchFlags mFlags = flags;
            bool mBackward = chkBackward.Checked;
            bool mWrap = chkWrap.Checked;
            bool mRegex = radRegex.Checked;
            bool mPurge = chkPurgeMarks.Checked;
            bool mBookmark = chkBookmarkLine.Checked;

            // Execute & Record
            if (action == "Count")
            {
                int count = DoCountOccurrences(editor, mSearch, mFlags);
                lblStatus.ForeColor = Color.DarkSlateGray;
                lblStatus.Text = $"Count: {count} match{(count != 1 ? "es" : "")} found.";
                return;
            }

            if (action == "ReplaceAll")
            {
                _mainForm.RecordMacroStep(new MacroStep { ActionType = MacroActionType.ReplaceAll, SearchText = mSearch, ReplaceText = mReplace, Flags = (int)mFlags, IsRegex = mRegex });
                int count = DoReplaceAll(editor, mSearch, mReplace, mFlags, mRegex);
                lblStatus.ForeColor = Color.DarkSlateGray;
                lblStatus.Text = $"{count} occurrence{(count != 1 ? "s" : "")} replaced.";
                return;
            }

            if (action == "MarkAll")
            {
                _mainForm.RecordMacroStep(new MacroStep { ActionType = MacroActionType.MarkAll, SearchText = mSearch, Flags = (int)mFlags, IsPurge = mPurge, IsBookmark = mBookmark });
                int count = DoMarkAll(editor, mSearch, mFlags, mPurge, mBookmark, EditorForm.MARK_INDICATOR, EditorForm.BOOKMARK_MARKER);
                lblStatus.ForeColor = Color.DarkSlateGray;
                lblStatus.Text = $"Mark: {count} match{(count != 1 ? "es" : "")}.";
                return;
            }

            // FindNext or Replace
            bool isReplace = action == "Replace";
            _mainForm.RecordMacroStep(new MacroStep { ActionType = MacroActionType.FindReplace, SearchText = mSearch, ReplaceText = mReplace, IsReplace = isReplace, Flags = (int)mFlags, IsBackward = mBackward, IsWrap = mWrap, IsRegex = mRegex });

            if (!DoFindOrReplaceNext(editor, mSearch, mReplace, isReplace, mFlags, mBackward, mWrap, mRegex))
            {
                lblStatus.ForeColor = Color.IndianRed;
                lblStatus.Text = $"Can't find the text \"{txtFind.Text}\"";
            }
            else
            {
                int line = editor.LineFromPosition(editor.CurrentPosition) + 1;
                int col = editor.CurrentPosition - editor.Lines[line - 1].Position + 1;
                lblStatus.ForeColor = Color.DarkSlateGray;
                lblStatus.Text = isReplace ? $"Replaced at Ln {line}, Col {col}" : $"Found at Ln {line}, Col {col}";
            }
        }

        #endregion

        #region Headless Search Engine (Macro Safe)

        public static bool DoFindOrReplaceNext(Scintilla editor, string searchFor, string replaceWith, bool replace, SearchFlags flags, bool backward, bool wrap, bool isRegex)
        {
            editor.SearchFlags = flags;

            if (backward)
            {
                editor.TargetStart = editor.CurrentPosition;
                editor.TargetEnd = 0;
            }
            else
            {
                editor.TargetStart = editor.CurrentPosition;
                editor.TargetEnd = editor.TextLength;
            }

            int pos = editor.SearchInTarget(searchFor);

            if (pos == -1 && wrap)
            {
                editor.TargetStart = backward ? editor.TextLength : 0;
                editor.TargetEnd = backward ? 0 : editor.TextLength;
                pos = editor.SearchInTarget(searchFor);
            }

            if (pos != -1)
            {
                if (replace)
                {
                    if (editor.SelectedText == searchFor || (isRegex && Regex.IsMatch(editor.SelectedText, searchFor)))
                    {
                        if (isRegex) editor.ReplaceTargetRe(replaceWith);
                        else editor.ReplaceTarget(replaceWith);
                    }
                    DoFindOrReplaceNext(editor, searchFor, replaceWith, false, flags, backward, wrap, isRegex);
                }
                else
                {
                    editor.SetSelection(editor.TargetEnd, editor.TargetStart);
                    editor.ScrollRange(editor.TargetStart, editor.TargetEnd);
                }
                return true;
            }
            return false;
        }

        public static int DoReplaceAll(Scintilla editor, string searchFor, string replaceWith, SearchFlags flags, bool isRegex)
        {
            editor.SearchFlags = flags;
            editor.BeginUndoAction();
            editor.TargetStart = 0;
            editor.TargetEnd = editor.TextLength;
            int count = 0;

            while (editor.SearchInTarget(searchFor) != -1)
            {
                if (isRegex) editor.ReplaceTargetRe(replaceWith);
                else editor.ReplaceTarget(replaceWith);

                editor.TargetStart = editor.TargetEnd;
                editor.TargetEnd = editor.TextLength;
                count++;
            }
            editor.EndUndoAction();
            return count;
        }

        public static int DoCountOccurrences(Scintilla editor, string searchFor, SearchFlags flags)
        {
            editor.SearchFlags = flags;
            editor.TargetStart = 0;
            editor.TargetEnd = editor.TextLength;
            int count = 0;

            while (editor.SearchInTarget(searchFor) != -1)
            {
                editor.TargetStart = editor.TargetEnd;
                editor.TargetEnd = editor.TextLength;
                count++;
            }
            return count;
        }

        public static int DoMarkAll(Scintilla editor, string searchFor, SearchFlags flags, bool purge, bool bookmark, int markIndicator, int bookmarkMarker)
        {
            editor.SearchFlags = flags;
            if (purge) DoClearAllMarks(editor, markIndicator);

            editor.TargetStart = 0;
            editor.TargetEnd = editor.TextLength;
            int count = 0;

            editor.IndicatorCurrent = markIndicator;

            while (editor.SearchInTarget(searchFor) != -1)
            {
                editor.IndicatorFillRange(editor.TargetStart, editor.TargetEnd - editor.TargetStart);

                if (bookmark)
                {
                    int lineIdx = editor.LineFromPosition(editor.TargetStart);
                    editor.Lines[lineIdx].MarkerAdd(bookmarkMarker);
                }

                editor.TargetStart = editor.TargetEnd;
                editor.TargetEnd = editor.TextLength;
                count++;
            }
            return count;
        }

        public static void DoClearAllMarks(Scintilla editor, int markIndicator)
        {
            if (editor == null) return;
            editor.IndicatorCurrent = markIndicator;
            editor.IndicatorClearRange(0, editor.TextLength);
        }

        #endregion

        #region UI Initialization

        private void CopyMarkedLines()
        {
            var editor = _mainForm.GetActiveEditor();
            if (editor == null) return;

            var text = new List<string>();
            for (int i = 0; i < editor.Lines.Count; i++)
            {
                if ((editor.Lines[i].MarkerGet() & (1u << EditorForm.BOOKMARK_MARKER)) != 0)
                {
                    text.Add(editor.Lines[i].Text);
                }
            }

            if (text.Count > 0)
            {
                Clipboard.SetText(string.Join("", text));
                lblStatus.ForeColor = Color.DarkSlateGray;
                lblStatus.Text = $"{text.Count} bookmarked line{(text.Count != 1 ? "s" : "")} copied to clipboard.";
            }
            else
            {
                lblStatus.ForeColor = Color.IndianRed;
                lblStatus.Text = "No bookmarked lines found to copy.";
            }
        }

        private void InitializeUI()
        {
            this.Text = "Find";
            this.Size = new Size(580, 385);
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            this.TopMost = true;
            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) this.Close(); };

            lblStatus = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = SystemColors.Control,
                ForeColor = Color.DarkSlateGray,
                Font = new Font("Segoe UI", 8.5f),
                Padding = new Padding(5, 0, 0, 0),
                Text = ""
            };
            this.Controls.Add(lblStatus);

            tabControl = new TabControl { Dock = DockStyle.Fill };
            var tabFind = new TabPage("Find");
            var tabReplace = new TabPage("Replace");
            var tabMark = new TabPage("Mark");

            tabControl.TabPages.Add(tabFind);
            tabControl.TabPages.Add(tabReplace);
            tabControl.TabPages.Add(tabMark);
            this.Controls.Add(tabControl);

            Label lblFind = new Label { Text = "Find what:", Location = new Point(10, 20), AutoSize = true };
            txtFind = new ComboBox { Location = new Point(85, 18), Width = 280, DropDownStyle = ComboBoxStyle.DropDown };

            Label lblReplace = new Label { Text = "Replace with:", Location = new Point(5, 50), AutoSize = true };
            txtReplace = new ComboBox { Location = new Point(85, 48), Width = 280, DropDownStyle = ComboBoxStyle.DropDown };

            chkBackward = new CheckBox { Text = "Backward direction", Location = new Point(10, 100), AutoSize = true };
            chkMatchCase = new CheckBox { Text = "Match case", Location = new Point(10, 125), AutoSize = true };
            chkWholeWord = new CheckBox { Text = "Match whole word only", Location = new Point(10, 150), AutoSize = true };
            chkWrap = new CheckBox { Text = "Wrap around", Location = new Point(10, 175), AutoSize = true, Checked = true };

            chkBookmarkLine = new CheckBox { Text = "Bookmark line", Location = new Point(200, 20), AutoSize = true, Checked = true };
            chkPurgeMarks = new CheckBox { Text = "Purge for each search", Location = new Point(200, 45), AutoSize = true };

            GroupBox grpMode = new GroupBox { Text = "Search Mode", Location = new Point(200, 100), Size = new Size(170, 100) };
            radNormal = new RadioButton { Text = "Normal", Location = new Point(10, 20), AutoSize = true, Checked = true };
            radExtended = new RadioButton { Text = "Extended (\\n, \\r, \\t...)", Location = new Point(10, 45), AutoSize = true };
            radRegex = new RadioButton { Text = "Regular expression", Location = new Point(10, 70), AutoSize = true };
            grpMode.Controls.AddRange(new Control[] { radNormal, radExtended, radRegex });

            btnFindNext = new Button { Text = "Find Next", Location = new Point(400, 15), Width = 130 };
            btnCount = new Button { Text = "Count", Location = new Point(400, 45), Width = 130 };
            btnReplace = new Button { Text = "Replace", Location = new Point(400, 75), Width = 130 };
            btnReplaceAll = new Button { Text = "Replace All", Location = new Point(400, 105), Width = 130 };

            btnMarkAll = new Button { Text = "Mark All", Location = new Point(400, 15), Width = 130 };
            btnClearMarks = new Button { Text = "Clear all marks", Location = new Point(400, 45), Width = 130 };
            btnCopyMarked = new Button { Text = "Copy Marked Text", Location = new Point(400, 75), Width = 130 };
            btnClose = new Button { Text = "Close", Location = new Point(400, 165), Width = 130 };

            btnFindNext.Click += (s, e) => ExecuteSearch("FindNext");
            this.AcceptButton = btnFindNext;
            btnCount.Click += (s, e) => ExecuteSearch("Count");
            btnReplace.Click += (s, e) => ExecuteSearch("Replace");
            btnReplaceAll.Click += (s, e) => ExecuteSearch("ReplaceAll");
            btnMarkAll.Click += (s, e) => ExecuteSearch("MarkAll");

            // Log manual clear marks to macro as well
            btnClearMarks.Click += (s, e) =>
            {
                _mainForm.RecordMacroStep(new MacroStep { ActionType = MacroActionType.ClearMarks });
                DoClearAllMarks(_mainForm.GetActiveEditor(), EditorForm.MARK_INDICATOR);
                lblStatus.ForeColor = Color.DarkSlateGray;
                lblStatus.Text = "All marks cleared.";
            };

            btnCopyMarked.Click += (s, e) => CopyMarkedLines();
            btnClose.Click += (s, e) => this.Hide();

            Panel commonPanel = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
            commonPanel.Controls.AddRange(new Control[] {
                lblFind, txtFind, lblReplace, txtReplace,
                chkBackward, chkMatchCase, chkWholeWord, chkWrap,
                chkBookmarkLine, chkPurgeMarks,
                grpMode,
                btnFindNext, btnCount, btnReplace, btnReplaceAll,
                btnMarkAll, btnClearMarks, btnCopyMarked,
                btnClose
            });

            tabControl.SelectedIndexChanged += (s, e) =>
            {
                if (tabControl.SelectedTab != null)
                {
                    tabControl.SelectedTab.Controls.Add(commonPanel);
                }

                bool isFind = tabControl.SelectedIndex == 0;
                bool isReplace = tabControl.SelectedIndex == 1;
                bool isMark = tabControl.SelectedIndex == 2;

                lblReplace.Visible = isReplace;
                txtReplace.Visible = isReplace;
                btnReplace.Visible = isReplace;
                btnReplaceAll.Visible = isReplace;

                btnFindNext.Visible = !isMark;
                btnCount.Visible = isFind;

                chkBookmarkLine.Visible = isMark;
                chkPurgeMarks.Visible = isMark;
                btnMarkAll.Visible = isMark;
                btnClearMarks.Visible = isMark;
                btnCopyMarked.Visible = isMark;

                this.Text = isFind ? "Find" : isReplace ? "Replace" : "Mark";
            };

            tabFind.Controls.Add(commonPanel);
            tabControl.SelectedIndex = 1; tabControl.SelectedIndex = 0;
        }

        public void FindNext()
        {
            if (!string.IsNullOrEmpty(txtFind.Text))
                ExecuteSearch("FindNext");
        }

        private void AddToHistory(List<string> history, ComboBox combo)
        {
            string text = combo.Text;
            if (string.IsNullOrEmpty(text)) return;

            history.Remove(text);
            history.Insert(0, text);
            if (history.Count > MaxHistory) history.RemoveRange(MaxHistory, history.Count - MaxHistory);

            // Rebuild dropdown items
            combo.Items.Clear();
            foreach (var item in history) combo.Items.Add(item);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            base.OnFormClosing(e);
        }
        #endregion
    }
}