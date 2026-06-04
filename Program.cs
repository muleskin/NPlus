using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace nplus
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // MUST run before anything touches the Scintilla type: extracts the
            // embedded native DLLs and tells Scintilla.NET where to find them.
            NativeBootstrap.Initialize();

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

    /// <summary>
    /// Makes the single-file build work: the native Scintilla.dll / Lexilla.dll
    /// are shipped as embedded resources inside nplus.exe (a single-file bundle
    /// can't expose them on disk for Scintilla.NET to LoadLibrary). At startup we
    /// write them out to a per-user folder and point Scintilla.NET at it via
    /// <c>ScintillaNET.ScintillaNativeLibrary.SatelliteDirectory</c>, which it
    /// probes before any of its file-system fallbacks.
    /// </summary>
    internal static class NativeBootstrap
    {
        private static readonly string[] NativeDlls = { "Scintilla.dll", "Lexilla.dll" };
        private const string ResourcePrefix = "nplus.native.";

        internal static void Initialize()
        {
            Assembly asm = Assembly.GetExecutingAssembly();

            // Version-stamp the extraction dir so upgraded exes don't reuse stale DLLs.
            string version = asm.GetName().Version?.ToString() ?? "0";
            string targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "nplus", "native", version);
            Directory.CreateDirectory(targetDir);

            foreach (string name in NativeDlls)
            {
                ExtractIfNeeded(asm, ResourcePrefix + name, Path.Combine(targetDir, name));
            }

            // Referencing ScintillaNativeLibrary (a standalone static class) does NOT
            // trigger the Scintilla control's native-loading static ctor, so this is
            // safe to set here and is honored on first use of the editor.
            ScintillaNET.ScintillaNativeLibrary.SatelliteDirectory = targetDir;
        }

        private static void ExtractIfNeeded(Assembly asm, string resourceName, string destPath)
        {
            using Stream src = asm.GetManifestResourceStream(resourceName);
            if (src == null)
            {
                throw new InvalidOperationException(
                    "Embedded native resource not found: " + resourceName);
            }

            // Skip rewriting if an identically sized copy is already present (fast path,
            // and avoids clobbering a DLL another nplus instance may have mapped).
            if (File.Exists(destPath) && new FileInfo(destPath).Length == src.Length)
            {
                return;
            }

            try
            {
                using FileStream dst = new FileStream(
                    destPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                src.CopyTo(dst);
            }
            catch (IOException)
            {
                // Another instance already extracted / has it loaded — fine as long as
                // the file exists. Re-throw only if it's genuinely missing.
                if (!File.Exists(destPath)) throw;
            }
        }
    }
}
