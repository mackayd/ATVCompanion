using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace UI
{
    /// <summary>
    /// Helper to create/update a single auto-start scheduled task that launches the Service at user logon.
    /// Uses schtasks.exe so we don't need extra NuGet packages.
    /// </summary>
    public static class ScheduledTasks
    {
        private const string TaskName = "ATVCompanion_AutoStart";

        public static bool CreateOrUpdateAutoStartTask(out string message)
        {
            try
            {
                var servicePath = FindServiceExePath();
                if (servicePath == null || !File.Exists(servicePath))
                {
                    message = "Could not locate Service.exe. Please build the Service project and try again.";
                    return false;
                }

                // Quote the path for schtasks
                var quotedService = $"\"{servicePath}\"";

                // Create/Update the task (ONLOGON trigger, highest privileges).
                // /F to force update if it exists.
                var args = $"/Create /TN \"{TaskName}\" /TR {quotedService} /SC ONLOGON /RL HIGHEST /F";
                var (ok, output) = RunSchtasks(args);
                message = ok ? "Auto-start task created/updated." : output;
                return ok;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private static (bool ok, string output) RunSchtasks(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return (false, "Failed to start schtasks.exe");

            var stdOut = proc.StandardOutput.ReadToEnd();
            var stdErr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            var ok = proc.ExitCode == 0;
            var output = ok ? stdOut : (stdOut + Environment.NewLine + stdErr).Trim();
            return (ok, output);
        }

        /// <summary>
        /// Probe a few reasonable locations for Service.exe without hardcoding backslashes.
        /// </summary>
        private static string? FindServiceExePath()
        {
            string baseDir = AppContext.BaseDirectory;

            string[] candidates = new string[]
            {
                // Same directory as UI exe (if you deploy together)
                Path.Combine(baseDir, "Service.exe"),

                // UI/bin/Release/.../ -> sibling Service/bin/Release/.../
                TryCombine(baseDir, "..", "..", "..", "..", "Service", "bin", "Release", "net8.0-windows", "Service.exe"),
                TryCombine(baseDir, "..", "..", "..", "..", "Service", "bin", "Debug",   "net8.0-windows", "Service.exe"),

                // If UI is launched from the project root (during dev)
                TryCombine(baseDir, "..", "..", "Service", "bin", "Release", "net8.0-windows", "Service.exe"),
                TryCombine(baseDir, "..", "..", "Service", "bin", "Debug",   "net8.0-windows", "Service.exe"),
            };

            return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
        }

        private static string TryCombine(params string[] parts)
        {
            try
            {
                var full = Path.GetFullPath(Path.Combine(parts));
                return full;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
