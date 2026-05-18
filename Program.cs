using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.Linq;
using System.Windows.Forms;

namespace nplus
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Route through a single-instance harness so that "Open with..." (or any
            // launch while nplus is already running) forwards the file paths to the
            // existing window instead of spawning a second copy.
            new SingleInstanceApp().Run(args);
        }
    }

    internal sealed class SingleInstanceApp : WindowsFormsApplicationBase
    {
        public SingleInstanceApp()
        {
            IsSingleInstance = true;
            EnableVisualStyles = true;
            ShutdownStyle = ShutdownMode.AfterMainFormCloses;
        }

        protected override void OnCreateMainForm()
        {
            // First launch — create the editor with whatever file args we got from CLI.
            MainForm = new EditorForm(CommandLineArgs?.ToArray() ?? Array.Empty<string>());
        }

        protected override void OnStartupNextInstance(StartupNextInstanceEventArgs e)
        {
            base.OnStartupNextInstance(e);

            // A second nplus.exe was launched (e.g. via Open With...). The framework
            // routes its args here on the original instance's UI thread.
            e.BringToForeground = true;

            if (MainForm is EditorForm editor && e.CommandLine != null && e.CommandLine.Count > 0)
            {
                editor.OpenFilesFromPaths(e.CommandLine.ToArray());
            }
        }
    }
}
