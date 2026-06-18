using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace nplus.Bootstrap
{
    /// <summary>
    /// Native-AOT launcher for nplus. Runs without any installed .NET runtime,
    /// ensures the .NET 8 Desktop Runtime is present (downloading + installing it
    /// with the user's consent if not), then extracts and launches the embedded
    /// WinForms application.
    /// </summary>
    internal static class Program
    {
        private const string PayloadResource = "nplus.payload.app.gz";

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (!RuntimeInstaller.IsDesktopRuntime8Present())
                {
                    if (!PromptInstall())
                    {
                        return 1; // user declined; nothing we can do.
                    }

                    if (!RuntimeInstaller.DownloadAndInstall() || !RuntimeInstaller.IsDesktopRuntime8Present())
                    {
                        ShowError(
                            "The .NET 8 Desktop Runtime could not be installed automatically.\n\n" +
                            "You can install it manually from:\n" + RuntimeInstaller.InstallerUrl +
                            "\n\nThen start nplus again.");
                        return 2;
                    }
                }

                string appExe = ExtractPayload();
                LaunchApp(appExe, args);
                return 0;
            }
            catch (Exception ex)
            {
                ShowError("nplus failed to start.\n\n" + ex.Message);
                return 3;
            }
        }

        // ---- install prompt ----------------------------------------------------

        private static bool PromptInstall()
        {
            const uint MB_YESNO = 0x4;
            const uint MB_ICONQUESTION = 0x20;
            const int IDYES = 6;

            int result = MessageBoxW(IntPtr.Zero,
                "nplus needs the Microsoft .NET 8 Desktop Runtime, which isn't installed on this PC.\n\n" +
                "Download and install it now? (This needs administrator approval and may take a few minutes.)",
                "nplus — install .NET 8 Runtime",
                MB_YESNO | MB_ICONQUESTION);

            return result == IDYES;
        }

        // ---- payload extraction + launch --------------------------------------

        /// <summary>
        /// Writes the embedded real app to a per-user, content-addressed folder and
        /// returns the path to its exe. Re-extracts only when the bytes change.
        /// </summary>
        private static string ExtractPayload()
        {
            Assembly asm = Assembly.GetExecutingAssembly();

            // The payload is embedded GZip-compressed; decompress it back to the
            // original app exe bytes.
            byte[] payload;
            using (Stream s = asm.GetManifestResourceStream(PayloadResource))
            {
                if (s == null)
                {
                    throw new InvalidOperationException("Embedded application payload is missing.");
                }

                using var gz = new GZipStream(s, CompressionMode.Decompress);
                using var ms = new MemoryStream();
                gz.CopyTo(ms);
                payload = ms.ToArray();
            }

            byte[] payloadHash = SHA256.HashData(payload);
            string tag = Convert.ToHexString(payloadHash).Substring(0, 16);
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "nplus", "app", tag);
            Directory.CreateDirectory(dir);

            // Reuse the cached exe only when its bytes hash to exactly the embedded
            // payload. The folder name is content-addressed, but the file inside it
            // lives in a per-user-writable directory — a length-only check would let a
            // same-user attacker drop a matching-size malicious nplus.exe here that we'd
            // then launch. Verifying the full hash makes that substitution fail closed.
            string exe = Path.Combine(dir, "nplus.exe");
            if (!File.Exists(exe) || !FileMatchesHash(exe, payloadHash))
            {
                try
                {
                    File.WriteAllBytes(exe, payload);
                }
                catch (IOException)
                {
                    // A concurrent launch may already be writing/running it; tolerate
                    // that only if what landed on disk is the genuine payload.
                    if (!File.Exists(exe) || !FileMatchesHash(exe, payloadHash)) throw;
                }
            }

            return exe;
        }

        // True if the file at <paramref name="path"/> hashes (SHA-256) to expectedHash.
        private static bool FileMatchesHash(string path, byte[] expectedHash)
        {
            try
            {
                byte[] got;
                using (FileStream fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    got = SHA256.HashData(fs);
                }
                return CryptographicOperations.FixedTimeEquals(expectedHash, got);
            }
            catch
            {
                return false;
            }
        }

        private static void LaunchApp(string exe, string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe),
            };

            // Forward our command line (e.g. file paths from "Open with…") verbatim.
            foreach (string a in args)
            {
                psi.ArgumentList.Add(a);
            }

            Process.Start(psi);
        }

        // ---- win32 -------------------------------------------------------------

        private static void ShowError(string message)
        {
            const uint MB_ICONERROR = 0x10;
            MessageBoxW(IntPtr.Zero, message, "nplus", MB_ICONERROR);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    }
}
