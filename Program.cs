using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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
            byte[] expected;
            using (Stream src = asm.GetManifestResourceStream(resourceName))
            {
                if (src == null)
                {
                    throw new InvalidOperationException(
                        "Embedded native resource not found: " + resourceName);
                }

                using var ms = new MemoryStream();
                src.CopyTo(ms);
                expected = ms.ToArray();
            }

            // Skip rewriting only when the on-disk copy is byte-for-byte the embedded
            // one. A length-only check would let a same-user attacker swap in a
            // matching-size malicious Scintilla.dll/Lexilla.dll that we'd then load via
            // LoadLibrary. Verifying the full hash makes the cached DLL exactly the one
            // we shipped, and still skips the write (and the clobber of an instance that
            // has it mapped) on the fast path.
            if (File.Exists(destPath) && FileMatchesHash(destPath, expected))
            {
                return;
            }

            try
            {
                using FileStream dst = new FileStream(
                    destPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                dst.Write(expected, 0, expected.Length);
            }
            catch (IOException)
            {
                // Another instance already extracted / has it loaded. Tolerate that only
                // if what's on disk is the genuine DLL; otherwise surface the failure
                // rather than load a file we couldn't validate or replace.
                if (!File.Exists(destPath) || !FileMatchesHash(destPath, expected)) throw;
            }
        }

        // True if the file's SHA-256 equals the hash of <paramref name="expected"/>.
        private static bool FileMatchesHash(string path, byte[] expected)
        {
            try
            {
                byte[] want = SHA256.HashData(expected);
                byte[] got;
                using (FileStream fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    got = SHA256.HashData(fs);
                }
                return CryptographicOperations.FixedTimeEquals(want, got);
            }
            catch
            {
                return false;
            }
        }
    }
}
