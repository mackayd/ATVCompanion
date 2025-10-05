using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace UI
{
    public static class ScheduledTaskCreator
    {
        /// <summary>
        /// Creates two Windows Scheduled Tasks that call the CLI directly:
        ///  - ATVCompanion_WakeDaily at 07:00 -> CLI.exe wake
        ///  - ATVCompanion_StandbyDaily at 23:30 -> CLI.exe standby
        ///
        /// Requires an elevated (Administrator) process to succeed.
        /// Returns true on success; 'output' has a full transcript.
        /// </summary>
        public static bool CreateTasks(out string output) =>
            CreateTasks(out output, null);

        /// <summary>
        /// Same as above, but lets you pass a specific CLI path if you want.
        /// If null, we try to resolve CLI.exe next to UI.exe or in ../CLI/bin/...
        /// </summary>
        public static bool CreateTasks(out string output, string? cliPath)
        {
            var sb = new StringBuilder();

            try
            {
                // ---- Locate CLI.exe (we require the EXE; we do not support .dll here) ----
                if (!TryResolveCliExe(cliPath, out var cliExe, out var reason))
                {
                    sb.AppendLine("CLI tool not found.");
                    sb.AppendLine(reason);
                    sb.AppendLine();
                    sb.AppendLine("Fix: build/publish the solution (Release), or copy CLI.exe next to UI.exe.");
                    output = sb.ToString().TrimEnd();
                    return false;
                }

                // ---- Build two tasks that call the CLI directly ----
                // Adjust times if you want different defaults.
                const string wakeTaskName = "ATVCompanion_WakeDaily";
                const string standbyTaskName = "ATVCompanion_StandbyDaily";
                const string wakeTime = "07:00";
                const string standbyTime = "23:30";

                // /TR needs one full command line. We quote the exe path and pass the verb.
                var wakeTR = $"\"{cliExe}\" wake";
                var standbyTR = $"\"{cliExe}\" standby";

                // Create/overwrite the tasks, run as SYSTEM with highest privileges.
                // NOTE: This requires the current process to be elevated.
                var wakeArgs =
                    $"/Create /F /RL HIGHEST /RU SYSTEM /SC DAILY /TN \"{wakeTaskName}\" /TR \"{wakeTR}\" /ST {wakeTime}";
                var standbyArgs =
                    $"/Create /F /RL HIGHEST /RU SYSTEM /SC DAILY /TN \"{standbyTaskName}\" /TR \"{standbyTR}\" /ST {standbyTime}";

                var okWake = Run("schtasks", wakeArgs, sb);
                var okStandby = Run("schtasks", standbyArgs, sb);

                if (okWake && okStandby)
                {
                    sb.AppendLine("Scheduled tasks created/updated successfully.");
                    sb.AppendLine($"  - {wakeTaskName} @ {wakeTime}  -> {wakeTR}");
                    sb.AppendLine($"  - {standbyTaskName} @ {standbyTime} -> {standbyTR}");
                    sb.AppendLine();
                    sb.AppendLine("Note: The CLI reads its config (IP/MAC/auth) from your shared ConfigStore.");
                    output = sb.ToString().TrimEnd();
                    return true;
                }

                // If either failed, include transcript for debugging.
                sb.AppendLine();
                sb.AppendLine("One or more schtasks commands failed. If you see 'Access is denied', run UI as Administrator.");
                output = sb.ToString().TrimEnd();
                return false;
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Exception: {ex.Message}");
                output = sb.ToString().TrimEnd();
                return false;
            }
        }

        // ---- helpers --------------------------------------------------------

        private static bool Run(string file, string args, StringBuilder log)
        {
            log.AppendLine($"> {file} {args}");
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            using var p = Process.Start(psi)!;
            var so = p.StandardOutput.ReadToEnd();
            var se = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (!string.IsNullOrWhiteSpace(so)) log.AppendLine(so.TrimEnd());
            if (!string.IsNullOrWhiteSpace(se)) log.AppendLine(se.TrimEnd());
            log.AppendLine($"ExitCode: {p.ExitCode}");
            log.AppendLine();
            return p.ExitCode == 0;
        }

        private static bool TryResolveCliExe(string? hint, out string cliExe, out string reason)
        {
            cliExe = string.Empty;
            reason = string.Empty;

            // 1) If caller provided an explicit path
            if (!string.IsNullOrWhiteSpace(hint))
            {
                var full = Path.GetFullPath(hint);
                if (File.Exists(full) && string.Equals(Path.GetExtension(full), ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    cliExe = full;
                    return true;
                }
                reason = $"Provided cliPath not found or not an .exe: {full}";
                return false;
            }

            // 2) Next to UI.exe (publish scenario)
            var baseDir = AppContext.BaseDirectory;
            var nextToUi = Path.Combine(baseDir, "CLI.exe");
            if (File.Exists(nextToUi))
            {
                cliExe = nextToUi;
                return true;
            }

            // 3) Dev layout: ...\src\UI\bin\<Config>\net8.0-windows\  ->  ...\src\CLI\bin\<Config>\net8.0\
            try
            {
                var uiOut = new DirectoryInfo(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var config = uiOut.Parent?.Name ?? "Release";              // e.g., "Release"
                var uiBin = uiOut.Parent?.Parent;                          // ...\src\UI\bin
                var src = uiBin?.Parent?.Parent;                           // ...\src
                if (src != null)
                {
                    foreach (var cfg in new[] { config, "Release", "Debug" })
                    {
                        var cliOut = Path.Combine(src.FullName, "CLI", "bin", cfg, "net8.0");
                        var candidate = Path.Combine(cliOut, "CLI.exe");
                        if (File.Exists(candidate))
                        {
                            cliExe = candidate;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // fall through
            }

            reason =
                "Searched for CLI.exe next to UI.exe and under ../CLI/bin/<Config>/net8.0/ but did not find it. " +
                "If you only have CLI.dll, publish the CLI as a framework-dependent exe or copy the CLI.exe to the UI output folder.";
            return false;
        }
    }
}
