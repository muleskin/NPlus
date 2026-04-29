using ScintillaNET;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace nplus
{
    public enum MacroActionType { InsertText, NewLine, Backspace, Delete, KeyCommand, FindReplace, ReplaceAll, MarkAll, ClearMarks }

    public enum SortMode { Lexicographic, Locale, Integer, DecimalComma, DecimalDot, Length }

    public class MacroStep
    {
        public MacroActionType ActionType { get; set; }
        public string Data { get; set; }
        public int CommandId { get; set; }

        // Find/Replace parameters
        public string SearchText { get; set; }
        public string ReplaceText { get; set; }
        public int Flags { get; set; }
        public bool IsRegex { get; set; }
        public bool IsBackward { get; set; }
        public bool IsWrap { get; set; }
        public bool IsReplace { get; set; }
        public bool IsPurge { get; set; }
        public bool IsBookmark { get; set; }

        public MacroStep() { }
        public MacroStep(MacroActionType action, string data = null) { ActionType = action; Data = data; }

        public void Execute(Scintilla editor)
        {
            switch (ActionType)
            {
                case MacroActionType.InsertText:
                    editor.InsertText(editor.CurrentPosition, Data ?? "");
                    editor.GotoPosition(editor.CurrentPosition + (Data?.Length ?? 0));
                    break;
                case MacroActionType.NewLine:
                    editor.InsertText(editor.CurrentPosition, System.Environment.NewLine);
                    editor.GotoPosition(editor.CurrentPosition + System.Environment.NewLine.Length);
                    break;
                case MacroActionType.Backspace:
                    if (editor.SelectionStart != editor.SelectionEnd)
                        editor.ReplaceSelection("");
                    else if (editor.CurrentPosition > 0)
                        editor.DeleteRange(editor.CurrentPosition - 1, 1);
                    break;
                case MacroActionType.Delete:
                    if (editor.SelectionStart != editor.SelectionEnd)
                        editor.ReplaceSelection("");
                    else if (editor.CurrentPosition < editor.TextLength)
                        editor.DeleteRange(editor.CurrentPosition, 1);
                    break;
                case MacroActionType.KeyCommand:
                    editor.ExecuteCmd((Command)CommandId);
                    break;
                case MacroActionType.FindReplace:
                    FindReplaceDialog.DoFindOrReplaceNext(editor, SearchText, ReplaceText, IsReplace, (SearchFlags)Flags, IsBackward, IsWrap, IsRegex);
                    break;
                case MacroActionType.ReplaceAll:
                    FindReplaceDialog.DoReplaceAll(editor, SearchText, ReplaceText, (SearchFlags)Flags, IsRegex);
                    break;
                case MacroActionType.MarkAll:
                    FindReplaceDialog.DoMarkAll(editor, SearchText, (SearchFlags)Flags, IsPurge, IsBookmark, EditorForm.MARK_INDICATOR, EditorForm.BOOKMARK_MARKER);
                    break;
                case MacroActionType.ClearMarks:
                    FindReplaceDialog.DoClearAllMarks(editor, EditorForm.MARK_INDICATOR);
                    break;
            }
        }
    }

    // Place this right above the final closing bracket of the namespace
    public class JsonViewerForm : Form
    {
        private TreeView _tree;

        public JsonViewerForm(JsonDocument jsonDoc, bool isDarkMode, Icon parentIcon = null)
        {
            this.Text = "n+ - JSON Explorer";
            this.Size = new Size(500, 700);
            this.StartPosition = FormStartPosition.CenterParent;
            if (parentIcon != null) this.Icon = parentIcon;

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 11),
                BorderStyle = BorderStyle.None,
                Indent = 25
            };

            // Match the main editor's theme
            if (isDarkMode)
            {
                this.BackColor = Color.FromArgb(30, 30, 35);
                _tree.BackColor = Color.FromArgb(30, 30, 35);
                _tree.ForeColor = Color.LightSkyBlue;
            }

            TreeNode root = new TreeNode("JSON Payload");
            _tree.Nodes.Add(root);

            // Recursively build the visual tree
            BuildTree(jsonDoc.RootElement, root);
            root.Expand();

            this.Controls.Add(_tree);
        }

        private void BuildTree(JsonElement element, TreeNode parentNode)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty prop in element.EnumerateObject())
                    {
                        TreeNode child = new TreeNode(prop.Name);
                        parentNode.Nodes.Add(child);
                        BuildTree(prop.Value, child);
                    }
                    break;
                case JsonValueKind.Array:
                    int index = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        TreeNode child = new TreeNode($"[{index}]");
                        parentNode.Nodes.Add(child);
                        BuildTree(item, child);
                        index++;
                    }
                    break;
                case JsonValueKind.String:
                    parentNode.Text += $" : \"{element.GetString()}\"";
                    if (_tree.BackColor != SystemColors.Window) parentNode.ForeColor = Color.Khaki;
                    break;
                case JsonValueKind.Number:
                    parentNode.Text += $" : {element.GetRawText()}";
                    if (_tree.BackColor != SystemColors.Window) parentNode.ForeColor = Color.Olive;
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    parentNode.Text += $" : {element.GetRawText()}";
                    if (_tree.BackColor != SystemColors.Window) parentNode.ForeColor = Color.DeepPink;
                    break;
            }
        }
    }

    public partial class EditorForm : Form
    {
        // ── Cohesive Icon Set (base64 PNG, theme-neutral) ──
        private static readonly string _ico_newdoc = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAoElEQVR4nGNgGAUDDRjRBdx6Lv0nVvOuEj0M/eiAiVQXkeoYFlwShFzn1nPpv5EyPwNDz6X/+NRS5AMGBgYGI2V+vD6h2AJCllDFAnyW4IwDYsC5ux8xxNzQ4oRkC2y2pPxnYGBg2OWDGbHYfEC1IMIFaG4BUUEECxZsYkd85uDNL4PDB8iuJNblMDA4fIAMiHU5QQtIKbbxAZoH0SggCADhqzUdoPqG7QAAAABJRU5ErkJggg==";
        private static readonly string _ico_save = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAbElEQVR4nGNgGAUEACO6gFvPpf8w9qoUOaINCpvzCM7eVaIHN5cJl+GUAGRzmPAppAYYtWAEWMCCTxI5bZMLhnEQfdtQQrJhXAE9xFuADQQEBKDwN2zYQFDP0I+DUQsIApw1GiXJFLlGG/oAACT2G0UGa8kbAAAAAElFTkSuQmCC";
        private static readonly string _ico_saveall = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAg0lEQVR4nGNgGOqAEZdExfqH/6lhARMtDcdpATUBzS1gQea49Vz6z8DAwFDhyE+SIWFzHjEwMDAwGClj6oP7AGY4JeDc3Y+4LaAVGLVg4C1gwSYIS3bUAMMwiL5tKCHLIK6AHqziQz+IBiaZUgI6AuVRakn6BdGuEj2c9TOxgBpmkAwACl8ZqWD7YdQAAAAASUVORK5CYII=";
        private static readonly string _ico_revert = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAhElEQVR4nGNgGAUDDRiJVfhsgdF/dDGphHME9RNUgM1gUizCawGy4dgMISSPFzxbYPSfGNeTqpY8DXj0MJFiCFUAOa7Hp5fmPiBoAbm+gQEWWhkMA/QNIkpcDdOLnuFQLCA5NxIBMIJIKuEcI6kW4XI9VguQLSLGYHyGk+Q6cuVHwcADABNJUho+031jAAAAAElFTkSuQmCC";
        private static readonly string _ico_showchars = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAjUlEQVR4nO3SIQ7CQBAF0FcUh4AEi9ng4ATcAME9VmJIkHAPztA7QAKqFskRcAiqEGxDW9Ps1zP/jRhyBp8iNbA+3ufYY4XJ186jjGH2a3+UKF/iig2mdfmpjKHAJXVcEsAO4yZF/wKzNuVNgKpv4IBXb0AZww0LnPHsHKiRqoxh6/Om3QNtk4EM5Awhb7HoFfkLrNj5AAAAAElFTkSuQmCC";
        private static readonly string _ico_indentguide = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAANUlEQVR4nGNgGAWUALeeS//dei79J0cMBpho6UC6WDCwYDQOBh6MxsHAg9E4GHgwGgfDAwAAu7dPL0d/AWYAAAAASUVORK5CYII=";
        private static readonly string _ico_wordwrap = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAZUlEQVR4nGNgGAWjYBQwogu49Vz6T65hu0r0MMxjItcwbACb4zBspIbhyD6hmg+wBQ9VLcAFCFpASaQTtIAUw3GpZaHUYHT16HFB/3yAKzWQYzhB4NZz6T+lkUyUJTS1YBTQHAAAd2wpQxX5AwQAAAAASUVORK5CYII=";
        private static readonly string _ico_colselect = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAW0lEQVR4nGNgGAUEACMxitx6Lv3HJberRA+vGSiSPT3TsBq0i8EGt+UMR7CKl5RkMWJYgNMQCnxAFMBlAT6LYWA0iEaDCL8Fo0FE0ILRICJowcgIIiZiLBjZAAB/9Vpu0oLm2AAAAABJRU5ErkJggg==";
        private static readonly string _ico_undo = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAgklEQVR4nGNgGAXDHjCSotit59J/dLFdJXp4zSDKAmwGE2sRQQuQDcdmCCF5goYT43pS1ZKnAYceJlIMoBogx/W49NLcB0RbQK6PiLKAXMOJsoASw3FaAMsw5CRTZP04LcBlMCELseVkvFmbnMINHeCNg10leowkly+UAEojfBRgBQBjT0+i6V2m8wAAAABJRU5ErkJggg==";
        private static readonly string _ico_playmacro = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAeElEQVR4nO3Vuw2AMAwE0ANlLEZgDMZiDEbIXlBZsiKQ/xJFrnRxT5cmwMyvs13HHe1YNUgEEgEOlQKEWCETwKFSgBAN5Aa0UBjgUClAyAi1TKDv5zLe0ha8lQMJC76Kw4BUTHE9kbYcMC6wFFPUCzzlYjL+gxkxDy27Lr81Sif2AAAAAElFTkSuQmCC";
        private static readonly string _ico_livemonitor = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAj0lEQVR4nGNgGAX0BncC3P7fCXD7D+MzUdtwdDYjLQxHBiz4NNlsSYFrOuIzhyzHUC2IVDbswnCAyoZdjFSNA2RLsFmIAmy2pPyHBRFyUJEK8MYBuoXIfGLjhKpBRLIF2FwJEyM22LBaQEgzKUkWHgfkRCQxDiEqDsjNZCQB5GRLCiDKB5TkA5on01EwAgAAJMg6MJ2h2xEAAAAASUVORK5CYII=";
        private static readonly string _ico_jsontree = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAe0lEQVR4nGNgGOqAEV3g2QKj/+hiUgnnMNQRC5jI1ThoLGChRLPNlhR4cB7xmYM1GMn2AbLh2PgwgDPy3HouYdUAA980JmGIYfPFwMfBrhI9nL6kaRwQC2geyQSDCFdkf2PAjGRsYDSSCYLRSCYIaB7JFBXXuAwdBSQBADHQNj/ZvWGqAAAAAElFTkSuQmCC";
        private static readonly string _ico_themetoggle = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAApklEQVR4nGNgGAUDDRiJVejWc+k/jL1AJIGBgYGBQSrhHIr+ZwuM/qOLEbQA2WB0C2BAKuEc47MFRv+R+TA2E6mGYwO4DMdrAbGGIwN0w3FaQI7hDAyoPsFrATUBhgXkuh4G0H1Bcx+w0MJQZF8MTR/gzWi7SvSILj4IGY7VAmoDrBaQ6wuyczI5lqAXeAQtIMUSZMPRLaF6fYBsKbFmkwSwBdPQBgDYYEnb4HzoJAAAAABJRU5ErkJggg==";
        private static readonly string _ico_userguide = "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAA+UlEQVR4nOWVPQ6CQBCFn7AgKnHFUBgTG+lN5BCcyGNwIg9hYa+xJFoICRL/CBaGRNk1u4IUxK9j82bezpDZAf4Oz19nnr/OZPUt2aS88+ViJoznCooJXYcCAFab6O3s9fuTIeElzhOKYHQvF8vNFGHQF7gOZeIZg1/TfAMilvAJTzdsg+SZRG3BMjVM7A6jK13BoKfBdSjmUwqdKDhEV9xTdlwqt2i3T5BcUoyHBojKjlUlg/ic4hjfYPd1jKw2V1P6HwCAaajCualUQZTcsdpECMJLPQYyVGoR7ZJ6WyQDU0HxCZZ9/IpxOcKFIbu9ZJaPFN+uzObzAE23TLgiQMjfAAAAAElFTkSuQmCC";
        private Image IconFromBase64(string b64) => Image.FromStream(new MemoryStream(Convert.FromBase64String(b64)));

        private static Image MakeRecordIcon()
        {
            Bitmap bmp = new Bitmap(24, 24);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Color.FromArgb(220, 50, 50)))
                    g.FillEllipse(brush, 4, 4, 16, 16);
            }
            return bmp;
        }

        private static Image MakeStopIcon()
        {
            Bitmap bmp = new Bitmap(24, 24);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var brush = new SolidBrush(Color.FromArgb(80, 80, 80)))
                    g.FillRectangle(brush, 5, 5, 14, 14);
            }
            return bmp;
        }

        private TabControl tcDocuments;
        private MenuStrip mainMenu;
        private ToolStrip mainToolbar;

        // NEW: Live File Monitoring State
        private Dictionary<TabPage, FileSystemWatcher> _fileWatchers = new Dictionary<TabPage, FileSystemWatcher>();
        // General file change detection (prompts user on external changes/deletions)
        private Dictionary<TabPage, FileSystemWatcher> _fileChangeWatchers = new Dictionary<TabPage, FileSystemWatcher>();
        private bool _isSwitchingTabs = false;

        // Status Bar Elements
        private StatusStrip statusBar;
        private ToolStripStatusLabel lblLength;
        private ToolStripStatusLabel lblPosition;
        private ToolStripStatusLabel lblEncoding;
        private ToolStripStatusLabel lblInsOvr;
        private ToolStripStatusLabel lblZoom;

        // JSON Side Panel
        private SplitContainer _mainSplit;
        private Panel _jsonPanel;
        private TreeView _jsonTree;
        private Label _jsonPanelHeader;

        private const int CloseBtnSize = 15;

        public const int BOOKMARK_MARKER = 1;
        public const int MARK_INDICATOR = 8;

        private FindReplaceDialog _findDialog;

        // Tab Drag-and-Drop Reordering State
        private TabPage _draggedTab = null;
        private int _dragStartIndex = -1;
        private Point _dragStartPoint;

        // Editor & Macro State
        private bool _isRecording = false;
        private bool _isDarkMode = true;
        private bool _wordWrap = false;
        private List<MacroStep> _currentMacro = new List<MacroStep>();
        private Dictionary<string, List<MacroStep>> _savedMacros = new Dictionary<string, List<MacroStep>>();

        // Toolbar View States & Buttons
        private bool _showCharacters = false;
        private bool _showIndentGuides = false;
        private bool _restoreMaximized = false;
        private float _zoomLevel = 1.0f;
        private const float ZoomStep = 0.1f;
        private const float ZoomMin = 0.5f;
        private const float ZoomMax = 3.0f;
        private float _baseEditorFontSize = 11f;
        private float _baseMenuFontSize = 9f;
        private float _baseTabFontSize = 9f;

        private ToolStripButton btnNew;
        private ToolStripButton btnSave;
        private ToolStripButton btnSaveAll;
        private ToolStripButton btnShowChars;
        private ToolStripButton btnIndentGuide;
        private ToolStripButton btnColSelect;
        private ToolStripButton btnWordWrap;
        private ToolStripButton btnUndo;
        private ToolStripButton btnThemeToggle;
        private ToolStripButton btnLiveMonitor; // NEW
        private ToolStripButton btnJsonTree;
        private ToolStripButton btnPlayMacro;
        private ToolStripButton btnStartRecord;
        private ToolStripButton btnStopRecord;
        private ToolStripButton btnRevert;
        private ToolStripButton btnHelp;

        // Macro Menu Items
        private ToolStripMenuItem startRecordItem, stopRecordItem, playbackItem, saveMacroItem, runMultipleItem;

        // Background Session Paths
        private readonly string _appDataFolder;
        private readonly string _sessionFilePath;
        private readonly string _backupFolderPath;
        private readonly string _settingsFilePath;
        private readonly string _recentFilesPath;
        private readonly string _macrosFilePath;

        // Recent Files
        private const int MaxRecentFiles = 10;
        private List<string> _recentFiles = new List<string>();
        private ToolStripMenuItem _fileMenu;

        // Encoding Menu
        private ToolStripMenuItem _encodingMenu;
        private Dictionary<TabPage, Encoding> _tabEncodings = new Dictionary<TabPage, Encoding>();

        public EditorForm()
        {
            // Setup hidden background paths for Session Snapshots & Settings
            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nplus");
            _sessionFilePath = Path.Combine(_appDataFolder, "session.txt");
            _backupFolderPath = Path.Combine(_appDataFolder, "backups");
            _settingsFilePath = Path.Combine(_appDataFolder, "settings.txt");
            _recentFilesPath = Path.Combine(_appDataFolder, "recentfiles.txt");
            _macrosFilePath = Path.Combine(_appDataFolder, "macros.json");

            // Ensure the backup directory exists
            Directory.CreateDirectory(_backupFolderPath);

            LoadSettings();
            LoadMacros();

            InitializeComponentCustom();
            AddNewTab("new 1");
            ApplyThemeToForm();
            LoadSession();

            if (_zoomLevel != 1.0f) ApplyZoom();
            if (_restoreMaximized) this.WindowState = FormWindowState.Maximized;
        }

        private void InitializeComponentCustom()
        {
            this.Text = "n+ - beta";
            if (this.StartPosition != FormStartPosition.Manual)
            {
                this.Size = new Size(1150, 750);
                this.StartPosition = FormStartPosition.CenterScreen;
            }

            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // --- MENU STRIP ---
            mainMenu = new MenuStrip();

            _fileMenu = new ToolStripMenuItem("File");
            _fileMenu.DropDownItems.Add("New", null, (s, e) => AddNewTab("new " + (tcDocuments.TabCount + 1)));
            _fileMenu.DropDownItems.Add("Open", null, (s, e) => OpenFile());
            _fileMenu.DropDownItems.Add("Save", null, (s, e) => SaveFile());
            _fileMenu.DropDownItems.Add("Save As...", null, (s, e) => SaveFileAs());
            _fileMenu.DropDownItems.Add("-");
            _fileMenu.DropDownItems.Add("Exit", null, (s, e) => this.Close());

            LoadRecentFiles();
            RebuildRecentFilesMenu();

            var editMenu = new ToolStripMenuItem("Edit");
            var findItem = (ToolStripMenuItem)editMenu.DropDownItems.Add("Find", null, (s, e) => ShowFindDialog(0));
            findItem.ShortcutKeys = Keys.Control | Keys.F;
            var replaceItem = (ToolStripMenuItem)editMenu.DropDownItems.Add("Replace", null, (s, e) => ShowFindDialog(1));
            replaceItem.ShortcutKeys = Keys.Control | Keys.H;
            var markItem = (ToolStripMenuItem)editMenu.DropDownItems.Add("Mark", null, (s, e) => ShowFindDialog(2));
            markItem.ShortcutKeys = Keys.Control | Keys.B;
            var findNextItem = (ToolStripMenuItem)editMenu.DropDownItems.Add("Find Next", null, (s, e) => FindNextFromEditor());
            findNextItem.ShortcutKeys = Keys.F3;

            editMenu.DropDownItems.Add("-");

            var lineOps = new ToolStripMenuItem("Line Operations");
            lineOps.DropDownItems.Add("Duplicate Current Line", null, (s, e) => DuplicateCurrentLine());
            lineOps.DropDownItems.Add("Remove Duplicate Lines", null, (s, e) => RemoveDuplicateLines());
            lineOps.DropDownItems.Add("Remove Consecutive Duplicate Lines", null, (s, e) => RemoveConsecutiveDuplicateLines());
            lineOps.DropDownItems.Add("-");
            var splitItem = (ToolStripMenuItem)lineOps.DropDownItems.Add("Split Lines", null, (s, e) => SplitLines());
            splitItem.ShortcutKeys = Keys.Control | Keys.I;
            var joinItem = (ToolStripMenuItem)lineOps.DropDownItems.Add("Join Lines", null, (s, e) => JoinLines());
            joinItem.ShortcutKeys = Keys.Control | Keys.J;
            lineOps.DropDownItems.Add("-");
            var moveUpItem = (ToolStripMenuItem)lineOps.DropDownItems.Add("Move Up Current Line", null, (s, e) => MoveLine(-1));
            moveUpItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.Up;
            var moveDownItem = (ToolStripMenuItem)lineOps.DropDownItems.Add("Move Down Current Line", null, (s, e) => MoveLine(1));
            moveDownItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.Down;
            lineOps.DropDownItems.Add("-");
            lineOps.DropDownItems.Add("Remove Empty Lines", null, (s, e) => RemoveEmptyLines(false));
            lineOps.DropDownItems.Add("Remove Empty Lines (Containing Blank characters)", null, (s, e) => RemoveEmptyLines(true));
            lineOps.DropDownItems.Add("-");
            var insertAbove = (ToolStripMenuItem)lineOps.DropDownItems.Add("Insert Blank Line Above Current", null, (s, e) => InsertBlankLine(false));
            insertAbove.ShortcutKeys = Keys.Control | Keys.Alt | Keys.Enter;
            var insertBelow = (ToolStripMenuItem)lineOps.DropDownItems.Add("Insert Blank Line Below Current", null, (s, e) => InsertBlankLine(true));
            insertBelow.ShortcutKeys = Keys.Control | Keys.Alt | Keys.Shift | Keys.Enter;
            lineOps.DropDownItems.Add("-");
            lineOps.DropDownItems.Add("Reverse Line Order", null, (s, e) => ReverseLineOrder());
            lineOps.DropDownItems.Add("Randomize Line Order", null, (s, e) => RandomizeLineOrder());
            lineOps.DropDownItems.Add("-");
            lineOps.DropDownItems.Add("Sort Lines Lexicographically Ascending", null, (s, e) => SortLinesAdvanced(SortMode.Lexicographic, false, false));
            lineOps.DropDownItems.Add("Sort Lines Lex. Ascending Ignoring Case", null, (s, e) => SortLinesAdvanced(SortMode.Lexicographic, false, true));
            lineOps.DropDownItems.Add("Sort Lines In Locale Order Ascending", null, (s, e) => SortLinesAdvanced(SortMode.Locale, false, false));
            lineOps.DropDownItems.Add("Sort Lines As Integers Ascending", null, (s, e) => SortLinesAdvanced(SortMode.Integer, false, false));
            lineOps.DropDownItems.Add("Sort Lines As Decimals (Comma) Ascending", null, (s, e) => SortLinesAdvanced(SortMode.DecimalComma, false, false));
            lineOps.DropDownItems.Add("Sort Lines As Decimals (Dot) Ascending", null, (s, e) => SortLinesAdvanced(SortMode.DecimalDot, false, false));
            lineOps.DropDownItems.Add("Sort Lines By Length Ascending", null, (s, e) => SortLinesAdvanced(SortMode.Length, false, false));
            lineOps.DropDownItems.Add("-");
            lineOps.DropDownItems.Add("Sort Lines Lexicographically Descending", null, (s, e) => SortLinesAdvanced(SortMode.Lexicographic, true, false));
            lineOps.DropDownItems.Add("Sort Lines Lex. Descending Ignoring Case", null, (s, e) => SortLinesAdvanced(SortMode.Lexicographic, true, true));
            lineOps.DropDownItems.Add("Sort Lines In Locale Order Descending", null, (s, e) => SortLinesAdvanced(SortMode.Locale, true, false));
            lineOps.DropDownItems.Add("Sort Lines As Integers Descending", null, (s, e) => SortLinesAdvanced(SortMode.Integer, true, false));
            lineOps.DropDownItems.Add("Sort Lines As Decimals (Comma) Descending", null, (s, e) => SortLinesAdvanced(SortMode.DecimalComma, true, false));
            lineOps.DropDownItems.Add("Sort Lines As Decimals (Dot) Descending", null, (s, e) => SortLinesAdvanced(SortMode.DecimalDot, true, false));
            lineOps.DropDownItems.Add("Sort Lines By Length Descending", null, (s, e) => SortLinesAdvanced(SortMode.Length, true, false));
            var deleteLineItem = (ToolStripMenuItem)lineOps.DropDownItems.Add("Delete Current Line", null, (s, e) => DeleteCurrentLine());
            deleteLineItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.L;

            var colModeItem = (ToolStripMenuItem)editMenu.DropDownItems.Add("Toggle Column Mode", null, (s, e) => btnColSelect.PerformClick());
            colModeItem.ShortcutKeys = Keys.Control | Keys.Alt | Keys.A;
            editMenu.DropDownItems.Add("-");

            var bookmarkMenu = new ToolStripMenuItem("Bookmark");
            var toggleBk = (ToolStripMenuItem)bookmarkMenu.DropDownItems.Add("Toggle Bookmark", null, (s, e) => ToggleBookmark());
            toggleBk.ShortcutKeys = Keys.Control | Keys.F2;
            var nextBk = (ToolStripMenuItem)bookmarkMenu.DropDownItems.Add("Next Bookmark", null, (s, e) => NavigateBookmark(true));
            nextBk.ShortcutKeys = Keys.F2;
            var prevBk = (ToolStripMenuItem)bookmarkMenu.DropDownItems.Add("Previous Bookmark", null, (s, e) => NavigateBookmark(false));
            prevBk.ShortcutKeys = Keys.Shift | Keys.F2;
            bookmarkMenu.DropDownItems.Add("Clear All Bookmarks", null, (s, e) => ClearAllBookmarks());
            bookmarkMenu.DropDownItems.Add("-");
            bookmarkMenu.DropDownItems.Add("Cut Bookmarked Lines", null, (s, e) => ProcessBookmarks(true, false));
            bookmarkMenu.DropDownItems.Add("Copy Bookmarked Lines", null, (s, e) => ProcessBookmarks(false, false));
            bookmarkMenu.DropDownItems.Add("Delete Bookmarked Lines", null, (s, e) => ProcessBookmarks(true, true));
            bookmarkMenu.DropDownItems.Add("Inverse Bookmark (Delete Unmarked)", null, (s, e) => InverseBookmarkDelete());
            bookmarkMenu.DropDownItems.Add("Paste to (Replace) Bookmarked Lines", null, (s, e) => PasteToBookmarks());

            editMenu.DropDownItems.Add(lineOps);
            editMenu.DropDownItems.Add(bookmarkMenu);

            var blankOps = new ToolStripMenuItem("Blank Operations");
            blankOps.DropDownItems.Add("Trim Trailing Space", null, (s, e) => BlankOp_TrimTrailing());
            blankOps.DropDownItems.Add("Trim Leading Space", null, (s, e) => BlankOp_TrimLeading());
            blankOps.DropDownItems.Add("Trim Leading and Trailing Space", null, (s, e) => BlankOp_TrimBoth());
            blankOps.DropDownItems.Add("EOL to Space", null, (s, e) => BlankOp_EolToSpace());
            blankOps.DropDownItems.Add("Trim both and EOL to Space", null, (s, e) => BlankOp_TrimBothAndEolToSpace());
            blankOps.DropDownItems.Add("-");
            blankOps.DropDownItems.Add("TAB to Space", null, (s, e) => BlankOp_TabToSpace());
            blankOps.DropDownItems.Add("Space to TAB (All)", null, (s, e) => BlankOp_SpaceToTabAll());
            blankOps.DropDownItems.Add("Space to TAB (Leading)", null, (s, e) => BlankOp_SpaceToTabLeading());
            editMenu.DropDownItems.Add(blankOps);

            var macroMenu = new ToolStripMenuItem("Macro");
            startRecordItem = new ToolStripMenuItem("Start Recording", null, (s, e) => StartRecording());
            stopRecordItem = new ToolStripMenuItem("Stop Recording", null, (s, e) => StopRecording()) { Enabled = false };
            playbackItem = new ToolStripMenuItem("Playback", null, (s, e) => PlaybackMacro(true)) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.P };
            saveMacroItem = new ToolStripMenuItem("Save Current Recorded Macro...", null, (s, e) => SaveMacro());
            runMultipleItem = new ToolStripMenuItem("Run a Macro Multiple Times...", null, (s, e) => RunMacroMultiple());

            var loadMacroItem = new ToolStripMenuItem("Load Saved Macro...", null, (s, e) => LoadMacroDialog());
            var editMacroItem = new ToolStripMenuItem("Edit Macro Steps...", null, (s, e) => EditMacroDialog());
            var trimSaveItem = new ToolStripMenuItem("Trim Trailing Space and Save", null, (s, e) => TrimTrailingSpaceAndSave()) { ShortcutKeys = Keys.Alt | Keys.Shift | Keys.S };
            var modifyMacroItem = new ToolStripMenuItem("Modify Shortcut/Delete Macro...", null, (s, e) => ModifyMacro());

            macroMenu.DropDownItems.AddRange(new ToolStripItem[] {
                startRecordItem, stopRecordItem, playbackItem, saveMacroItem, runMultipleItem,
                new ToolStripSeparator(),
                loadMacroItem, editMacroItem,
                new ToolStripSeparator(),
                trimSaveItem, modifyMacroItem
            });

            var toolsMenu = new ToolStripMenuItem("Tools");
            var jsonMenu = new ToolStripMenuItem("JSON");
            jsonMenu.DropDownItems.Add("Format / Pretty Print JSON", null, (s, e) => FormatJson());
            jsonMenu.DropDownItems.Add("View JSON in Visual Tree", null, (s, e) => ShowJsonTree());
            toolsMenu.DropDownItems.Add(jsonMenu);

            var helpMenu = new ToolStripMenuItem("Help");
            helpMenu.DropDownItems.Add("User's Guide", null, (s, e) => ShowUserGuide());

            _encodingMenu = new ToolStripMenuItem("Encoding");
            _encodingMenu.DropDownItems.Add("ANSI", null, (s, e) => SetEncoding("ANSI"));
            _encodingMenu.DropDownItems.Add("UTF-8", null, (s, e) => SetEncoding("UTF-8"));
            _encodingMenu.DropDownItems.Add("UTF-8-BOM", null, (s, e) => SetEncoding("UTF-8-BOM"));
            _encodingMenu.DropDownItems.Add("UTF-16 BE BOM", null, (s, e) => SetEncoding("UTF-16 BE BOM"));
            _encodingMenu.DropDownItems.Add("UTF-16 LE BOM", null, (s, e) => SetEncoding("UTF-16 LE BOM"));
            _encodingMenu.DropDownItems.Add("-");
            _encodingMenu.DropDownItems.Add("Convert to ANSI", null, (s, e) => ConvertEncoding("ANSI"));
            _encodingMenu.DropDownItems.Add("Convert to UTF-8", null, (s, e) => ConvertEncoding("UTF-8"));
            _encodingMenu.DropDownItems.Add("Convert to UTF-8-BOM", null, (s, e) => ConvertEncoding("UTF-8-BOM"));
            _encodingMenu.DropDownItems.Add("Convert to UTF-16 BE BOM", null, (s, e) => ConvertEncoding("UTF-16 BE BOM"));
            _encodingMenu.DropDownItems.Add("Convert to UTF-16 LE BOM", null, (s, e) => ConvertEncoding("UTF-16 LE BOM"));

            _encodingMenu.DropDownOpening += (s, e) => UpdateEncodingMenuChecks();

            var viewMenu = new ToolStripMenuItem("View");
            var zoomInItem = (ToolStripMenuItem)viewMenu.DropDownItems.Add("Zoom In", null, (s, e) => ZoomIn());
            zoomInItem.ShortcutKeys = Keys.F11;
            var zoomOutItem = (ToolStripMenuItem)viewMenu.DropDownItems.Add("Zoom Out", null, (s, e) => ZoomOut());
            zoomOutItem.ShortcutKeys = Keys.F12;
            var zoomResetItem = (ToolStripMenuItem)viewMenu.DropDownItems.Add("Reset Zoom (100%)", null, (s, e) => ZoomReset());
            zoomResetItem.ShortcutKeys = Keys.Control | Keys.D0;

            mainMenu.Items.Add(_fileMenu);
            mainMenu.Items.Add(editMenu);
            mainMenu.Items.Add(viewMenu);
            mainMenu.Items.Add(macroMenu);
            mainMenu.Items.Add(toolsMenu);
            mainMenu.Items.Add(_encodingMenu);
            mainMenu.Items.Add(helpMenu);

            // --- TOOLBAR (RIBBON) ---
            mainToolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = SystemColors.Control };
            mainToolbar.ImageScalingSize = new Size(24, 24);

            // --- New Document Button (drawn at runtime) ---
            btnNew = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_newdoc),
                ToolTipText = "New Document"
            };
            btnNew.Click += (s, e) => AddNewTab("new " + (tcDocuments.TabCount + 1));

            btnSave = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_save),
                ToolTipText = "Save Active Document",
                Enabled = false
            };
            btnSave.Click += (s, e) => SaveFile();

            btnSaveAll = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_saveall),
                ToolTipText = "Save All Modified Documents"
            };
            btnSaveAll.Click += (s, e) => SaveAllFiles();

            btnShowChars = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_showchars),
                ToolTipText = "Show All Characters",
                CheckOnClick = true,
                Checked = _showCharacters
            };
            btnShowChars.CheckedChanged += (s, e) => ToggleShowCharacters(btnShowChars.Checked);

            btnIndentGuide = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_indentguide),
                ToolTipText = "Show Indent Guides",
                CheckOnClick = true,
                Checked = _showIndentGuides
            };
            btnIndentGuide.CheckedChanged += (s, e) => ToggleIndentGuides(btnIndentGuide.Checked);

            btnColSelect = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_colselect),
                ToolTipText = "Toggle Column Selection Mode (Ctrl+Alt+A)",
                CheckOnClick = true
            };
            btnColSelect.CheckedChanged += (s, e) => ToggleColumnMode(btnColSelect.Checked);

            btnWordWrap = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_wordwrap),
                ToolTipText = "Toggle Word Wrap",
                CheckOnClick = true,
                Checked = _wordWrap
            };
            btnWordWrap.CheckedChanged += (s, e) => ToggleWordWrap(btnWordWrap.Checked);

            btnUndo = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_undo),
                ToolTipText = "Undo Last Action"
            };
            btnUndo.Click += (s, e) => GetActiveEditor()?.Undo();

            btnPlayMacro = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_playmacro),
                ToolTipText = "Playback Active Macro (Ctrl+Shift+P)",
                Enabled = false
            };
            btnPlayMacro.Click += (s, e) => PlaybackMacro(true);

            btnStartRecord = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = MakeRecordIcon(),
                ToolTipText = "Start Recording Macro"
            };
            btnStartRecord.Click += (s, e) => StartRecording();

            btnStopRecord = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = MakeStopIcon(),
                ToolTipText = "Stop Recording Macro",
                Enabled = false
            };
            btnStopRecord.Click += (s, e) => StopRecording();

            // NEW: Live Monitor Button
            // Using Text instead of Image to avoid missing resource errors. You can swap this to an Image icon later!
            btnLiveMonitor = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_livemonitor),
                ToolTipText = "Toggle Live File Monitoring (Auto-Reload on changes)",
                CheckOnClick = true
            };
            btnLiveMonitor.CheckedChanged += (s, e) => ToggleLiveMonitor(btnLiveMonitor.Checked);

            btnJsonTree = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_jsontree),
                ToolTipText = "Toggle JSON Tree Explorer Panel"
            };
            btnJsonTree.Click += (s, e) => ToggleJsonPanel();

            btnThemeToggle = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_themetoggle),
                ToolTipText = "Toggle Light/Dark Theme"
            };
            btnThemeToggle.Click += (s, e) => ToggleTheme();

            btnRevert = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_revert),
                ToolTipText = "Revert to Saved Version"
            };
            btnRevert.Click += (s, e) => RevertToSaved();

            btnHelp = new ToolStripButton()
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = IconFromBase64(_ico_userguide),
                ToolTipText = "View User's Guide"
            };
            btnHelp.Click += (s, e) => ShowUserGuide();

            mainToolbar.Items.AddRange(new ToolStripItem[] {
                btnNew, new ToolStripSeparator(),
                btnSave, btnSaveAll, btnRevert, new ToolStripSeparator(),
                btnShowChars, btnIndentGuide, btnWordWrap, btnColSelect, new ToolStripSeparator(),
                btnUndo, btnPlayMacro, btnStartRecord, btnStopRecord, new ToolStripSeparator(),
                btnLiveMonitor, btnJsonTree, new ToolStripSeparator(),
                btnThemeToggle, btnHelp
            });

            // --- STATUS BAR ---
            statusBar = new StatusStrip();
            statusBar.SizingGrip = false;

            lblLength = new ToolStripStatusLabel("Length: 0");
            lblLength.BorderSides = ToolStripStatusLabelBorderSides.Right;
            var spacer = new ToolStripStatusLabel("") { Spring = true };

            lblPosition = new ToolStripStatusLabel("Ln: 1  Col: 1  Pos: 0");
            lblPosition.BorderSides = ToolStripStatusLabelBorderSides.Left | ToolStripStatusLabelBorderSides.Right;

            lblEncoding = new ToolStripStatusLabel("UTF-8");
            lblEncoding.BorderSides = ToolStripStatusLabelBorderSides.Right;

            lblInsOvr = new ToolStripStatusLabel("INS");
            lblInsOvr.BorderSides = ToolStripStatusLabelBorderSides.Right;

            lblZoom = new ToolStripStatusLabel("100%");

            statusBar.Items.AddRange(new ToolStripItem[] {
                lblLength, spacer, lblPosition, lblEncoding, lblInsOvr, lblZoom
            });

            // --- TAB CONTROL ---
            tcDocuments = new TabControl
            {
                Dock = DockStyle.Fill,
                DrawMode = System.Windows.Forms.TabDrawMode.OwnerDrawFixed,
                Padding = new Point(24, 4)
            };
            tcDocuments.DrawItem += TcDocuments_DrawItem;
            tcDocuments.MouseDown += TcDocuments_MouseDown;
            tcDocuments.MouseMove += TcDocuments_MouseMove;
            tcDocuments.AllowDrop = true;
            tcDocuments.DragOver += TcDocuments_DragOver;
            tcDocuments.DragDrop += TcDocuments_DragDrop;
            tcDocuments.SelectedIndexChanged += (s, e) =>
            {
                _isSwitchingTabs = true; // Prevent event bubbling
                UpdateToolbarState();
                UpdateStatusBar(GetActiveEditor());

                // Update the Live Monitoring toggle visually based on the new active tab
                if (tcDocuments.SelectedTab != null)
                {
                    btnLiveMonitor.Checked = _fileWatchers.ContainsKey(tcDocuments.SelectedTab);
                    UpdateEncodingStatusLabel(GetTabEncoding(tcDocuments.SelectedTab));
                }

                _isSwitchingTabs = false;
            };

            this.MainMenuStrip = mainMenu;

            // --- JSON SIDE PANEL (right side, initially hidden) ---
            _jsonTree = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 11),
                BorderStyle = BorderStyle.None,
                Indent = 25
            };

            var btnCloseJson = new Button
            {
                Text = "✕",
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                Width = 28,
                Cursor = Cursors.Hand
            };
            btnCloseJson.FlatAppearance.BorderSize = 0;
            btnCloseJson.Click += (s, e) => { _mainSplit.Panel1Collapsed = true; };

            _jsonPanelHeader = new Label
            {
                Text = "  JSON Explorer",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Height = 28
            };

            var headerPanel = new Panel { Dock = DockStyle.Top, Height = 28 };
            headerPanel.Controls.Add(_jsonPanelHeader);
            headerPanel.Controls.Add(btnCloseJson);

            _jsonPanel = new Panel { Dock = DockStyle.Fill };
            _jsonPanel.Controls.Add(_jsonTree);
            _jsonPanel.Controls.Add(headerPanel);

            // --- SPLIT CONTAINER (JSON tree left, editor right) ---
            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Panel1Collapsed = true,
                SplitterWidth = 4,
                FixedPanel = FixedPanel.Panel1
            };

            _mainSplit.Panel1.Controls.Add(_jsonPanel);
            _mainSplit.Panel2.Controls.Add(tcDocuments);

            this.Controls.Add(_mainSplit);
            this.Controls.Add(mainToolbar);
            this.Controls.Add(statusBar);
            this.Controls.Add(mainMenu);

            this.BringToFront();
        }

        #region Live File Monitoring Engine

        private void ToggleLiveMonitor(bool enable)
        {
            // Do nothing if we are just visually changing the toggle due to switching tabs
            if (_isSwitchingTabs) return;

            var page = tcDocuments.SelectedTab;
            if (page == null) return;

            if (enable)
            {
                var editor = page.Controls[0] as Scintilla;
                string path = editor?.Tag as string;

                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    MessageBox.Show("Cannot monitor a new/unsaved file. Please save it to disk first.", "n+ - beta", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // Revert the button visually
                    _isSwitchingTabs = true;
                    btnLiveMonitor.Checked = false;
                    _isSwitchingTabs = false;
                    return;
                }

                // Create a background watcher for this specific file
                var watcher = new FileSystemWatcher
                {
                    Path = Path.GetDirectoryName(path),
                    Filter = Path.GetFileName(path),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };

                watcher.Changed += Fsw_Changed;
                watcher.EnableRaisingEvents = true;

                // Track the watcher so we can clean it up later
                _fileWatchers[page] = watcher;
            }
            else
            {
                // Disable and destroy the watcher
                if (_fileWatchers.TryGetValue(page, out var watcher))
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Changed -= Fsw_Changed;
                    watcher.Dispose();
                    _fileWatchers.Remove(page);
                }
            }

            tcDocuments.Invalidate(); // Refresh the UI to immediately apply the green tab color
        }

        private void Fsw_Changed(object sender, FileSystemEventArgs e)
        {
            // This event runs on a background thread. Delay briefly to let the writing application release its file lock.
            System.Threading.Thread.Sleep(100);

            // Find out which TabPage this watcher belongs to
            TabPage targetPage = null;
            foreach (var kvp in _fileWatchers)
            {
                if (kvp.Value == sender)
                {
                    targetPage = kvp.Key;
                    break;
                }
            }

            if (targetPage == null) return;

            // Push the update back to the main UI thread safely
            this.BeginInvoke((MethodInvoker)delegate
            {
                if (!tcDocuments.TabPages.Contains(targetPage)) return;
                var editor = targetPage.Controls[0] as Scintilla;
                if (editor == null) return;

                try
                {
                    // Read file safely, allowing other applications concurrent access
                    using (var fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                    {
                        string text = sr.ReadToEnd();

                        // Temporarily lift read-only so the text can be updated
                        bool wasReadOnly = editor.ReadOnly;
                        if (wasReadOnly) editor.ReadOnly = false;

                        editor.Text = text;

                        // Always scroll to the bottom during live monitoring so the user
                        // can tail log files without having to manually scroll down.
                        editor.GotoPosition(editor.TextLength);
                        editor.ScrollCaret();

                        // Clear the "Unsaved" asterisk since we just pulled the latest disk copy
                        targetPage.Text = Path.GetFileName(e.FullPath);
                        if (wasReadOnly) targetPage.Text += " [READ-ONLY]";
                        editor.SetSavePoint();

                        if (wasReadOnly) editor.ReadOnly = true;
                    }
                }
                catch { /* Wait silently; FSW usually fires multiple times, the next one will succeed when the lock lifts */ }
            });
        }

        #endregion

        #region External File Change Detection

        private bool _fileChangePromptActive = false;

        private void StartFileChangeWatch(TabPage page, string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            // Remove any existing watcher for this tab
            StopFileChangeWatch(page);

            try
            {
                var watcher = new FileSystemWatcher
                {
                    Path = Path.GetDirectoryName(filePath),
                    Filter = Path.GetFileName(filePath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                };

                watcher.Changed += FileChange_Changed;
                watcher.Deleted += FileChange_Deleted;
                watcher.Renamed += FileChange_Renamed;
                watcher.EnableRaisingEvents = true;

                _fileChangeWatchers[page] = watcher;
            }
            catch { /* Ignore - file may be on a path that doesn't support watching */ }
        }

        private void StopFileChangeWatch(TabPage page)
        {
            if (_fileChangeWatchers.TryGetValue(page, out var watcher))
            {
                _fileChangeWatchers.Remove(page);
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Changed -= FileChange_Changed;
                    watcher.Deleted -= FileChange_Deleted;
                    watcher.Renamed -= FileChange_Renamed;
                    watcher.Dispose();
                }
                catch (ObjectDisposedException) { /* Already disposed */ }
            }
        }

        private TabPage FindPageForChangeWatcher(object sender)
        {
            foreach (var kvp in _fileChangeWatchers)
            {
                if (kvp.Value == sender) return kvp.Key;
            }
            return null;
        }

        private void FileChange_Changed(object sender, FileSystemEventArgs e)
        {
            System.Threading.Thread.Sleep(150);

            try { this.BeginInvoke((MethodInvoker)delegate { HandleFileChanged(sender, e); }); }
            catch { /* Form may be disposed */ }
        }

        private void FileChange_Deleted(object sender, FileSystemEventArgs e)
        {
            System.Threading.Thread.Sleep(100);

            try { this.BeginInvoke((MethodInvoker)delegate { HandleFileDeleted(sender, e); }); }
            catch { /* Form may be disposed */ }
        }

        private void FileChange_Renamed(object sender, RenamedEventArgs e)
        {
            System.Threading.Thread.Sleep(100);

            try { this.BeginInvoke((MethodInvoker)delegate { HandleFileRenamed(sender, e); }); }
            catch { /* Form may be disposed */ }
        }

        private void HandleFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_fileChangePromptActive) return;

            var page = FindPageForChangeWatcher(sender);
            if (page == null || !tcDocuments.TabPages.Contains(page)) return;
            if (_fileWatchers.ContainsKey(page)) return;

            var editor = page.Controls[0] as Scintilla;
            if (editor == null) return;

            // Disable watcher while prompt is open
            try
            {
                if (_fileChangeWatchers.TryGetValue(page, out var w))
                    w.EnableRaisingEvents = false;
            }
            catch (ObjectDisposedException) { return; }

            _fileChangePromptActive = true;
            try
            {
                var res = MessageBox.Show(
                    $"The file \"{Path.GetFileName(e.FullPath)}\" has been modified by another program.\n\nDo you want to reload it?",
                    "n+ - File Changed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (res == DialogResult.Yes)
                {
                    try { LoadFileIntoEditor(editor, page, e.FullPath); }
                    catch { /* Ignore read errors */ }
                }
                else
                {
                    // Re-enable watcher
                    try
                    {
                        if (_fileChangeWatchers.TryGetValue(page, out var w2))
                            w2.EnableRaisingEvents = true;
                    }
                    catch (ObjectDisposedException) { }
                }
            }
            finally { _fileChangePromptActive = false; }
        }

        private void HandleFileDeleted(object sender, FileSystemEventArgs e)
        {
            if (_fileChangePromptActive) return;

            var page = FindPageForChangeWatcher(sender);
            if (page == null || !tcDocuments.TabPages.Contains(page)) return;
            if (_fileWatchers.ContainsKey(page)) return;

            var editor = page.Controls[0] as Scintilla;
            if (editor == null) return;

            _fileChangePromptActive = true;
            try
            {
                var res = MessageBox.Show(
                    $"The file \"{Path.GetFileName(e.FullPath)}\" has been deleted or moved.\n\nDo you want to close this tab?",
                    "n+ - File Removed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (res == DialogResult.Yes)
                {
                    StopFileChangeWatch(page);
                    int idx = tcDocuments.TabPages.IndexOf(page);
                    if (idx >= 0) CloseTab(idx);
                }
                else
                {
                    if (!page.Text.EndsWith("*")) page.Text += "*";
                    editor.Tag = null;
                    StopFileChangeWatch(page);
                }
            }
            finally { _fileChangePromptActive = false; }
        }

        private void HandleFileRenamed(object sender, RenamedEventArgs e)
        {
            if (_fileChangePromptActive) return;

            var page = FindPageForChangeWatcher(sender);
            if (page == null || !tcDocuments.TabPages.Contains(page)) return;
            if (_fileWatchers.ContainsKey(page)) return;

            var editor = page.Controls[0] as Scintilla;
            if (editor == null) return;

            _fileChangePromptActive = true;
            try
            {
                var res = MessageBox.Show(
                    $"The file \"{Path.GetFileName(e.OldFullPath)}\" has been renamed to \"{Path.GetFileName(e.FullPath)}\".\n\nDo you want to update the tab to track the new name?",
                    "n+ - File Renamed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (res == DialogResult.Yes)
                {
                    editor.Tag = e.FullPath;
                    page.Text = Path.GetFileName(e.FullPath);
                    StopFileChangeWatch(page);
                    StartFileChangeWatch(page, e.FullPath);
                }
                else
                {
                    if (!page.Text.EndsWith("*")) page.Text += "*";
                    editor.Tag = null;
                    StopFileChangeWatch(page);
                }
            }
            finally { _fileChangePromptActive = false; }
        }

        #endregion

        #region Session Management & Settings

        private void LoadSettings()
        {
            if (!File.Exists(_settingsFilePath)) return;

            try
            {
                string[] lines = File.ReadAllLines(_settingsFilePath);
                if (lines.Length >= 2)
                {
                    bool.TryParse(lines[0], out _isDarkMode);
                    bool.TryParse(lines[1], out _wordWrap);
                }
                if (lines.Length >= 4)
                {
                    bool.TryParse(lines[2], out _showCharacters);
                    bool.TryParse(lines[3], out _showIndentGuides);
                }
                if (lines.Length >= 8)
                {
                    int x, y, w, h;
                    if (int.TryParse(lines[4], out x) && int.TryParse(lines[5], out y) &&
                        int.TryParse(lines[6], out w) && int.TryParse(lines[7], out h))
                    {
                        var savedBounds = new Rectangle(x, y, w, h);

                        // Verify the saved position is still on a visible screen
                        bool onScreen = false;
                        foreach (var screen in Screen.AllScreens)
                        {
                            if (screen.WorkingArea.IntersectsWith(savedBounds)) { onScreen = true; break; }
                        }

                        if (onScreen)
                        {
                            this.StartPosition = FormStartPosition.Manual;
                            this.Location = new Point(x, y);
                            this.Size = new Size(w, h);
                        }
                    }
                }
                if (lines.Length >= 9)
                {
                    bool maximized;
                    if (bool.TryParse(lines[8], out maximized) && maximized)
                    {
                        _restoreMaximized = true;
                    }
                }
                if (lines.Length >= 10)
                {
                    float zoom;
                    if (float.TryParse(lines[9], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out zoom))
                    {
                        if (zoom >= ZoomMin && zoom <= ZoomMax) _zoomLevel = zoom;
                    }
                }
            }
            catch { /* Ignore corrupt settings file */ }
        }

        private void LoadMacros()
        {
            _savedMacros.Clear();
            if (!File.Exists(_macrosFilePath)) return;

            try
            {
                string json = File.ReadAllText(_macrosFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, List<MacroStep>>>(json);
                if (data != null) _savedMacros = data;
            }
            catch { /* Ignore corrupt macros file */ }
        }

        private void SaveMacros()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_savedMacros, options);
                File.WriteAllText(_macrosFilePath, json);
            }
            catch { /* Ignore write errors */ }
        }

        private void LoadRecentFiles()
        {
            _recentFiles.Clear();
            if (!File.Exists(_recentFilesPath)) return;

            try
            {
                var lines = File.ReadAllLines(_recentFilesPath);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line) && _recentFiles.Count < MaxRecentFiles)
                        _recentFiles.Add(line.Trim());
                }
            }
            catch { /* Ignore corrupt recent files */ }
        }

        private void SaveRecentFiles()
        {
            try { File.WriteAllLines(_recentFilesPath, _recentFiles); }
            catch { /* Ignore write errors */ }
        }

        private void AddToRecentFiles(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            _recentFiles.Remove(filePath);
            _recentFiles.Insert(0, filePath);

            if (_recentFiles.Count > MaxRecentFiles)
                _recentFiles.RemoveRange(MaxRecentFiles, _recentFiles.Count - MaxRecentFiles);

            SaveRecentFiles();
            RebuildRecentFilesMenu();
        }

        private void RebuildRecentFilesMenu()
        {
            // Remove old recent file items (everything after "Exit")
            const string recentTag = "RECENT_FILE_ITEM";
            for (int i = _fileMenu.DropDownItems.Count - 1; i >= 0; i--)
            {
                if (_fileMenu.DropDownItems[i].Tag as string == recentTag)
                    _fileMenu.DropDownItems.RemoveAt(i);
            }

            if (_recentFiles.Count == 0) return;

            // Find the index of Exit to insert before it
            int exitIndex = -1;
            for (int i = 0; i < _fileMenu.DropDownItems.Count; i++)
            {
                if (_fileMenu.DropDownItems[i].Text == "Exit") { exitIndex = i; break; }
            }
            if (exitIndex < 0) return;

            // Insert a separator before Exit, then the recent files
            var sep = new ToolStripSeparator { Tag = recentTag };
            _fileMenu.DropDownItems.Insert(exitIndex, sep);

            for (int i = 0; i < _recentFiles.Count; i++)
            {
                string path = _recentFiles[i];
                string label = $"{i + 1}. {Path.GetFileName(path)}";
                var item = new ToolStripMenuItem(label) { Tag = recentTag, ToolTipText = path };
                item.Click += (s, e) => OpenRecentFile(path);
                _fileMenu.DropDownItems.Insert(exitIndex + 1 + i, item);
            }
        }

        private void OpenRecentFile(string path)
        {
            if (!File.Exists(path))
            {
                var res = MessageBox.Show($"File not found:\n{path}\n\nRemove from recent list?", "n+ - beta", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (res == DialogResult.Yes)
                {
                    _recentFiles.Remove(path);
                    SaveRecentFiles();
                    RebuildRecentFilesMenu();
                }
                return;
            }

            AddNewTab("Loading...", path);
            LoadFileIntoEditor(GetActiveEditor(), tcDocuments.SelectedTab, path);
            AddToRecentFiles(path);
        }

        private void FormatJson()
        {
            var editor = GetActiveEditor();
            if (editor == null || string.IsNullOrWhiteSpace(editor.Text)) return;

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                using (JsonDocument doc = JsonDocument.Parse(editor.Text))
                {
                    editor.BeginUndoAction();
                    editor.Text = JsonSerializer.Serialize(doc.RootElement, options);
                    editor.EndUndoAction();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid JSON format.\n\nError: {ex.Message}", "nplus JSON Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void ToggleJsonPanel()
        {
            if (!_mainSplit.Panel1Collapsed)
            {
                _mainSplit.Panel1Collapsed = true;
            }
            else
            {
                ShowJsonTree();
            }
        }

        private void ShowJsonTree()
        {
            var editor = GetActiveEditor();
            if (editor == null || string.IsNullOrWhiteSpace(editor.Text)) return;

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(editor.Text))
                {
                    _jsonTree.Nodes.Clear();
                    ApplyJsonPanelTheme();

                    TreeNode root = new TreeNode("JSON Payload");
                    _jsonTree.Nodes.Add(root);
                    BuildJsonTree(doc.RootElement, root);
                    root.Expand();

                    _mainSplit.Panel1Collapsed = false;
                    if (_mainSplit.Width > 0)
                        _mainSplit.SplitterDistance = (int)(_mainSplit.Width * 0.35);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot generate tree from invalid JSON.\n\nError: {ex.Message}", "nplus JSON Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildJsonTree(JsonElement element, TreeNode parentNode)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty prop in element.EnumerateObject())
                    {
                        TreeNode child = new TreeNode(prop.Name);
                        parentNode.Nodes.Add(child);
                        BuildJsonTree(prop.Value, child);
                    }
                    break;
                case JsonValueKind.Array:
                    int index = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        TreeNode child = new TreeNode($"[{index}]");
                        parentNode.Nodes.Add(child);
                        BuildJsonTree(item, child);
                        index++;
                    }
                    break;
                case JsonValueKind.String:
                    parentNode.Text += $" : \"{element.GetString()}\"";
                    if (_isDarkMode) parentNode.ForeColor = Color.Khaki;
                    break;
                case JsonValueKind.Number:
                    parentNode.Text += $" : {element.GetRawText()}";
                    if (_isDarkMode) parentNode.ForeColor = Color.Olive;
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    parentNode.Text += $" : {element.GetRawText()}";
                    if (_isDarkMode) parentNode.ForeColor = Color.DeepPink;
                    break;
            }
        }

        private void ApplyJsonPanelTheme()
        {
            if (_isDarkMode)
            {
                _jsonPanel.BackColor = Color.FromArgb(30, 30, 35);
                _jsonTree.BackColor = Color.FromArgb(30, 30, 35);
                _jsonTree.ForeColor = Color.LightSkyBlue;
                _jsonPanelHeader.BackColor = Color.FromArgb(40, 40, 45);
                _jsonPanelHeader.ForeColor = Color.Gainsboro;
                _jsonPanelHeader.Parent.BackColor = Color.FromArgb(40, 40, 45);
                foreach (Control c in _jsonPanelHeader.Parent.Controls)
                {
                    if (c is Button btn) { btn.ForeColor = Color.Gainsboro; btn.BackColor = Color.FromArgb(40, 40, 45); }
                }
            }
            else
            {
                _jsonPanel.BackColor = SystemColors.Window;
                _jsonTree.BackColor = SystemColors.Window;
                _jsonTree.ForeColor = SystemColors.WindowText;
                _jsonPanelHeader.BackColor = SystemColors.Control;
                _jsonPanelHeader.ForeColor = SystemColors.ControlText;
                _jsonPanelHeader.Parent.BackColor = SystemColors.Control;
                foreach (Control c in _jsonPanelHeader.Parent.Controls)
                {
                    if (c is Button btn) { btn.ForeColor = SystemColors.ControlText; btn.BackColor = SystemColors.Control; }
                }
            }
        }

        private void ApplyJsonLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.Json;

            // Base colors
            editor.Styles[Style.Json.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Json.Operator].ForeColor = _isDarkMode ? Color.Silver : Color.Black;

            // JSON specific elements
            editor.Styles[Style.Json.PropertyName].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.Styles[Style.Json.String].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Json.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;

            // Boolean values and null
            editor.Styles[Style.Json.Keyword].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.SetKeywords(0, "true false null");

            // Comments (JSON strictly doesn't support them, but many config files like JSON5 do)
            editor.Styles[Style.Json.LineComment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Json.BlockComment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;

            // Enable code folding for nested JSON objects and arrays { } [ ]
            editor.SetProperty("fold", "1");
            editor.SetProperty("fold.compact", "1");
        }

        private void SaveSettings()
        {
            try
            {
                // Save window bounds from RestoreBounds if maximized, otherwise current bounds
                var bounds = this.WindowState == FormWindowState.Normal ? this.Bounds : this.RestoreBounds;

                File.WriteAllLines(_settingsFilePath, new string[]
                {
                    _isDarkMode.ToString(),
                    _wordWrap.ToString(),
                    _showCharacters.ToString(),
                    _showIndentGuides.ToString(),
                    bounds.X.ToString(),
                    bounds.Y.ToString(),
                    bounds.Width.ToString(),
                    bounds.Height.ToString(),
                    (this.WindowState == FormWindowState.Maximized).ToString(),
                    _zoomLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            }
            catch { /* Ignore write errors */ }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSession();
            SaveSettings();
            SaveMacros();

            // Clean up any active background watchers
            foreach (var watcher in _fileWatchers.Values) watcher.Dispose();
            _fileWatchers.Clear();
            foreach (var watcher in _fileChangeWatchers.Values)
            {
                try { watcher.Dispose(); } catch (ObjectDisposedException) { }
            }
            _fileChangeWatchers.Clear();

            base.OnFormClosing(e);
        }

        private void SaveSession()
        {
            List<string> sessionLines = new List<string>();

            try
            {
                DirectoryInfo di = new DirectoryInfo(_backupFolderPath);
                foreach (FileInfo file in di.GetFiles()) file.Delete();
            }
            catch { /* Ignore if files are locked */ }

            int counter = 0;
            foreach (TabPage page in tcDocuments.TabPages)
            {
                var editor = page.Controls[0] as Scintilla;
                if (editor == null) continue;

                string originalPath = editor.Tag as string ?? "";
                string tabTitle = page.Text;
                string backupPath = "";

                if (tabTitle.EndsWith("*") || string.IsNullOrEmpty(originalPath))
                {
                    backupPath = Path.Combine(_backupFolderPath, $"backup_{counter}.tmp");
                    try { File.WriteAllText(backupPath, editor.Text); } catch { }
                }

                sessionLines.Add($"{originalPath}|{backupPath}|{tabTitle}");
                counter++;
            }

            try { File.WriteAllLines(_sessionFilePath, sessionLines); } catch { }
        }

        private void LoadSession()
        {
            if (!File.Exists(_sessionFilePath)) return;

            try
            {
                string[] lines = File.ReadAllLines(_sessionFilePath);
                bool loadedAny = false;

                foreach (string line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length != 3) continue;

                    string originalPath = parts[0];
                    string backupPath = parts[1];
                    string tabTitle = parts[2];

                    string displayTitle = tabTitle.TrimEnd('*');
                    if (string.IsNullOrEmpty(originalPath) && string.IsNullOrEmpty(displayTitle)) displayTitle = "new 1";

                    AddNewTab(displayTitle, string.IsNullOrEmpty(originalPath) ? null : originalPath);
                    var editor = GetActiveEditor();
                    var page = tcDocuments.SelectedTab;

                    if (!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
                    {
                        editor.Text = File.ReadAllText(backupPath);
                        if (!page.Text.EndsWith("*")) page.Text += "*";
                    }
                    else if (!string.IsNullOrEmpty(originalPath) && File.Exists(originalPath))
                    {
                        LoadFileIntoEditor(editor, page, originalPath);
                    }

                    loadedAny = true;
                }

                if (loadedAny && tcDocuments.TabPages.Count > 1 && tcDocuments.TabPages[0].Text == "new 1")
                {
                    var firstEditor = tcDocuments.TabPages[0].Controls[0] as Scintilla;
                    if (firstEditor != null && string.IsNullOrEmpty(firstEditor.Tag as string) && firstEditor.Text == "")
                    {
                        tcDocuments.TabPages.RemoveAt(0);
                    }
                }
            }
            catch { /* Ignore corrupt session file */ }
        }

        #endregion

        #region Hex Viewer & File Loaders
        private bool IsBinaryFile(string filePath)
        {
            try
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int bytesToCheck = 8192;
                    byte[] buffer = new byte[bytesToCheck];
                    int bytesRead = stream.Read(buffer, 0, bytesToCheck);

                    // UTF-16 files contain null bytes normally — check for BOM first
                    if (bytesRead >= 2)
                    {
                        if (buffer[0] == 0xFF && buffer[1] == 0xFE) return false; // UTF-16 LE
                        if (buffer[0] == 0xFE && buffer[1] == 0xFF) return false; // UTF-16 BE
                    }

                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (buffer[i] == 0x00) return true;
                    }
                }
            }
            catch { /* Fallback to text if locked */ }
            return false;
        }

        private void LoadFileIntoEditor(Scintilla editor, TabPage page, string path)
        {
            if (!File.Exists(path)) return;

            if (IsBinaryFile(path))
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(path); }
                catch (IOException)
                {
                    // File is locked — read via shared access
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                }

                System.Text.StringBuilder hexBuilder = new System.Text.StringBuilder(bytes.Length * 4);

                for (int i = 0; i < bytes.Length; i += 16)
                {
                    hexBuilder.Append($"{i:X8}  ");
                    for (int j = 0; j < 16; j++)
                    {
                        if (i + j < bytes.Length) hexBuilder.Append($"{bytes[i + j]:X2} ");
                        else hexBuilder.Append("   ");
                        if (j == 7) hexBuilder.Append(" ");
                    }

                    hexBuilder.Append(" |");
                    for (int j = 0; j < 16; j++)
                    {
                        if (i + j < bytes.Length)
                        {
                            char c = (char)bytes[i + j];
                            if (char.IsControl(c)) hexBuilder.Append(".");
                            else hexBuilder.Append(c);
                        }
                    }
                    hexBuilder.AppendLine("|");
                }

                editor.Text = hexBuilder.ToString();
                editor.ReadOnly = true;
                page.Text = Path.GetFileName(path) + " [HEX]";
                editor.Lexer = Lexer.Null;
            }
            else
            {
                bool openedReadOnly = false;
                string text;
                Encoding detectedEncoding;

                try
                {
                    detectedEncoding = DetectFileEncoding(path);
                    text = File.ReadAllText(path, detectedEncoding);
                }
                catch (IOException)
                {
                    // File is locked by another process — read via shared access, open as read-only
                    openedReadOnly = true;
                    detectedEncoding = DetectFileEncoding(path);
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, detectedEncoding))
                    {
                        text = sr.ReadToEnd();
                    }
                }

                editor.ReadOnly = false;
                editor.Text = text;

                if (openedReadOnly)
                {
                    editor.ReadOnly = true;
                    page.Text = Path.GetFileName(path) + " [READ-ONLY]";
                }
                else
                {
                    page.Text = Path.GetFileName(path);
                }

                _tabEncodings[page] = detectedEncoding;
                UpdateEncodingStatusLabel(detectedEncoding);
                editor.SetSavePoint();
                editor.EmptyUndoBuffer();
            }

            // Start watching for external changes to this file
            StartFileChangeWatch(page, path);
        }
        #endregion

        private void ShowFindDialog(int tabIndex)
        {
            if (_findDialog == null || _findDialog.IsDisposed)
            {
                _findDialog = new FindReplaceDialog(this);
            }
            _findDialog.Show();
            _findDialog.Focus();
            _findDialog.SetMode(tabIndex);
        }

        private void FindNextFromEditor()
        {
            if (_findDialog != null && !_findDialog.IsDisposed)
            {
                _findDialog.FindNext();
            }
            else
            {
                ShowFindDialog(0);
            }
        }

        public Scintilla GetActiveEditor() => tcDocuments.SelectedTab?.Controls[0] as Scintilla;

        public void RecordMacroStep(MacroStep step)
        {
            if (_isRecording) _currentMacro.Add(step);
        }

        #region UI & Toolbar Functionality

        private void ShowUserGuide()
        {
            AddNewTab("User Guide.txt");
            var editor = GetActiveEditor();

            editor.Text = @"========================================================================
                 n+ - beta
                 USER'S GUIDE
========================================================================

1. SESSION SNAPSHOTS (HOT EXIT)
   - n+ automatically saves your work in the background.
   - You can safely close the application window at any time. The next 
     time you open n+, all your tabs, including unsaved changes, 
     will be restored exactly as you left them.
   - The window size, position, and maximized state are also saved 
     and restored between sessions.
   - Note: Closing an *individual* tab will still prompt you to save.

2. SYNTAX HIGHLIGHTING & FILE SUPPORT
   - n+ automatically detects and highlights syntax for:
     C#, C/C++, Java, JavaScript/TypeScript, Python, SQL, 
     Visual Basic (.vb/.bas), VBScript (.vbs), PowerShell (.ps1),
     PHP, HTML, XML/XAML, JSON, and YAML (.yml/.yaml).
   - Binary/Executable files are safely opened in a Read-Only Hex View.
   - Files locked by another process are opened in Read-Only mode,
     with '[READ-ONLY]' shown in the tab title.

3. MACRO RECORDING SYSTEM
   - Click 'Macro -> Start Recording' to begin capturing your inputs.
   - Type text, use Enter, Backspace, Delete, arrow keys, Home, End, 
     Tab, and Page Up/Down — all are captured with Ctrl/Shift modifiers.
   - Search & Replace actions are also recorded in macros.
   - Click 'Macro -> Stop Recording' when finished.
   - Press [Ctrl+Shift+P] or click the green Play button to playback.
   - Use 'Run a Macro Multiple Times' to process entire log files.
   - Use 'Load Saved Macro...' to load a previously saved macro.
   - Use 'Edit Macro Steps...' to view, reorder, add, delete, or 
     modify individual steps in any macro.
   - Saved macros persist between sessions automatically.

4. COLUMN SELECTION MODE (Ctrl+Alt+A)
   - Toggle the ▤ Column Mode on the ribbon toolbar.
   - Click and drag your mouse, or hold Shift + Arrow Keys, to draw a 
     rectangular box over your text.
   - Type to insert characters on multiple lines simultaneously!

5. FIND / REPLACE / MARK ENGINE (Ctrl+F, Ctrl+H, Ctrl+B)
   - Supports Normal, Extended (\n, \t), and Regular Expressions.
   - The 'Mark' tab allows you to highlight all occurrences of a string
     and optionally drop a blue bookmark on every line it finds.
   - Selected text is auto-populated into the Find field when opened.
   - The Find and Replace fields remember recent search terms in a 
     dropdown history (up to 20 entries per session).

6. BOOKMARK SYSTEM (Ctrl+F2)
   - Press Ctrl+F2 to toggle a bookmark on your current line.
   - Jump forward between bookmarks with F2, and backward with Shift+F2.
   - Go to 'Edit -> Bookmark' to copy, cut, or delete bookmarked lines.

7. LINE OPERATIONS (Ctrl+J, Ctrl+Shift+Up/Down)
   - Highlight lines and select 'Sort Lexicographically'.
   - Press Ctrl+J to 'Join Lines' (removes line breaks in the selection).
   - Use Ctrl+Shift+Up/Down to quickly shift a line through your code.

8. BLANK OPERATIONS (Edit Menu)
   - Trim Trailing / Leading / Both whitespace from every line.
   - EOL to Space joins all lines into one with spaces between them.
   - Convert TABs to spaces or spaces to TABs (all or leading only).
   - All operations support undo.

9. THEME ENGINE & WORD WRAP
   - Click the Toggle Theme icon to switch between Dark Matrix Mode 
     and Standard Light Mode. Syntax highlighting updates automatically.
   - Click the Word Wrap icon to wrap text to the visible window edge.

10. ZOOM (View Menu)
   - F11 to zoom in, F12 to zoom out, Ctrl + 0 to reset.
   - Scales the menu, tabs, toolbar, status bar, and all editor content.
   - Zoom level persists between sessions.
   - Editor-only zoom: Use Ctrl + Mouse Wheel to zoom in or out on 
     just the current editor tab. This does not affect the menu, 
     toolbar, or other tabs.

11. JSON TOOLS
   - Go to 'Tools -> Format / Pretty Print JSON' to cleanly indent and
     format dense or single-line JSON payloads.
   - Go to 'Tools -> View JSON in Visual Tree' or click the JSON tree 
     icon on the toolbar to open a collapsible tree explorer in a 
     docked panel on the left side of the editor.
   - Click the toolbar icon again to close the panel, or use the ✕ 
     button in the panel header. The splitter is draggable to resize.

12. LIVE FILE MONITORING (TAIL)
   - Click the 'Live' icon on the toolbar to auto-reload the current 
     file whenever it is modified by an external program.
   - The tab will turn green, and new content will always auto-scroll 
     to the bottom so you can tail rolling log files hands-free.
   - Toggle the icon off to stop monitoring and resume normal editing.

13. NEW DOCUMENT TOOLBAR BUTTON
   - The first button on the toolbar (page with a '+' icon) creates a 
     new empty tab, identical to 'File -> New'.

14. REVERT TO SAVED
   - Click the blue circular arrow icon on the toolbar to reload the 
     current file from disk, discarding all unsaved changes.
   - You will be prompted to confirm before reverting.

15. TAB REORDERING (DRAG AND DROP)
   - Click and drag any open file tab to reposition it along the tab bar.
   - Tabs move in real-time as you drag, giving live visual feedback.
   - The close button on each tab continues to work as normal.

16. RECENT FILES (File Menu)
   - The File menu shows up to 10 recently opened files above Exit.
   - Click any entry to re-open it. If the file no longer exists, n+ 
     will offer to remove it from the list.
   - The list is saved between sessions automatically.

17. ENCODING (Encoding Menu)
   - Supports ANSI, UTF-8, UTF-8 with BOM, UTF-16 BE BOM, and 
     UTF-16 LE BOM encodings.
   - The current encoding is auto-detected when a file is opened and 
     shown in the status bar.
   - Select an encoding at the top of the menu to change how the 
     file will be saved (without converting existing content).
   - Use 'Convert to...' options to re-encode the actual text content.
   - A bullet marker indicates the current encoding in the menu.

========================================================================";

            editor.ReadOnly = true;
            tcDocuments.SelectedTab.Text = "User Guide.txt";
            UpdateToolbarState();
        }

        private void ToggleTheme()
        {
            _isDarkMode = !_isDarkMode;
            ApplyThemeToForm();

            foreach (TabPage page in tcDocuments.TabPages)
            {
                if (page.Controls[0] is Scintilla editor)
                {
                    string path = editor.Tag as string;
                    ApplySyntaxHighlighting(editor, path);
                }
            }
            tcDocuments.Invalidate();

            // Update the JSON side panel theme if it's visible
            if (!_mainSplit.Panel1Collapsed) ApplyJsonPanelTheme();
        }

        private void ApplyThemeToForm()
        {
            this.BackColor = _isDarkMode ? Color.FromArgb(30, 30, 30) : SystemColors.Control;

            // Update the new Status Bar colors based on the theme
            statusBar.BackColor = _isDarkMode ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
            statusBar.ForeColor = _isDarkMode ? Color.LightGray : Color.Black;
        }

        private void UpdateToolbarState()
        {
            if (tcDocuments.SelectedTab == null)
            {
                btnSave.Enabled = false;
                return;
            }
            btnSave.Enabled = tcDocuments.SelectedTab.Text.EndsWith("*");
            btnPlayMacro.Enabled = _currentMacro.Count > 0 && !_isRecording;
        }

        private void ToggleShowCharacters(bool show)
        {
            _showCharacters = show;
            btnShowChars.BackColor = show ? Color.LightSkyBlue : SystemColors.Control;

            foreach (TabPage page in tcDocuments.TabPages)
            {
                if (page.Controls[0] is Scintilla editor)
                {
                    editor.ViewWhitespace = show ? WhitespaceMode.VisibleAlways : WhitespaceMode.Invisible;
                    editor.ViewEol = show;
                }
            }
        }

        private void ToggleIndentGuides(bool show)
        {
            _showIndentGuides = show;
            btnIndentGuide.BackColor = show ? Color.LightSkyBlue : SystemColors.Control;

            foreach (TabPage page in tcDocuments.TabPages)
            {
                if (page.Controls[0] is Scintilla editor)
                {
                    editor.IndentationGuides = show ? IndentView.LookBoth : IndentView.None;
                }
            }
        }

        private void ToggleWordWrap(bool enable)
        {
            _wordWrap = enable;
            btnWordWrap.BackColor = enable ? Color.LightSkyBlue : SystemColors.Control;

            foreach (TabPage page in tcDocuments.TabPages)
            {
                if (page.Controls[0] is Scintilla editor)
                {
                    editor.WrapMode = enable ? WrapMode.Word : WrapMode.None;
                }
            }
        }

        private void ToggleColumnMode(bool enable)
        {
            btnColSelect.BackColor = enable ? Color.LightSkyBlue : SystemColors.Control;

            foreach (TabPage page in tcDocuments.TabPages)
            {
                if (page.Controls[0] is Scintilla editor)
                {
                    editor.MultipleSelection = true;
                    editor.AdditionalSelectionTyping = true;
                    editor.DirectMessage(2422, new IntPtr(enable ? 1 : 0), IntPtr.Zero);
                }
            }
        }

        private void RevertToSaved()
        {
            var editor = GetActiveEditor();
            var page = tcDocuments.SelectedTab;
            if (editor == null || page == null) return;

            string path = editor.Tag as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show("This file has no saved version on disk.", "nplus", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var res = MessageBox.Show($"Revert to the last saved version of:\n{Path.GetFileName(path)}?\n\nAll unsaved changes will be lost.", "Confirm Revert", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (res != DialogResult.Yes) return;

            LoadFileIntoEditor(editor, page, path);
        }

        private void ZoomIn()
        {
            if (_zoomLevel < ZoomMax)
            {
                _zoomLevel += ZoomStep;
                ApplyZoom();
            }
        }

        private void ZoomOut()
        {
            if (_zoomLevel > ZoomMin)
            {
                _zoomLevel -= ZoomStep;
                ApplyZoom();
            }
        }

        private void ZoomReset()
        {
            _zoomLevel = 1.0f;
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            _zoomLevel = (float)Math.Round(_zoomLevel, 2);
            this.SuspendLayout();

            float menuSize = Math.Max(7, _baseMenuFontSize * _zoomLevel);
            float tabSize = Math.Max(7, _baseTabFontSize * _zoomLevel);
            float editorSize = Math.Max(8, _baseEditorFontSize * _zoomLevel);
            var uiFont = new Font("Segoe UI", menuSize);

            // Menu bar — set font on the strip AND each item
            mainMenu.Font = uiFont;
            foreach (ToolStripItem item in mainMenu.Items)
            {
                item.Font = uiFont;
                if (item is ToolStripMenuItem mi)
                    foreach (ToolStripItem sub in mi.DropDownItems)
                        sub.Font = uiFont;
            }

            // Toolbar — scale icon sizes and font
            int iconSize = Math.Max(16, (int)(24 * _zoomLevel));
            mainToolbar.ImageScalingSize = new Size(iconSize, iconSize);
            mainToolbar.Font = uiFont;
            mainToolbar.AutoSize = true;
            mainToolbar.PerformLayout();

            // Tab control
            tcDocuments.Font = new Font(tcDocuments.Font.FontFamily, tabSize);
            int tabPadX = Math.Max(18, (int)(24 * _zoomLevel));
            int tabPadY = Math.Max(4, (int)(4 * _zoomLevel));
            tcDocuments.Padding = new Point(tabPadX, tabPadY);

            // Status bar
            statusBar.Font = uiFont;
            foreach (ToolStripItem item in statusBar.Items) item.Font = uiFont;
            statusBar.PerformLayout();

            // All open editors
            foreach (TabPage page in tcDocuments.TabPages)
            {
                if (page.Controls[0] is Scintilla editor)
                {
                    editor.Styles[Style.Default].Size = (int)editorSize;
                    string path = editor.Tag as string;
                    ApplySyntaxHighlighting(editor, path);
                }
            }

            this.ResumeLayout(true);
            tcDocuments.Invalidate();

            // Update zoom indicator in the status bar
            int pct = (int)Math.Round(_zoomLevel * 100);
            lblZoom.Text = $"{pct}%";
        }
        #endregion

        #region Macro Engine
        private void StartRecording()
        {
            _currentMacro.Clear();
            _isRecording = true;
            startRecordItem.Enabled = false;
            stopRecordItem.Enabled = true;
            btnStartRecord.Enabled = false;
            btnStopRecord.Enabled = true;
            btnPlayMacro.Enabled = false;
            this.Text = "nplus - [RECORDING MACRO...]";
        }

        private void StopRecording()
        {
            _isRecording = false;
            startRecordItem.Enabled = true;
            stopRecordItem.Enabled = false;
            btnStartRecord.Enabled = true;
            btnStopRecord.Enabled = false;
            btnPlayMacro.Enabled = _currentMacro.Count > 0;
            this.Text = "nplus - C# Enterprise Text Editor";
        }

        private void PlaybackMacro(bool wrapInUndo)
        {
            var editor = GetActiveEditor();
            if (editor == null || _currentMacro.Count == 0) return;

            if (wrapInUndo) editor.BeginUndoAction();
            foreach (var step in _currentMacro) step.Execute(editor);
            if (wrapInUndo) editor.EndUndoAction();
        }

        private void RunMacroMultiple()
        {
            if (_currentMacro.Count == 0) return;

            Form dialog = new Form { Width = 320, Height = 180, Text = "Run Macro Multiple Times", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, Icon = this.Icon };
            RadioButton rbEnd = new RadioButton { Text = "Run to end of file", Location = new Point(20, 20), AutoSize = true, Checked = true };
            RadioButton rbCount = new RadioButton { Text = "Run this many times:", Location = new Point(20, 50), AutoSize = true };
            NumericUpDown numCount = new NumericUpDown { Location = new Point(160, 50), Width = 80, Minimum = 1, Maximum = 10000 };
            Button btnRun = new Button { Text = "Run", Location = new Point(110, 90), DialogResult = DialogResult.OK };

            dialog.Controls.AddRange(new Control[] { rbEnd, rbCount, numCount, btnRun });
            dialog.AcceptButton = btnRun;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var editor = GetActiveEditor(); if (editor == null) return;
                editor.BeginUndoAction();

                if (rbEnd.Checked)
                {
                    int lastPos = -1;
                    while (editor.CurrentPosition < editor.TextLength && editor.CurrentPosition != lastPos)
                    {
                        lastPos = editor.CurrentPosition;
                        PlaybackMacro(false);
                    }
                }
                else
                {
                    for (int i = 0; i < numCount.Value; i++) PlaybackMacro(false);
                }
                editor.EndUndoAction();
            }
        }

        private void TrimTrailingSpaceAndSave()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            editor.BeginUndoAction();

            for (int i = 0; i < editor.Lines.Count; i++)
            {
                var line = editor.Lines[i];
                string text = line.Text;
                if (string.IsNullOrEmpty(text)) continue;

                string trimmed = text.TrimEnd(' ', '\t', '\r', '\n');

                string lineEnding = "";
                if (text.EndsWith("\r\n")) lineEnding = "\r\n";
                else if (text.EndsWith("\n")) lineEnding = "\n";
                else if (text.EndsWith("\r")) lineEnding = "\r";

                string newText = trimmed + lineEnding;

                if (text != newText)
                {
                    editor.TargetStart = line.Position;
                    editor.TargetEnd = line.EndPosition;
                    editor.ReplaceTarget(newText);
                }
            }
            editor.EndUndoAction();
            SaveFile();
        }

        private void SaveMacro()
        {
            if (_currentMacro.Count == 0) { MessageBox.Show("No active recording.", "nplus"); return; }

            Form prompt = new Form { Width = 300, Height = 150, Text = "Save Macro", StartPosition = FormStartPosition.CenterParent, Icon = this.Icon };
            Label lbl = new Label { Left = 20, Top = 20, Text = "Macro Name:" };
            TextBox txt = new TextBox { Left = 20, Top = 45, Width = 240 };
            Button btnOk = new Button { Text = "Save", Left = 160, Top = 80, DialogResult = DialogResult.OK };

            prompt.Controls.AddRange(new Control[] { lbl, txt, btnOk });
            prompt.AcceptButton = btnOk;

            if (prompt.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(txt.Text))
            {
                _savedMacros[txt.Text] = new List<MacroStep>(_currentMacro);
                SaveMacros();
                MessageBox.Show($"Macro '{txt.Text}' saved.");
            }
        }

        private void ModifyMacro()
        {
            if (_savedMacros.Count == 0) { MessageBox.Show("No saved macros.", "nplus"); return; }

            Form manager = new Form { Width = 350, Height = 300, Text = "Modify/Delete Macros", StartPosition = FormStartPosition.CenterParent, Icon = this.Icon };
            ListBox lst = new ListBox { Left = 20, Top = 20, Width = 180, Height = 200 };
            foreach (var key in _savedMacros.Keys) lst.Items.Add(key);

            Button btnLoad = new Button { Text = "Load as Active", Left = 210, Top = 20, Width = 110 };
            Button btnDelete = new Button { Text = "Delete", Left = 210, Top = 50, Width = 110 };

            btnLoad.Click += (s, e) =>
            {
                if (lst.SelectedItem != null)
                {
                    _currentMacro = new List<MacroStep>(_savedMacros[lst.SelectedItem.ToString()]);
                    btnPlayMacro.Enabled = _currentMacro.Count > 0;
                    MessageBox.Show("Loaded into playback memory.");
                    manager.Close();
                }
            };

            btnDelete.Click += (s, e) =>
            {
                if (lst.SelectedItem != null)
                {
                    _savedMacros.Remove(lst.SelectedItem.ToString());
                    lst.Items.Remove(lst.SelectedItem);
                    SaveMacros();
                }
            };
            manager.Controls.AddRange(new Control[] { lst, btnLoad, btnDelete });
            manager.ShowDialog();
        }

        private void LoadMacroDialog()
        {
            if (_savedMacros.Count == 0) { MessageBox.Show("No saved macros.", "nplus"); return; }

            Form dialog = new Form { Width = 340, Height = 260, Text = "Load Saved Macro", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, Icon = this.Icon };
            ListBox lst = new ListBox { Left = 20, Top = 20, Width = 280, Height = 150 };
            foreach (var key in _savedMacros.Keys) lst.Items.Add(key);
            if (lst.Items.Count > 0) lst.SelectedIndex = 0;

            Button btnOk = new Button { Text = "Load", Left = 110, Top = 180, Width = 90, DialogResult = DialogResult.OK };
            Button btnCancel = new Button { Text = "Cancel", Left = 210, Top = 180, Width = 90, DialogResult = DialogResult.Cancel };

            dialog.Controls.AddRange(new Control[] { lst, btnOk, btnCancel });
            dialog.AcceptButton = btnOk;
            dialog.CancelButton = btnCancel;

            if (dialog.ShowDialog() == DialogResult.OK && lst.SelectedItem != null)
            {
                string name = lst.SelectedItem.ToString();
                _currentMacro = new List<MacroStep>(_savedMacros[name]);
                btnPlayMacro.Enabled = _currentMacro.Count > 0;
                MessageBox.Show($"Macro '{name}' loaded ({_currentMacro.Count} steps).", "nplus");
            }
        }

        private void EditMacroDialog()
        {
            // Let user pick which macro to edit: active or a saved one
            var sources = new List<string> { "(Active Macro)" };
            sources.AddRange(_savedMacros.Keys);

            Form picker = new Form { Width = 320, Height = 220, Text = "Edit Macro", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, Icon = this.Icon };
            Label lblPick = new Label { Text = "Select a macro to edit:", Left = 20, Top = 15, AutoSize = true };
            ListBox lstPick = new ListBox { Left = 20, Top = 40, Width = 260, Height = 100 };
            foreach (var s in sources) lstPick.Items.Add(s);
            lstPick.SelectedIndex = 0;

            Button btnEdit = new Button { Text = "Edit", Left = 100, Top = 150, Width = 90, DialogResult = DialogResult.OK };
            Button btnCancel = new Button { Text = "Cancel", Left = 200, Top = 150, Width = 80, DialogResult = DialogResult.Cancel };
            picker.Controls.AddRange(new Control[] { lblPick, lstPick, btnEdit, btnCancel });
            picker.AcceptButton = btnEdit;
            picker.CancelButton = btnCancel;

            if (picker.ShowDialog() != DialogResult.OK || lstPick.SelectedItem == null) return;

            string selectedName = lstPick.SelectedItem.ToString();
            bool isActive = selectedName == "(Active Macro)";

            List<MacroStep> steps;
            if (isActive)
            {
                if (_currentMacro.Count == 0) { MessageBox.Show("No active macro to edit.", "nplus"); return; }
                steps = _currentMacro;
            }
            else
            {
                steps = _savedMacros[selectedName];
            }

            ShowMacroEditor(steps, selectedName, isActive);
        }

        private void ShowMacroEditor(List<MacroStep> steps, string macroName, bool isActive)
        {
            Form editor = new Form { Width = 560, Height = 480, Text = $"Macro Editor - {macroName}", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.Sizable, Icon = this.Icon };

            ListBox lstSteps = new ListBox { Left = 20, Top = 20, Width = 360, Height = 360, Font = new Font("Consolas", 9.5f) };
            RefreshStepList(lstSteps, steps);

            Button btnMoveUp = new Button { Text = "▲ Up", Left = 400, Top = 20, Width = 120 };
            Button btnMoveDown = new Button { Text = "▼ Down", Left = 400, Top = 55, Width = 120 };
            Button btnEditStep = new Button { Text = "Edit Step", Left = 400, Top = 100, Width = 120 };
            Button btnDeleteStep = new Button { Text = "Delete Step", Left = 400, Top = 135, Width = 120 };
            Button btnAddInsert = new Button { Text = "Add Insert Text", Left = 400, Top = 180, Width = 120 };
            Button btnAddNewLine = new Button { Text = "Add New Line", Left = 400, Top = 215, Width = 120 };
            Button btnDuplicate = new Button { Text = "Duplicate Step", Left = 400, Top = 260, Width = 120 };
            Button btnOk = new Button { Text = "Apply && Close", Left = 400, Top = 340, Width = 120, DialogResult = DialogResult.OK };

            btnMoveUp.Click += (s, e) =>
            {
                int i = lstSteps.SelectedIndex;
                if (i > 0)
                {
                    var temp = steps[i]; steps[i] = steps[i - 1]; steps[i - 1] = temp;
                    RefreshStepList(lstSteps, steps);
                    lstSteps.SelectedIndex = i - 1;
                }
            };

            btnMoveDown.Click += (s, e) =>
            {
                int i = lstSteps.SelectedIndex;
                if (i >= 0 && i < steps.Count - 1)
                {
                    var temp = steps[i]; steps[i] = steps[i + 1]; steps[i + 1] = temp;
                    RefreshStepList(lstSteps, steps);
                    lstSteps.SelectedIndex = i + 1;
                }
            };

            btnEditStep.Click += (s, e) =>
            {
                int i = lstSteps.SelectedIndex;
                if (i < 0) return;
                var step = steps[i];

                if (step.ActionType == MacroActionType.InsertText)
                {
                    Form editPrompt = new Form { Width = 400, Height = 150, Text = "Edit Text", StartPosition = FormStartPosition.CenterParent, Icon = this.Icon };
                    Label lbl = new Label { Text = "Text to insert:", Left = 15, Top = 18, AutoSize = true };
                    TextBox txt = new TextBox { Left = 15, Top = 42, Width = 345, Text = step.Data ?? "" };
                    Button ok = new Button { Text = "OK", Left = 260, Top = 75, DialogResult = DialogResult.OK };
                    editPrompt.Controls.AddRange(new Control[] { lbl, txt, ok });
                    editPrompt.AcceptButton = ok;
                    if (editPrompt.ShowDialog() == DialogResult.OK) { step.Data = txt.Text; RefreshStepList(lstSteps, steps); lstSteps.SelectedIndex = i; }
                }
                else if (step.ActionType == MacroActionType.FindReplace || step.ActionType == MacroActionType.ReplaceAll)
                {
                    Form editPrompt = new Form { Width = 400, Height = 200, Text = "Edit Find/Replace", StartPosition = FormStartPosition.CenterParent, Icon = this.Icon };
                    Label lbl1 = new Label { Text = "Find:", Left = 15, Top = 18, AutoSize = true };
                    TextBox txtFind = new TextBox { Left = 80, Top = 15, Width = 280, Text = step.SearchText ?? "" };
                    Label lbl2 = new Label { Text = "Replace:", Left = 15, Top = 50, AutoSize = true };
                    TextBox txtRepl = new TextBox { Left = 80, Top = 47, Width = 280, Text = step.ReplaceText ?? "" };
                    Button ok = new Button { Text = "OK", Left = 260, Top = 120, DialogResult = DialogResult.OK };
                    editPrompt.Controls.AddRange(new Control[] { lbl1, txtFind, lbl2, txtRepl, ok });
                    editPrompt.AcceptButton = ok;
                    if (editPrompt.ShowDialog() == DialogResult.OK) { step.SearchText = txtFind.Text; step.ReplaceText = txtRepl.Text; RefreshStepList(lstSteps, steps); lstSteps.SelectedIndex = i; }
                }
                else
                {
                    MessageBox.Show("This step type cannot be edited directly.", "nplus");
                }
            };

            btnDeleteStep.Click += (s, e) =>
            {
                int i = lstSteps.SelectedIndex;
                if (i >= 0) { steps.RemoveAt(i); RefreshStepList(lstSteps, steps); if (steps.Count > 0) lstSteps.SelectedIndex = Math.Min(i, steps.Count - 1); }
            };

            btnAddInsert.Click += (s, e) =>
            {
                Form addPrompt = new Form { Width = 400, Height = 150, Text = "Add Insert Text Step", StartPosition = FormStartPosition.CenterParent, Icon = this.Icon };
                Label lbl = new Label { Text = "Text to insert:", Left = 15, Top = 18, AutoSize = true };
                TextBox txt = new TextBox { Left = 15, Top = 42, Width = 345 };
                Button ok = new Button { Text = "Add", Left = 260, Top = 75, DialogResult = DialogResult.OK };
                addPrompt.Controls.AddRange(new Control[] { lbl, txt, ok });
                addPrompt.AcceptButton = ok;
                if (addPrompt.ShowDialog() == DialogResult.OK && txt.Text.Length > 0)
                {
                    int insertAt = lstSteps.SelectedIndex >= 0 ? lstSteps.SelectedIndex + 1 : steps.Count;
                    steps.Insert(insertAt, new MacroStep(MacroActionType.InsertText, txt.Text));
                    RefreshStepList(lstSteps, steps);
                    lstSteps.SelectedIndex = insertAt;
                }
            };

            btnAddNewLine.Click += (s, e) =>
            {
                int insertAt = lstSteps.SelectedIndex >= 0 ? lstSteps.SelectedIndex + 1 : steps.Count;
                steps.Insert(insertAt, new MacroStep(MacroActionType.NewLine));
                RefreshStepList(lstSteps, steps);
                lstSteps.SelectedIndex = insertAt;
            };

            btnDuplicate.Click += (s, e) =>
            {
                int i = lstSteps.SelectedIndex;
                if (i < 0) return;
                var orig = steps[i];
                var copy = new MacroStep { ActionType = orig.ActionType, Data = orig.Data, CommandId = orig.CommandId, SearchText = orig.SearchText, ReplaceText = orig.ReplaceText, Flags = orig.Flags, IsRegex = orig.IsRegex, IsBackward = orig.IsBackward, IsWrap = orig.IsWrap, IsReplace = orig.IsReplace, IsPurge = orig.IsPurge, IsBookmark = orig.IsBookmark };
                steps.Insert(i + 1, copy);
                RefreshStepList(lstSteps, steps);
                lstSteps.SelectedIndex = i + 1;
            };

            editor.Controls.AddRange(new Control[] { lstSteps, btnMoveUp, btnMoveDown, btnEditStep, btnDeleteStep, btnAddInsert, btnAddNewLine, btnDuplicate, btnOk });
            editor.AcceptButton = btnOk;

            if (editor.ShowDialog() == DialogResult.OK)
            {
                if (isActive)
                {
                    _currentMacro = steps;
                    btnPlayMacro.Enabled = _currentMacro.Count > 0;
                }
                else
                {
                    SaveMacros();
                }
            }
        }

        private void RefreshStepList(ListBox lst, List<MacroStep> steps)
        {
            lst.Items.Clear();
            for (int i = 0; i < steps.Count; i++)
            {
                lst.Items.Add($"{i + 1,3}. {FormatStep(steps[i])}");
            }
        }

        private string FormatStep(MacroStep step)
        {
            switch (step.ActionType)
            {
                case MacroActionType.InsertText:
                    string display = (step.Data ?? "").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
                    if (display.Length > 30) display = display.Substring(0, 30) + "...";
                    return $"Insert \"{display}\"";
                case MacroActionType.NewLine: return "New Line (Enter)";
                case MacroActionType.Backspace: return "Backspace";
                case MacroActionType.Delete: return "Delete";
                case MacroActionType.KeyCommand: return $"Key Command ({(Command)step.CommandId})";
                case MacroActionType.FindReplace:
                    return step.IsReplace ? $"Replace \"{step.SearchText}\" → \"{step.ReplaceText}\"" : $"Find \"{step.SearchText}\"";
                case MacroActionType.ReplaceAll: return $"Replace All \"{step.SearchText}\" → \"{step.ReplaceText}\"";
                case MacroActionType.MarkAll: return $"Mark All \"{step.SearchText}\"";
                case MacroActionType.ClearMarks: return "Clear All Marks";
                default: return step.ActionType.ToString();
            }
        }
        #endregion

        #region Line Operations

        private List<string> GetSelectedOrAllLines(Scintilla editor, out int startLine, out int endLine)
        {
            startLine = editor.LineFromPosition(editor.SelectionStart);
            endLine = editor.LineFromPosition(editor.SelectionEnd);
            if (editor.SelectionStart == editor.SelectionEnd)
            {
                startLine = 0;
                endLine = editor.Lines.Count - 1;
            }
            var lines = new List<string>();
            for (int i = startLine; i <= endLine; i++) lines.Add(editor.Lines[i].Text);
            return lines;
        }

        private void ReplaceLines(Scintilla editor, int startLine, int endLine, List<string> lines)
        {
            editor.BeginUndoAction();
            editor.SelectionStart = editor.Lines[startLine].Position;
            editor.SelectionEnd = editor.Lines[endLine].EndPosition;
            editor.ReplaceSelection(string.Join("", lines));
            editor.EndUndoAction();
        }

        private void DuplicateCurrentLine()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int cur = editor.LineFromPosition(editor.CurrentPosition);
            string text = editor.Lines[cur].Text;
            if (!text.EndsWith("\n")) text += Environment.NewLine;
            editor.BeginUndoAction();
            editor.InsertText(editor.Lines[cur].EndPosition, text);
            editor.EndUndoAction();
        }

        private void RemoveDuplicateLines()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            var lines = editor.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Distinct();
            editor.Text = string.Join(Environment.NewLine, lines);
        }

        private void RemoveConsecutiveDuplicateLines()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            var lines = editor.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var result = new List<string> { lines[0] };
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i] != lines[i - 1]) result.Add(lines[i]);
            }
            editor.Text = string.Join(Environment.NewLine, result);
        }

        private void SplitLines()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            if (editor.SelectionStart == editor.SelectionEnd) return;
            string sel = editor.SelectedText;
            editor.BeginUndoAction();
            editor.ReplaceSelection(string.Join(Environment.NewLine, sel.ToCharArray().Select(c => c.ToString())));
            editor.EndUndoAction();
        }

        private void MoveLine(int dir)
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int cur = editor.LineFromPosition(editor.CurrentPosition);
            int target = cur + dir;
            if (target < 0 || target >= editor.Lines.Count) return;
            string text = editor.Lines[cur].Text;
            editor.BeginUndoAction();
            editor.DeleteRange(editor.Lines[cur].Position, editor.Lines[cur].Length);
            editor.InsertText(editor.Lines[target].Position, text);
            editor.GotoPosition(editor.Lines[target].Position);
            editor.EndUndoAction();
        }

        private void JoinLines()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            editor.BeginUndoAction();
            string sel = editor.SelectedText;
            if (string.IsNullOrEmpty(sel))
            {
                int cur = editor.LineFromPosition(editor.CurrentPosition);
                if (cur + 1 < editor.Lines.Count)
                {
                    editor.SelectionStart = editor.Lines[cur].Position;
                    editor.SelectionEnd = editor.Lines[cur + 1].EndPosition;
                    sel = editor.SelectedText;
                }
            }
            if (!string.IsNullOrEmpty(sel))
            {
                editor.ReplaceSelection(sel.Replace("\r", "").Replace("\n", " ").Replace("  ", " "));
            }
            editor.EndUndoAction();
        }

        private void RemoveEmptyLines(bool white)
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            var lines = editor.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var filtered = white ? lines.Where(l => !string.IsNullOrWhiteSpace(l)) : lines.Where(l => l.Length > 0);
            editor.Text = string.Join(Environment.NewLine, filtered);
        }

        private void InsertBlankLine(bool below)
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int cur = editor.LineFromPosition(editor.CurrentPosition);
            editor.BeginUndoAction();
            if (below)
            {
                editor.InsertText(editor.Lines[cur].EndPosition, Environment.NewLine);
                editor.GotoPosition(editor.Lines[cur + 1].Position);
            }
            else
            {
                editor.InsertText(editor.Lines[cur].Position, Environment.NewLine);
                editor.GotoPosition(editor.Lines[cur].Position);
            }
            editor.EndUndoAction();
        }

        private void ReverseLineOrder()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int startLine, endLine;
            var lines = GetSelectedOrAllLines(editor, out startLine, out endLine);
            lines.Reverse();
            ReplaceLines(editor, startLine, endLine, lines);
        }

        private void RandomizeLineOrder()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int startLine, endLine;
            var lines = GetSelectedOrAllLines(editor, out startLine, out endLine);
            var rng = new Random();
            for (int i = lines.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var temp = lines[i]; lines[i] = lines[j]; lines[j] = temp;
            }
            ReplaceLines(editor, startLine, endLine, lines);
        }

        private void SortLinesAdvanced(SortMode mode, bool descending, bool ignoreCase)
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int startLine, endLine;
            var lines = GetSelectedOrAllLines(editor, out startLine, out endLine);

            IOrderedEnumerable<string> sorted;
            switch (mode)
            {
                case SortMode.Lexicographic:
                    var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                    sorted = descending ? lines.OrderByDescending(l => l, comparer) : lines.OrderBy(l => l, comparer);
                    break;
                case SortMode.Locale:
                    var localeComp = ignoreCase ? StringComparer.CurrentCultureIgnoreCase : StringComparer.CurrentCulture;
                    sorted = descending ? lines.OrderByDescending(l => l, localeComp) : lines.OrderBy(l => l, localeComp);
                    break;
                case SortMode.Integer:
                    sorted = descending
                        ? lines.OrderByDescending(l => { long v; return long.TryParse(l.Trim(), out v) ? v : long.MaxValue; })
                        : lines.OrderBy(l => { long v; return long.TryParse(l.Trim(), out v) ? v : long.MaxValue; });
                    break;
                case SortMode.DecimalDot:
                    sorted = descending
                        ? lines.OrderByDescending(l => { double v; return double.TryParse(l.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : double.MaxValue; })
                        : lines.OrderBy(l => { double v; return double.TryParse(l.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : double.MaxValue; });
                    break;
                case SortMode.DecimalComma:
                    var commaFmt = new System.Globalization.NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };
                    sorted = descending
                        ? lines.OrderByDescending(l => { double v; return double.TryParse(l.Trim(), System.Globalization.NumberStyles.Any, commaFmt, out v) ? v : double.MaxValue; })
                        : lines.OrderBy(l => { double v; return double.TryParse(l.Trim(), System.Globalization.NumberStyles.Any, commaFmt, out v) ? v : double.MaxValue; });
                    break;
                case SortMode.Length:
                    sorted = descending
                        ? lines.OrderByDescending(l => l.TrimEnd('\r', '\n').Length)
                        : lines.OrderBy(l => l.TrimEnd('\r', '\n').Length);
                    break;
                default:
                    sorted = lines.OrderBy(l => l);
                    break;
            }
            ReplaceLines(editor, startLine, endLine, sorted.ToList());
        }

        private void DeleteCurrentLine()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int cur = editor.LineFromPosition(editor.CurrentPosition);
            editor.DeleteRange(editor.Lines[cur].Position, editor.Lines[cur].Length);
        }
        #endregion

        #region Blank Operations
        private void BlankOp_TrimTrailing()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            editor.BeginUndoAction();
            for (int i = 0; i < editor.Lines.Count; i++)
            {
                string lineText = editor.Lines[i].Text;
                string eol = lineText.EndsWith("\r\n") ? "\r\n" : lineText.EndsWith("\n") ? "\n" : "";
                string content = lineText.TrimEnd('\r', '\n');
                string trimmed = content.TrimEnd() + eol;
                if (trimmed != lineText)
                {
                    editor.TargetStart = editor.Lines[i].Position;
                    editor.TargetEnd = editor.Lines[i].EndPosition;
                    editor.ReplaceTarget(trimmed);
                }
            }
            editor.EndUndoAction();
        }

        private void BlankOp_TrimLeading()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            editor.BeginUndoAction();
            for (int i = 0; i < editor.Lines.Count; i++)
            {
                string lineText = editor.Lines[i].Text;
                string eol = lineText.EndsWith("\r\n") ? "\r\n" : lineText.EndsWith("\n") ? "\n" : "";
                string content = lineText.TrimEnd('\r', '\n');
                string trimmed = content.TrimStart() + eol;
                if (trimmed != lineText)
                {
                    editor.TargetStart = editor.Lines[i].Position;
                    editor.TargetEnd = editor.Lines[i].EndPosition;
                    editor.ReplaceTarget(trimmed);
                }
            }
            editor.EndUndoAction();
        }

        private void BlankOp_TrimBoth()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            editor.BeginUndoAction();
            for (int i = 0; i < editor.Lines.Count; i++)
            {
                string lineText = editor.Lines[i].Text;
                string eol = lineText.EndsWith("\r\n") ? "\r\n" : lineText.EndsWith("\n") ? "\n" : "";
                string content = lineText.TrimEnd('\r', '\n');
                string trimmed = content.Trim() + eol;
                if (trimmed != lineText)
                {
                    editor.TargetStart = editor.Lines[i].Position;
                    editor.TargetEnd = editor.Lines[i].EndPosition;
                    editor.ReplaceTarget(trimmed);
                }
            }
            editor.EndUndoAction();
        }

        private void BlankOp_EolToSpace()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            editor.BeginUndoAction();
            string text = editor.Text;
            text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            editor.Text = text;
            editor.EndUndoAction();
        }

        private void BlankOp_TrimBothAndEolToSpace()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            editor.BeginUndoAction();
            string[] lines = editor.Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].Trim();
            editor.Text = string.Join(" ", lines);
            editor.EndUndoAction();
        }

        private void BlankOp_TabToSpace()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int tabWidth = editor.TabWidth > 0 ? editor.TabWidth : 4;
            editor.BeginUndoAction();
            editor.Text = editor.Text.Replace("\t", new string(' ', tabWidth));
            editor.EndUndoAction();
        }

        private void BlankOp_SpaceToTabAll()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int tabWidth = editor.TabWidth > 0 ? editor.TabWidth : 4;
            string spaces = new string(' ', tabWidth);
            editor.BeginUndoAction();
            editor.Text = editor.Text.Replace(spaces, "\t");
            editor.EndUndoAction();
        }

        private void BlankOp_SpaceToTabLeading()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int tabWidth = editor.TabWidth > 0 ? editor.TabWidth : 4;
            string spaces = new string(' ', tabWidth);
            editor.BeginUndoAction();
            for (int i = 0; i < editor.Lines.Count; i++)
            {
                string lineText = editor.Lines[i].Text;
                string eol = lineText.EndsWith("\r\n") ? "\r\n" : lineText.EndsWith("\n") ? "\n" : "";
                string content = lineText.TrimEnd('\r', '\n');

                int leadingEnd = 0;
                while (leadingEnd < content.Length && (content[leadingEnd] == ' ' || content[leadingEnd] == '\t')) leadingEnd++;

                string leading = content.Substring(0, leadingEnd).Replace(spaces, "\t");
                string newLine = leading + content.Substring(leadingEnd) + eol;
                if (newLine != lineText)
                {
                    editor.TargetStart = editor.Lines[i].Position;
                    editor.TargetEnd = editor.Lines[i].EndPosition;
                    editor.ReplaceTarget(newLine);
                }
            }
            editor.EndUndoAction();
        }
        #endregion

        #region Bookmark Operations
        private void ToggleBookmark()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int lineIdx = editor.LineFromPosition(editor.CurrentPosition);
            if ((editor.Lines[lineIdx].MarkerGet() & (1u << BOOKMARK_MARKER)) != 0)
                editor.Lines[lineIdx].MarkerDelete(BOOKMARK_MARKER);
            else
                editor.Lines[lineIdx].MarkerAdd(BOOKMARK_MARKER);
        }

        private void NavigateBookmark(bool next)
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            int curLine = editor.LineFromPosition(editor.CurrentPosition);
            uint mask = 1u << BOOKMARK_MARKER;
            int targetLine = -1;

            if (next)
            {
                if (curLine + 1 < editor.Lines.Count) targetLine = editor.Lines[curLine + 1].MarkerNext(mask);
                if (targetLine == -1 && editor.Lines.Count > 0) targetLine = editor.Lines[0].MarkerNext(mask);
            }
            else
            {
                if (curLine - 1 >= 0) targetLine = editor.Lines[curLine - 1].MarkerPrevious(mask);
                if (targetLine == -1 && editor.Lines.Count > 0) targetLine = editor.Lines[editor.Lines.Count - 1].MarkerPrevious(mask);
            }

            if (targetLine != -1) editor.GotoPosition(editor.Lines[targetLine].Position);
        }

        private void ClearAllBookmarks() => GetActiveEditor()?.MarkerDeleteAll(BOOKMARK_MARKER);

        private void ProcessBookmarks(bool del, bool quiet)
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            var text = new List<string>();
            editor.BeginUndoAction();
            for (int i = editor.Lines.Count - 1; i >= 0; i--)
            {
                if ((editor.Lines[i].MarkerGet() & (1u << BOOKMARK_MARKER)) != 0)
                {
                    text.Insert(0, editor.Lines[i].Text);
                    if (del) editor.DeleteRange(editor.Lines[i].Position, editor.Lines[i].Length);
                }
            }
            if (!quiet && text.Count > 0) Clipboard.SetText(string.Join("", text));
            editor.EndUndoAction();
        }

        private void InverseBookmarkDelete()
        {
            var editor = GetActiveEditor(); if (editor == null) return;
            editor.BeginUndoAction();
            for (int i = editor.Lines.Count - 1; i >= 0; i--)
                if ((editor.Lines[i].MarkerGet() & (1u << BOOKMARK_MARKER)) == 0)
                    editor.DeleteRange(editor.Lines[i].Position, editor.Lines[i].Length);
            editor.EndUndoAction();
        }

        private void PasteToBookmarks()
        {
            var editor = GetActiveEditor(); if (editor == null || !Clipboard.ContainsText()) return;
            string clip = Clipboard.GetText();
            editor.BeginUndoAction();
            for (int i = editor.Lines.Count - 1; i >= 0; i--)
            {
                if ((editor.Lines[i].MarkerGet() & (1u << BOOKMARK_MARKER)) != 0)
                {
                    editor.SelectionStart = editor.Lines[i].Position;
                    editor.SelectionEnd = editor.Lines[i].EndPosition;
                    editor.ReplaceSelection(clip + Environment.NewLine);
                }
            }
            editor.EndUndoAction();
        }
        #endregion

        #region Encoding Operations

        private Encoding DetectFileEncoding(string filePath)
        {
            byte[] bom = new byte[4];
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Read(bom, 0, 4);
            }

            // Check BOM signatures
            if (bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;       // UTF-16 BE BOM
            if (bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;                 // UTF-16 LE BOM
            if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return Encoding.UTF8;  // UTF-8 BOM

            // No BOM — check if content is valid UTF-8
            try
            {
                byte[] content;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    content = ms.ToArray();
                }
                var utf8NoBom = new UTF8Encoding(false, true);
                utf8NoBom.GetString(content); // Throws on invalid UTF-8
                return utf8NoBom; // UTF-8 without BOM
            }
            catch
            {
                return Encoding.Default; // ANSI fallback
            }
        }

        private string GetEncodingName(Encoding enc)
        {
            if (enc == null) return "UTF-8";

            // Check for UTF-8 with BOM (preamble present)
            if (enc is UTF8Encoding && enc.GetPreamble().Length > 0) return "UTF-8-BOM";
            if (enc is UTF8Encoding) return "UTF-8";
            if (enc.CodePage == 1200) return "UTF-16 LE BOM";    // Encoding.Unicode
            if (enc.CodePage == 1201) return "UTF-16 BE BOM";    // Encoding.BigEndianUnicode
            if (enc.CodePage == Encoding.Default.CodePage) return "ANSI";

            return enc.EncodingName;
        }

        private Encoding GetEncodingFromName(string name)
        {
            switch (name)
            {
                case "ANSI": return Encoding.Default;
                case "UTF-8": return new UTF8Encoding(false);       // No BOM
                case "UTF-8-BOM": return new UTF8Encoding(true);    // With BOM
                case "UTF-16 BE BOM": return Encoding.BigEndianUnicode;
                case "UTF-16 LE BOM": return Encoding.Unicode;
                default: return new UTF8Encoding(false);
            }
        }

        private Encoding GetTabEncoding(TabPage page)
        {
            if (page != null && _tabEncodings.TryGetValue(page, out var enc)) return enc;
            return new UTF8Encoding(false); // Default: UTF-8 without BOM
        }

        private void SetEncoding(string encodingName)
        {
            var page = tcDocuments.SelectedTab;
            if (page == null) return;

            var newEnc = GetEncodingFromName(encodingName);
            _tabEncodings[page] = newEnc;
            UpdateEncodingStatusLabel(newEnc);

            // Mark as modified since re-saving will change the file's encoding
            if (!page.Text.EndsWith("*")) page.Text += "*";
            UpdateToolbarState();
            tcDocuments.Invalidate();
        }

        private void ConvertEncoding(string targetName)
        {
            var page = tcDocuments.SelectedTab;
            if (page == null) return;
            var editor = page.Controls[0] as Scintilla;
            if (editor == null) return;

            var currentEnc = GetTabEncoding(page);
            var targetEnc = GetEncodingFromName(targetName);

            // Re-encode the text: decode from current, re-encode to target
            byte[] currentBytes = currentEnc.GetBytes(editor.Text);
            string converted = targetEnc.GetString(currentBytes);

            editor.BeginUndoAction();
            editor.Text = converted;
            editor.EndUndoAction();

            _tabEncodings[page] = targetEnc;
            UpdateEncodingStatusLabel(targetEnc);

            if (!page.Text.EndsWith("*")) page.Text += "*";
            UpdateToolbarState();
            tcDocuments.Invalidate();
        }

        private void UpdateEncodingStatusLabel(Encoding enc)
        {
            lblEncoding.Text = GetEncodingName(enc);
        }

        private void UpdateEncodingMenuChecks()
        {
            var page = tcDocuments.SelectedTab;
            string currentName = page != null ? GetEncodingName(GetTabEncoding(page)) : "UTF-8";

            // The first 5 items are the encoding selectors (before the separator)
            for (int i = 0; i < 5 && i < _encodingMenu.DropDownItems.Count; i++)
            {
                if (_encodingMenu.DropDownItems[i] is ToolStripMenuItem item)
                {
                    item.Checked = (item.Text == currentName);
                }
            }
        }

        #endregion

        #region Boilerplate UI & Tabs
        private void AddNewTab(string title, string path = null)
        {
            TabPage page = new TabPage(title);
            Scintilla editor = new Scintilla { Dock = DockStyle.Fill, Tag = path };

            // NEW: Fire the Status Bar updater whenever Scintilla repaints or the cursor moves
            editor.UpdateUI += (s, e) =>
            {
                if (tcDocuments.SelectedTab?.Controls[0] == s)
                {
                    UpdateStatusBar(s as Scintilla);
                }
            };

            editor.CharAdded += (s, e) =>
            {
                if (!_isRecording) return;
                char c = (char)e.Char;
                // Skip newline chars — Enter is already recorded as NewLine in KeyDown
                if (c == '\r' || c == '\n') return;
                _currentMacro.Add(new MacroStep(MacroActionType.InsertText, c.ToString()));
            };

            editor.KeyDown += (s, e) =>
            {
                if (e.Control && e.Alt && !e.Shift && e.KeyCode == Keys.A)
                {
                    btnColSelect.Checked = !btnColSelect.Checked;
                    e.SuppressKeyPress = true;
                }

                if (!_isRecording) return;
                if (e.KeyCode == Keys.Enter)
                {
                    _currentMacro.Add(new MacroStep(MacroActionType.NewLine));
                }
                else if (e.KeyCode == Keys.Back)
                {
                    _currentMacro.Add(new MacroStep(MacroActionType.Backspace));
                }
                else if (e.KeyCode == Keys.Delete)
                {
                    _currentMacro.Add(new MacroStep(MacroActionType.Delete));
                }
                else if (e.KeyCode == Keys.Tab)
                {
                    _currentMacro.Add(new MacroStep { ActionType = MacroActionType.KeyCommand, CommandId = (int)(e.Shift ? Command.BackTab : Command.Tab) });
                }
                else
                {
                    // Map navigation keys to Scintilla commands
                    Command? cmd = null;
                    switch (e.KeyCode)
                    {
                        case Keys.Home:
                            if (e.Control && e.Shift) cmd = Command.DocumentStartExtend;
                            else if (e.Control) cmd = Command.DocumentStart;
                            else if (e.Shift) cmd = Command.HomeExtend;
                            else cmd = Command.Home;
                            break;
                        case Keys.End:
                            if (e.Control && e.Shift) cmd = Command.DocumentEndExtend;
                            else if (e.Control) cmd = Command.DocumentEnd;
                            else if (e.Shift) cmd = Command.LineEndExtend;
                            else cmd = Command.LineEnd;
                            break;
                        case Keys.Left:
                            if (e.Control && e.Shift) cmd = Command.WordLeftExtend;
                            else if (e.Control) cmd = Command.WordLeft;
                            else if (e.Shift) cmd = Command.CharLeftExtend;
                            else cmd = Command.CharLeft;
                            break;
                        case Keys.Right:
                            if (e.Control && e.Shift) cmd = Command.WordRightExtend;
                            else if (e.Control) cmd = Command.WordRight;
                            else if (e.Shift) cmd = Command.CharRightExtend;
                            else cmd = Command.CharRight;
                            break;
                        case Keys.Up:
                            if (e.Shift) cmd = Command.LineUpExtend;
                            else cmd = Command.LineUp;
                            break;
                        case Keys.Down:
                            if (e.Shift) cmd = Command.LineDownExtend;
                            else cmd = Command.LineDown;
                            break;
                        case Keys.PageUp:
                            if (e.Shift) cmd = Command.PageUpExtend;
                            else cmd = Command.PageUp;
                            break;
                        case Keys.PageDown:
                            if (e.Shift) cmd = Command.PageDownExtend;
                            else cmd = Command.PageDown;
                            break;
                    }
                    if (cmd.HasValue)
                        _currentMacro.Add(new MacroStep { ActionType = MacroActionType.KeyCommand, CommandId = (int)cmd.Value });
                }
            };

            // Apply global ribbon toggles
            editor.ViewWhitespace = _showCharacters ? WhitespaceMode.VisibleAlways : WhitespaceMode.Invisible;
            editor.ViewEol = _showCharacters;
            editor.IndentationGuides = _showIndentGuides ? IndentView.LookBoth : IndentView.None;
            editor.WrapMode = _wordWrap ? WrapMode.Word : WrapMode.None;

            editor.MultipleSelection = true;
            editor.AdditionalSelectionTyping = true;
            editor.DirectMessage(2422, new IntPtr(btnColSelect.Checked ? 1 : 0), IntPtr.Zero);

            ApplySyntaxHighlighting(editor, path);

            editor.TextChanged += (s, e) =>
            {
                if (!page.Text.EndsWith("*") && !editor.ReadOnly)
                {
                    page.Text += "*";
                    if (tcDocuments.SelectedTab == page) UpdateToolbarState();
                }
            };

            page.Controls.Add(editor);
            tcDocuments.TabPages.Add(page);
            tcDocuments.SelectedTab = page;

            // Default encoding for new tabs
            if (!_tabEncodings.ContainsKey(page))
                _tabEncodings[page] = new UTF8Encoding(false);
            UpdateEncodingStatusLabel(GetTabEncoding(page));

            UpdateToolbarState();

            // Force an initial update of the status bar when the new tab is created
            UpdateStatusBar(editor);
        }

        private void UpdateStatusBar(Scintilla editor)
        {
            if (editor == null)
            {
                lblLength.Text = "Length: 0";
                lblPosition.Text = "Ln: 1  Col: 1  Pos: 0";
                lblInsOvr.Text = "INS";
                return;
            }

            int pos = editor.CurrentPosition;
            int line = editor.LineFromPosition(pos);
            int col = editor.GetColumn(pos);

            // Using :N0 formats the integer with commas (e.g., 1,234,567)
            lblLength.Text = $"Length: {editor.TextLength:N0}";

            // Add 1 to line and col because Scintilla indexes from 0, but humans read from 1
            lblPosition.Text = $"Ln: {line + 1:N0}  Col: {col + 1:N0}  Pos: {pos:N0}";

            lblInsOvr.Text = editor.Overtype ? "OVR" : "INS";
            lblEncoding.Text = "UTF-8"; // ScintillaNET processes natively in UTF-8
        }

        private void TcDocuments_DrawItem(object sender, DrawItemEventArgs e)
        {
            var page = tcDocuments.TabPages[e.Index];
            var rect = tcDocuments.GetTabRect(e.Index);

            // 1. Check Editor State
            var editor = page.Controls.Count > 0 ? page.Controls[0] as Scintilla : null;
            bool isReadOnly = editor != null && editor.ReadOnly;
            bool isUnsaved = page.Text.EndsWith("*");

            // NEW: Check if this specific tab is currently being live-monitored
            bool isMonitored = _fileWatchers.ContainsKey(page);

            Brush fillBrush;
            bool isCustomBrush = true;

            // 2. Apply Custom Background Colors
            if (isMonitored)
            {
                // Cyan background for Live Monitoring
                fillBrush = new SolidBrush(_isDarkMode ? Color.FromArgb(30, 80, 90) : Color.LightCyan);
            }
            else if (isReadOnly)
            {
                // Reddish background for Read-Only
                fillBrush = new SolidBrush(_isDarkMode ? Color.FromArgb(80, 40, 40) : Color.MistyRose);
            }
            else
            {
                // Standard Colors
                if (_isDarkMode)
                {
                    fillBrush = new SolidBrush(Color.FromArgb(45, 45, 48));
                }
                else
                {
                    fillBrush = SystemBrushes.Control;
                    isCustomBrush = false;
                }
            }

            Color textColor = _isDarkMode ? Color.White : Color.Black;

            // Draw the tab background
            e.Graphics.FillRectangle(fillBrush, rect);

            // Draw save status icon (small circle on the left)
            int iconSize = 8;
            int iconX = rect.Left + 4;
            int iconY = rect.Top + (rect.Height - iconSize) / 2;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (isUnsaved)
            {
                using (var brush = new SolidBrush(_isDarkMode ? Color.FromArgb(230, 160, 50) : Color.FromArgb(210, 140, 30)))
                    e.Graphics.FillEllipse(brush, iconX, iconY, iconSize, iconSize);
            }
            else
            {
                using (var brush = new SolidBrush(_isDarkMode ? Color.FromArgb(60, 180, 100) : Color.FromArgb(50, 160, 80)))
                    e.Graphics.FillEllipse(brush, iconX, iconY, iconSize, iconSize);
            }

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;

            // Draw the tab text (shifted right to make room for the icon)
            var textRect = new Rectangle(rect.Left + iconSize + 8, rect.Top, rect.Width - iconSize - 8, rect.Height);
            TextRenderer.DrawText(e.Graphics, page.Text, page.Font, textRect, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // Draw the close 'x' button
            var closeRect = new Rectangle(rect.Right - CloseBtnSize - 4, rect.Top + 6, CloseBtnSize, CloseBtnSize);
            e.Graphics.DrawString("x", new Font("Arial", 8, FontStyle.Bold), Brushes.IndianRed, closeRect);

            // Prevent memory leaks by disposing of custom brushes
            if (isCustomBrush) fillBrush.Dispose();
        }

        private void TcDocuments_MouseDown(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < tcDocuments.TabPages.Count; i++)
            {
                var rect = tcDocuments.GetTabRect(i);
                var closeRect = new Rectangle(rect.Right - CloseBtnSize - 4, rect.Top + 6, CloseBtnSize, CloseBtnSize);

                if (closeRect.Contains(e.Location))
                {
                    CloseTab(i);
                    return;
                }
            }

            // Record drag start for tab reordering
            if (e.Button == MouseButtons.Left)
            {
                for (int i = 0; i < tcDocuments.TabPages.Count; i++)
                {
                    if (tcDocuments.GetTabRect(i).Contains(e.Location))
                    {
                        _dragStartIndex = i;
                        _dragStartPoint = e.Location;
                        _draggedTab = tcDocuments.TabPages[i];
                        break;
                    }
                }
            }
        }

        private void TcDocuments_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _draggedTab == null) return;

            // Only start a drag after a minimum movement threshold
            if (Math.Abs(e.X - _dragStartPoint.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - _dragStartPoint.Y) < SystemInformation.DragSize.Height)
                return;

            tcDocuments.DoDragDrop(_draggedTab, DragDropEffects.Move);
        }

        private void TcDocuments_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TabPage)))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Move;

            Point clientPoint = tcDocuments.PointToClient(new Point(e.X, e.Y));
            TabPage draggedPage = (TabPage)e.Data.GetData(typeof(TabPage));

            for (int i = 0; i < tcDocuments.TabPages.Count; i++)
            {
                if (tcDocuments.GetTabRect(i).Contains(clientPoint))
                {
                    int dragIndex = tcDocuments.TabPages.IndexOf(draggedPage);
                    if (dragIndex == i || dragIndex < 0) break;

                    // Rebuild the tab list in the new order
                    var tabs = new List<TabPage>();
                    foreach (TabPage tp in tcDocuments.TabPages) tabs.Add(tp);

                    tabs.RemoveAt(dragIndex);
                    tabs.Insert(i, draggedPage);

                    tcDocuments.SuspendLayout();
                    tcDocuments.TabPages.Clear();
                    tcDocuments.TabPages.AddRange(tabs.ToArray());
                    tcDocuments.SelectedTab = draggedPage;
                    tcDocuments.ResumeLayout();
                    break;
                }
            }
        }

        private void TcDocuments_DragDrop(object sender, DragEventArgs e)
        {
            _draggedTab = null;
            _dragStartIndex = -1;
        }

        private void CloseTab(int idx)
        {
            var page = tcDocuments.TabPages[idx];

            if (page.Text.EndsWith("*"))
            {
                var res = MessageBox.Show($"Save changes to {page.Text.TrimEnd('*')}?", "nplus", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (res == DialogResult.Yes) SaveSpecificFile(page.Controls[0] as Scintilla, page);
                else if (res == DialogResult.Cancel) return;
            }

            // Clean up the FileSystemWatcher if the user closes a tab that is actively monitoring
            if (_fileWatchers.TryGetValue(page, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= Fsw_Changed;
                watcher.Dispose();
                _fileWatchers.Remove(page);
            }

            // Clean up file change detection watcher
            StopFileChangeWatch(page);

            // Clean up encoding tracking
            _tabEncodings.Remove(page);

            tcDocuments.TabPages.RemoveAt(idx);
            if (tcDocuments.TabCount == 0) AddNewTab("new 1");
        }

        private void OpenFile()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Multiselect = true;
                ofd.Title = "Open File(s)";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    foreach (string filePath in ofd.FileNames)
                    {
                        AddNewTab("Loading...", filePath);
                        LoadFileIntoEditor(GetActiveEditor(), tcDocuments.SelectedTab, filePath);
                        AddToRecentFiles(filePath);
                    }
                }
            }
        }

        private void SaveFile()
        {
            var editor = GetActiveEditor();
            if (editor != null && tcDocuments.SelectedTab.Text.EndsWith("*"))
            {
                SaveSpecificFile(editor, tcDocuments.SelectedTab);
            }
        }

        private void SaveFileAs()
        {
            var editor = GetActiveEditor();
            var page = tcDocuments.SelectedTab;

            if (editor == null || page == null) return;

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                if (editor.Tag is string currentPath && !string.IsNullOrEmpty(currentPath))
                {
                    sfd.FileName = Path.GetFileName(currentPath);
                    sfd.InitialDirectory = Path.GetDirectoryName(currentPath);
                }

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    string newPath = sfd.FileName;
                    StopFileChangeWatch(page);
                    File.WriteAllText(newPath, editor.Text, GetTabEncoding(page));

                    editor.Tag = newPath;
                    page.Text = Path.GetFileName(newPath);
                    ApplySyntaxHighlighting(editor, newPath);

                    editor.SetSavePoint();
                    UpdateToolbarState();
                    AddToRecentFiles(newPath);
                    StartFileChangeWatch(page, newPath);
                }
            }
        }

        private void SaveAllFiles()
        {
            var modifiedPages = tcDocuments.TabPages.Cast<TabPage>().Where(p => p.Text.EndsWith("*")).ToList();
            if (modifiedPages.Count == 0)
            {
                MessageBox.Show("All files are already saved.", "nplus", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var res = MessageBox.Show($"Are you sure you want to save {modifiedPages.Count} modified files?", "Confirm Save All", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res != DialogResult.Yes) return;

            foreach (TabPage page in modifiedPages)
            {
                var editor = page.Controls[0] as Scintilla;
                if (editor != null) SaveSpecificFile(editor, page);
            }
        }

        private void SaveSpecificFile(Scintilla editor, TabPage page)
        {
            string path = editor.Tag as string;
            if (string.IsNullOrEmpty(path))
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    if (sfd.ShowDialog() == DialogResult.OK) path = sfd.FileName; else return;
                }
            }

            // Temporarily stop watching to avoid self-triggering on our own save
            StopFileChangeWatch(page);

            File.WriteAllText(path, editor.Text, GetTabEncoding(page));
            editor.Tag = path;

            page.Text = Path.GetFileName(path);
            ApplySyntaxHighlighting(editor, path);
            UpdateToolbarState();
            StartFileChangeWatch(page, path);
        }

        private void ApplyCSharpLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.Cpp;

            editor.Styles[Style.Cpp.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Cpp.Comment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Cpp.CommentLine].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Cpp.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;
            editor.Styles[Style.Cpp.String].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Cpp.Character].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Cpp.Preprocessor].ForeColor = _isDarkMode ? Color.Gray : Color.DarkGray;
            editor.Styles[Style.Cpp.Operator].ForeColor = _isDarkMode ? Color.Silver : Color.Black;

            editor.Styles[Style.Cpp.Word].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.SetKeywords(0, "abstract as base break case catch class const continue default delegate do else enum event explicit extern false finally fixed for foreach goto if implicit in int interface internal is lock namespace new null object operator out override params private protected public readonly ref return sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while");

            editor.Styles[Style.Cpp.Identifier].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.Black;
            editor.Styles[Style.Cpp.Word2].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.SetKeywords(1, "byte char bool decimal double float object sbyte short string void var");
        }

        private void ApplySqlLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.Sql;

            editor.Styles[Style.Sql.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Sql.Comment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Sql.CommentLine].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Sql.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;
            editor.Styles[Style.Sql.String].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Sql.Operator].ForeColor = _isDarkMode ? Color.Silver : Color.Black;

            editor.Styles[Style.Sql.Word].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.SetKeywords(0, "select insert update delete from where and or not in as inner join left right outer on group by order asc desc having create table drop alter index view trigger procedure function begin end declare set if else return commit rollback go use");

            editor.Styles[Style.Sql.Word2].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.SetKeywords(1, "varchar nvarchar int datetime bit char float numeric max min count sum avg cast convert isnull coalesce getdate");
        }

        private void ApplyPythonLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.Python;

            editor.Styles[Style.Python.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Python.CommentLine].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Python.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;
            editor.Styles[Style.Python.String].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Python.Character].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Python.Operator].ForeColor = _isDarkMode ? Color.Silver : Color.Black;

            editor.Styles[Style.Python.Word].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.SetKeywords(0, "False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield");

            editor.Styles[Style.Python.ClassName].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.Styles[Style.Python.DefName].ForeColor = _isDarkMode ? Color.Orange : Color.DarkOrange;
        }

        private void ApplyJavaScriptLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.Cpp;

            editor.Styles[Style.Cpp.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Cpp.Comment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Cpp.CommentLine].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Cpp.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;
            editor.Styles[Style.Cpp.String].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Cpp.Character].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Cpp.Operator].ForeColor = _isDarkMode ? Color.Silver : Color.Black;

            editor.Styles[Style.Cpp.Word].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.SetKeywords(0, "abstract arguments await boolean break byte case catch char class const continue debugger default delete do double else enum eval export extends false final finally float for function goto if implements import in instanceof int interface let long native new null package private protected public return short static super switch synchronized this throw throws transient true try typeof var void volatile while with yield");

            editor.Styles[Style.Cpp.Word2].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.SetKeywords(1, "document window console Math JSON Object Array String Number Boolean RegExp Date Promise");
        }

        private void ApplyHtmlXmlLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.Html;

            editor.Styles[Style.Html.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Html.Tag].ForeColor = _isDarkMode ? Color.DeepSkyBlue : Color.Blue;
            editor.Styles[Style.Html.TagUnknown].ForeColor = _isDarkMode ? Color.IndianRed : Color.Red;
            editor.Styles[Style.Html.Attribute].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.Teal;
            editor.Styles[Style.Html.AttributeUnknown].ForeColor = _isDarkMode ? Color.IndianRed : Color.Red;
            editor.Styles[Style.Html.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;

            editor.Styles[Style.Html.DoubleString].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Html.SingleString].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;

            editor.Styles[Style.Html.Other].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Html.Comment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Html.Entity].ForeColor = _isDarkMode ? Color.Orange : Color.DarkOrange;

            editor.SetProperty("fold.html", "1");
        }

        private void ApplyVbLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.Vb;

            editor.Styles[Style.Vb.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Vb.Comment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Vb.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;
            editor.Styles[Style.Vb.String].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Vb.Operator].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Vb.Preprocessor].ForeColor = _isDarkMode ? Color.Gray : Color.DarkGray;
            editor.Styles[Style.Vb.Date].ForeColor = _isDarkMode ? Color.Orange : Color.DarkOrange;

            editor.Styles[Style.Vb.Keyword].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.SetKeywords(0, "addhandler addressof alias and andalso as boolean byref byte byval call case catch cbool cbyte cchar cdate cdbl cdec char cint class clng cobj const continue csbyte cshort csng cstr ctype cuint culng cushort date decimal declare default delegate dim directcast do double each else elseif end endif enum erase error event exit false finally for friend function get gettype getxmlnamespace global gosub goto handles if implements imports in inherits integer interface is isnot let lib like long loop me mod module mustinherit mustoverride mybase myclass namespace narrowing new next not nothing notinheritable notoverridable object of on operator option optional or orelse overloads overridable overrides paramarray partial private property protected public raiseevent readonly redim rem removehandler resume return sbyte select set shadows shared short single static step stop string structure sub synclock then throw to true try trycast typeof uinteger ulong ushort using variant wend when while widening with withevents writeonly xor");

            editor.Styles[Style.Vb.Keyword2].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.SetKeywords(1, "boolean byte char date decimal double integer long object sbyte short single string uinteger ulong ushort");
        }

        private void ApplyVbScriptLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.Vb;

            editor.Styles[Style.Vb.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Vb.Comment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Vb.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;
            editor.Styles[Style.Vb.String].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Vb.Operator].ForeColor = _isDarkMode ? Color.Silver : Color.Black;

            editor.Styles[Style.Vb.Keyword].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.SetKeywords(0, "and as call case class const dim do each else elseif empty end erase error execute exit explicit false for function get if in is let loop me mod new next not nothing null on option or preserve private property public randomize redim rem resume select set step stop sub then to true until wend while with xor");

            editor.Styles[Style.Vb.Keyword2].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.SetKeywords(1, "abs array asc atn cbool cbyte ccur cdate cdbl chr cint clng cos createobject csng cstr date dateadd datediff datepart dateserial datevalue day eval exp filter formatcurrency formatdatetime formatnumber formatpercent getobject hex hour inputbox instr instrrev int isarray isdate isempty isnull isnumeric isobject join lbound lcase left len loadpicture log ltrim mid minute month monthname msgbox now oct replace rgb right rnd round rtrim scriptengine second sgn sin space split sqr strcomp string strreverse tan time timer timeserial timevalue trim typename ubound ucase vartype weekday weekdayname year");
        }

        private void ApplyJavaLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.Cpp;

            editor.Styles[Style.Cpp.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Cpp.Comment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Cpp.CommentLine].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Cpp.CommentDoc].ForeColor = _isDarkMode ? Color.DarkSeaGreen : Color.DarkGreen;
            editor.Styles[Style.Cpp.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;
            editor.Styles[Style.Cpp.String].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Cpp.Character].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Cpp.Operator].ForeColor = _isDarkMode ? Color.Silver : Color.Black;

            editor.Styles[Style.Cpp.Word].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.SetKeywords(0, "abstract assert boolean break byte case catch char class const continue default do double else enum extends final finally float for goto if implements import instanceof int interface long native new null package private protected public return short static strictfp super switch synchronized this throw throws transient try void volatile while");

            editor.Styles[Style.Cpp.Word2].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.SetKeywords(1, "Boolean Byte Character Class ClassLoader Comparable Double Enum Float Integer Iterable Long Math Number Object Override Runnable Short String StringBuffer StringBuilder System Thread Throwable Void");
        }

        private void ApplyPowerShellLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.PowerShell;

            editor.Styles[Style.PowerShell.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.PowerShell.Comment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.PowerShell.CommentStream].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.PowerShell.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;
            editor.Styles[Style.PowerShell.String].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.PowerShell.Character].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.PowerShell.Operator].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.PowerShell.Variable].ForeColor = _isDarkMode ? Color.Orange : Color.DarkOrange;
            editor.Styles[Style.PowerShell.HereString].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.PowerShell.HereCharacter].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;

            editor.Styles[Style.PowerShell.Keyword].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.SetKeywords(0, "begin break catch class continue data define do dynamicparam else elseif end enum exit filter finally for foreach from function hidden if in inlinescript parallel param process return sequence switch throw trap try until using var while workflow");

            editor.Styles[Style.PowerShell.Cmdlet].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.SetKeywords(1, "add-content add-member add-type clear-content clear-host clear-item clear-variable compare-object convertfrom-csv convertfrom-json convertto-csv convertto-html convertto-json copy-item export-csv foreach-object format-list format-table get-alias get-childitem get-command get-content get-credential get-date get-help get-host get-item get-itemproperty get-location get-member get-module get-process get-random get-service get-unique get-variable group-object import-csv import-module invoke-command invoke-expression invoke-restmethod invoke-webrequest join-path measure-command measure-object move-item new-item new-object new-variable out-file out-host out-null out-string read-host remove-item remove-variable rename-item resolve-path select-object select-string set-content set-item set-location set-variable sort-object split-path start-process start-sleep stop-process test-connection test-path where-object write-error write-host write-output write-verbose write-warning");
        }

        private void ApplyPhpLexer(Scintilla editor)
        {
            editor.Lexer = Lexer.Cpp;

            editor.Styles[Style.Cpp.Default].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[Style.Cpp.Comment].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Cpp.CommentLine].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[Style.Cpp.CommentDoc].ForeColor = _isDarkMode ? Color.DarkSeaGreen : Color.DarkGreen;
            editor.Styles[Style.Cpp.Number].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;
            editor.Styles[Style.Cpp.String].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Cpp.Character].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[Style.Cpp.Operator].ForeColor = _isDarkMode ? Color.Silver : Color.Black;

            editor.Styles[Style.Cpp.Word].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.SetKeywords(0, "abstract and array as break callable case catch class clone const continue declare default die do echo else elseif empty enddeclare endfor endforeach endif endswitch endwhile eval exit extends final finally fn for foreach function global goto if implements include include_once instanceof insteadof interface isset list match namespace new or parent print private protected public readonly require require_once return self static switch throw trait try unset use var while xor yield yield_from");

            editor.Styles[Style.Cpp.Word2].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.SetKeywords(1, "array bool boolean callable double false float int integer iterable mixed null numeric object resource self static string true void");
        }

        private void ApplyYamlLexer(Scintilla editor)
        {
            // YAML is Scintilla lexer ID 48 (SCLEX_YAML) — not in ScintillaNET's enum
            editor.Lexer = (Lexer)48;

            // YAML style indices: 0=Default, 1=Comment, 2=Identifier/Key, 3=Keyword,
            // 4=Number, 5=Reference, 6=Document, 7=Text, 8=Error, 9=Operator
            editor.Styles[0].ForeColor = _isDarkMode ? Color.Silver : Color.Black;
            editor.Styles[1].ForeColor = _isDarkMode ? Color.SeaGreen : Color.Green;
            editor.Styles[2].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.Styles[3].ForeColor = _isDarkMode ? Color.DeepPink : Color.Blue;
            editor.Styles[4].ForeColor = _isDarkMode ? Color.Olive : Color.DarkOliveGreen;
            editor.Styles[5].ForeColor = _isDarkMode ? Color.Orange : Color.DarkOrange;
            editor.Styles[6].ForeColor = _isDarkMode ? Color.LightSkyBlue : Color.DarkCyan;
            editor.Styles[7].ForeColor = _isDarkMode ? Color.Khaki : Color.Maroon;
            editor.Styles[8].ForeColor = Color.Red;
            editor.Styles[9].ForeColor = _isDarkMode ? Color.Silver : Color.Black;

            editor.SetKeywords(0, "true false yes no null on off");
        }

        private void ApplySyntaxHighlighting(Scintilla editor, string filePath)
        {
            editor.StyleResetDefault();
            editor.Styles[Style.Default].Font = "Consolas";
            editor.Styles[Style.Default].Size = (int)(_baseEditorFontSize * _zoomLevel);
            editor.Styles[Style.Default].BackColor = _isDarkMode ? Color.FromArgb(30, 30, 35) : Color.White;
            editor.Styles[Style.Default].ForeColor = _isDarkMode ? Color.FromArgb(220, 220, 220) : Color.Black;
            editor.StyleClearAll();

            editor.Markers[BOOKMARK_MARKER].Symbol = MarkerSymbol.Circle;
            editor.Markers[BOOKMARK_MARKER].SetBackColor(_isDarkMode ? Color.DeepSkyBlue : Color.Blue);

            editor.Styles[Style.LineNumber].BackColor = _isDarkMode ? Color.FromArgb(40, 40, 45) : Color.WhiteSmoke;
            editor.Styles[Style.LineNumber].ForeColor = _isDarkMode ? Color.Gray : Color.DarkGray;

            editor.Margins[0].Width = 40;
            editor.Margins[1].Width = 20;
            editor.Margins[1].Type = MarginType.Symbol;
            editor.Margins[1].Sensitive = true;
            editor.Margins[1].Mask = (1u << BOOKMARK_MARKER);

            editor.Indicators[MARK_INDICATOR].Style = IndicatorStyle.StraightBox;
            editor.Indicators[MARK_INDICATOR].Under = true;
            editor.Indicators[MARK_INDICATOR].ForeColor = _isDarkMode ? Color.DarkCyan : Color.Cyan;
            editor.Indicators[MARK_INDICATOR].OutlineAlpha = 100;
            editor.Indicators[MARK_INDICATOR].Alpha = 50;

            editor.CaretForeColor = _isDarkMode ? Color.White : Color.Black;

            string ext = string.IsNullOrEmpty(filePath) ? ".cs" : Path.GetExtension(filePath).ToLower();
            switch (ext)
            {
                case ".sql": ApplySqlLexer(editor); break;
                case ".cs": case ".cpp": case ".h": case ".c": ApplyCSharpLexer(editor); break;
                case ".py": ApplyPythonLexer(editor); break;
                case ".js": case ".ts": ApplyJavaScriptLexer(editor); break;
                case ".html": case ".htm": case ".xml": case ".xaml": case ".xsl": case ".xslt": ApplyHtmlXmlLexer(editor); break;
                case ".json": ApplyJsonLexer(editor); break;
                case ".vb": case ".bas": ApplyVbLexer(editor); break;
                case ".vbs": ApplyVbScriptLexer(editor); break;
                case ".java": ApplyJavaLexer(editor); break;
                case ".ps1": case ".psm1": case ".psd1": case ".ps": ApplyPowerShellLexer(editor); break;
                case ".php": ApplyPhpLexer(editor); break;
                case ".yml": case ".yaml": ApplyYamlLexer(editor); break;
                case ".csv": editor.Lexer = Lexer.Null; break;
                default: editor.Lexer = Lexer.Null; break;
            }
        }
        #endregion

    }
}