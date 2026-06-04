using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;

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
            string installer = Path.Combine(Path.GetTempPath(),
                "nplus-windowsdesktop-runtime-8-x64.exe");

            if (!DownloadFile(InstallerUrl, installer))
            {
                return false;
            }

            try
            {
                if (!RunInstaller(installer, InstallerArgs, elevate: true, out int code))
                {
                    return false; // most commonly: user cancelled the UAC elevation prompt.
                }

                return IsInstallerSuccess(code);
            }
            finally
            {
                try { if (File.Exists(installer)) File.Delete(installer); } catch { }
            }
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
