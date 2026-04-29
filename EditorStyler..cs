using ScintillaNET;
using System.Drawing;

namespace nplus
{
    public static class EditorStyler
    {
        public static void ApplyHighEnergyTheme(Scintilla editor)
        {
            // Set Lexer to C++ (Scintilla uses this for C# as well)
            editor.Lexer = Lexer.Cpp;

            // Base Colors
            Color bg = Color.FromArgb(20, 20, 25);
            Color fg = Color.FromArgb(240, 240, 240);
            Color keyword = Color.FromArgb(255, 0, 150); // Neon Pink
            Color identifier = Color.FromArgb(0, 255, 255); // Cyan

            editor.StyleResetDefault();
            editor.Styles[Style.Default].Font = "Consolas";
            editor.Styles[Style.Default].Size = 11;
            editor.Styles[Style.Default].BackColor = bg;
            editor.Styles[Style.Default].ForeColor = fg;
            editor.StyleClearAll();

            // Line Numbers
            editor.Margins[0].Width = 45;
            editor.Styles[Style.LineNumber].BackColor = Color.FromArgb(30, 30, 35);
            editor.Styles[Style.LineNumber].ForeColor = Color.DimGray;

            // Syntax Specifics
            editor.Styles[Style.Cpp.Word].ForeColor = keyword;
            editor.Styles[Style.Cpp.Word2].ForeColor = Color.FromArgb(173, 127, 255);
            editor.Styles[Style.Cpp.String].ForeColor = Color.FromArgb(230, 219, 116);
            editor.Styles[Style.Cpp.Comment].ForeColor = Color.FromArgb(117, 113, 94);
            editor.Styles[Style.Cpp.Identifier].ForeColor = identifier;

            // Keywords - Standard C# set
            string keywords = "abstract as base break case catch class const continue default delegate do else enum event explicit extern false finally fixed for foreach goto if implicit in int interface internal is lock namespace new null object operator out override params private protected public readonly ref return sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while";
            editor.SetKeywords(0, keywords);

            // Caret and Selection
            editor.CaretForeColor = Color.White;
            editor.SetSelectionBackColor(true, Color.FromArgb(60, 60, 70));
        }
    }
}