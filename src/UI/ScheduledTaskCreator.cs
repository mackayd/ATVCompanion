using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace UI
{
    public static class ScheduledTaskCreator
    {
        /// <summary>
        /// Creates Windows Scheduled Tasks that call the CLI directly:
        ///  - CompanDroid_WakeOnStartup -> triggered ONSTART -> CLI.exe wake
        ///  - CompanDroid_Standby_OnSystem1074 -> triggered on System/User32 EventID 1074 -> CLI.exe standby
        ///  - CompanDroid_Standby_OnSecurity4647 -> triggered on Security EventID 4647 -> CLI.exe standby
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
                if (!TryResolveCliExe(cliPath, out var cliExe, out var reason))
                {
                    sb.AppendLine("CLI tool not found.");
                    sb.AppendLine(reason);
                    sb.AppendLine();
                    sb.AppendLine("Fix: build/publish the solution (Release), or copy CLI.exe next to UI.exe.");
                    output = sb.ToString().TrimEnd();
                    return false;
                }

                const string wakeTaskName = "CompanDroid_WakeOnStartup";
                const string standbyTaskSystem1074 = "CompanDroid_Standby_OnSystem1074";
                const string standbyTaskSecurity4647 = "CompanDroid_Standby_OnSecurity4647";

                var wakeTR = $"\"{cliExe}\" wake";
                var standbyTR = $"\"{cliExe}\" standby";

                const string system1074Query = "*[System[Provider[@Name='User32'] and EventID=1074]]";
                const string security4647Query = "*[System[EventID=4647]]";

                var okWake = Run("schtasks", sb,
                    "/Create", "/F", "/RL", "HIGHEST", "/RU", "SYSTEM",
                    "/SC", "ONSTART", "/TN", wakeTaskName, "/TR", wakeTR);

                var okStandbySystem = Run("schtasks", sb,
                    "/Create", "/F", "/RL", "HIGHEST", "/RU", "SYSTEM",
                    "/SC", "ONEVENT", "/EC", "System", "/MO", system1074Query,
                    "/TN", standbyTaskSystem1074, "/TR", standbyTR);

                var okStandbySecurity = Run("schtasks", sb,
                    "/Create", "/F", "/RL", "HIGHEST", "/RU", "SYSTEM",
                    "/SC", "ONEVENT", "/EC", "Security", "/MO", security4647Query,
                    "/TN", standbyTaskSecurity4647, "/TR", standbyTR);

                if (okWake && okStandbySystem && okStandbySecurity)
                {
                    sb.AppendLine("Scheduled tasks created/updated successfully.");
                    sb.AppendLine($"  - {wakeTaskName} (ONSTART) -> {wakeTR}");
                    sb.AppendLine($"  - {standbyTaskSystem1074} (System/User32/1074) -> {standbyTR}");
                    sb.AppendLine($"  - {standbyTaskSecurity4647} (Security/4647) -> {standbyTR}");
                    sb.AppendLine();
                    sb.AppendLine("Note: The CLI reads its config (IP/MAC/auth) from your shared ConfigStore.");
                    output = sb.ToString().TrimEnd();
                    return true;
                }

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

        private static bool Run(string file, StringBuilder log, params string[] args)
        {
            log.AppendLine($"> {file} {FormatArgsForLog(args)}");

            var psi = new ProcessStartInfo
            {
                FileName = file,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

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

        private static string FormatArgsForLog(string[] args)
        {
            static string QuoteIfNeeded(string value) =>
                value.Contains(' ') ? $"\"{value}\"" : value;

            var sb = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(QuoteIfNeeded(args[i]));
            }

            return sb.ToString();
        }

        private static bool TryResolveCliExe(string? hint, out string cliExe, out string reason)
        {
            cliExe = string.Empty;
            reason = string.Empty;

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

            var baseDir = AppContext.BaseDirectory;
            var nextToUi = Path.Combine(baseDir, "CLI.exe");
            if (File.Exists(nextToUi))
            {
                cliExe = nextToUi;
                return true;
            }

            try
            {
                var uiOut = new DirectoryInfo(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var config = uiOut.Parent?.Name ?? "Release";
                var uiBin = uiOut.Parent?.Parent;
                var src = uiBin?.Parent?.Parent;
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
