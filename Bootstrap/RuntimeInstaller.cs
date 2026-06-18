using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace nplus.Bootstrap
{
    /// <summary>
    /// Detection + download + install logic for the .NET 8 Desktop Runtime.
    /// Kept free of UI and of the embedded-payload concerns so it can be exercised
    /// in isolation (see tests\RuntimeInstaller.Tests.csproj, which compiles this
    /// same source file).
    /// </summary>
    internal static class RuntimeInstaller
    {
        // Official Microsoft channel link → latest .NET 8 Windows Desktop Runtime (x64).
        internal const string InstallerUrl =
            "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";

        internal const string InstallerArgs = "/install /passive /norestart";

        /// <summary>True if a Microsoft.WindowsDesktop.App 8.x runtime is installed.</summary>
        internal static bool IsDesktopRuntime8Present()
        {
            return HasDesktopRuntime8(DefaultDotnetRoots());
        }

        /// <summary>
        /// Testable core: true if any of <paramref name="roots"/> contains a
        /// shared\Microsoft.WindowsDesktop.App\8.* framework folder.
        /// </summary>
        internal static bool HasDesktopRuntime8(IEnumerable<string> roots)
        {
            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string sharedDir = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
                if (!Directory.Exists(sharedDir))
                {
                    continue;
                }

                foreach (string verDir in Directory.GetDirectories(sharedDir))
                {
                    string name = Path.GetFileName(verDir);
                    int dot = name.IndexOf('.');
                    string majorPart = dot < 0 ? name : name.Substring(0, dot);
                    if (int.TryParse(majorPart, out int major) && major == 8)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static IEnumerable<string> DefaultDotnetRoots()
        {
            string envRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(envRoot))
            {
                yield return envRoot;
            }

            // 64-bit process → ProgramFiles is the 64-bit Program Files dir.
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(pf))
            {
                yield return Path.Combine(pf, "dotnet");
            }
        }

        /// <summary>Downloads the runtime installer and runs it (elevated). True on success.</summary>
        internal static bool DownloadAndInstall()
        {
            // Download into a fresh, randomly-named per-launch directory rather than a
            // fixed %TEMP% filename — removes the predictable-path / pre-planted-file
            // (symlink) angle on what we're about to execute elevated.
            string tmpDir = Path.Combine(Path.GetTempPath(), "nplus-rt-" + Guid.NewGuid().ToString("N"));
            string installer = Path.Combine(tmpDir, "windowsdesktop-runtime-8-x64.exe");

            try
            {
                Directory.CreateDirectory(tmpDir);

                if (!DownloadFile(InstallerUrl, installer))
                {
                    return false;
                }

                // HTTPS alone doesn't prove the bytes are Microsoft's. Before launching
                // this elevated, require a valid Authenticode signature that chains to a
                // trusted root AND is signed by Microsoft. A MITM, a poisoned cache, or a
                // tampered download fails here instead of running as administrator.
                if (!IsTrustedMicrosoftInstaller(installer))
                {
                    return false;
                }

                if (!RunInstaller(installer, InstallerArgs, elevate: true, out int code))
                {
                    return false; // most commonly: user cancelled the UAC elevation prompt.
                }

                return IsInstallerSuccess(code);
            }
            finally
            {
                try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// True only if <paramref name="path"/> carries a valid Authenticode signature
        /// that chains to a trusted root AND whose signer is Microsoft. Both checks are
        /// required: WinVerifyTrust alone would accept any trusted publisher, and the
        /// signer-name check alone wouldn't prove the signature is valid/untampered.
        /// </summary>
        internal static bool IsTrustedMicrosoftInstaller(string path)
        {
            return HasValidTrustedSignature(path) && IsSignedByMicrosoft(path);
        }

        private static bool IsSignedByMicrosoft(string path)
        {
            try
            {
                using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                // Distinguished name of Microsoft's code-signing certs, e.g.
                // "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, ...".
                string subject = cert.Subject ?? string.Empty;
                return subject.IndexOf("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false; // unsigned, or signature container unreadable
            }
        }

        // --- Authenticode validation via WinVerifyTrust ------------------------------

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;

        private static bool HasValidTrustedSignature(string path)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = path,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };

            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            try
            {
                Marshal.StructureToPtr(fileInfo, pFile, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFile,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = 0,
                    dwUIContext = 0,
                    pSignatureSettings = IntPtr.Zero,
                };

                Guid action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                int result;
                try
                {
                    result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                }
                finally
                {
                    // Always release the trust-provider state, regardless of the verdict.
                    data.dwStateAction = WTD_STATEACTION_CLOSE;
                    WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                }

                return result == 0; // S_OK => signature present, valid, and trusted.
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(pFile);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        /// <summary>
        /// Windows Installer success codes we accept: 0 = success,
        /// 3010 = success/reboot required, 1638 = same-or-newer already installed.
        /// </summary>
        internal static bool IsInstallerSuccess(int code)
        {
            return code == 0 || code == 3010 || code == 1638;
        }

        internal static bool DownloadFile(string url, string destPath)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                using HttpResponseMessage resp =
                    http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();

                using Stream src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                using FileStream dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                src.CopyTo(dst);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Runs an installer and waits for it. <paramref name="elevate"/> requests UAC
        /// elevation (production); tests pass false with a stub to exercise the
        /// process-launch + exit-code handling without touching the system.
        /// Returns false if the process could not be started at all.
        /// </summary>
        internal static bool RunInstaller(string path, string args, bool elevate, out int exitCode)
        {
            exitCode = -1;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = args,
                    UseShellExecute = true,
                };
                if (elevate)
                {
                    psi.Verb = "runas";
                }

                using Process p = Process.Start(psi);
                if (p == null)
                {
                    return false;
                }

                p.WaitForExit();
                exitCode = p.ExitCode;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
