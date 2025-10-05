// src/UI/Log.cs
using System;
using SysDiag = System.Diagnostics;

namespace UI   // <-- if your MainWindow uses a different namespace, match it here.
{
    /// <summary>
    /// Minimal UI logger that mirrors Log.* calls and forwards to MainWindow's ConsoleBox.
    /// Safe to call from any thread.
    /// </summary>
    public static partial class Log
    {
        // --- Information ---
        public static void Information(string message) =>
            Forward("[INFO]  ", message, m => MainWindow.Current?.LogInfo(m));

        public static void Information(string messageTemplate, params object[] args) =>
            Information(Format(messageTemplate, args));

        // --- Warning ---
        public static void Warning(string message) =>
            Forward("[WARN]  ", message, m => MainWindow.Current?.LogWarn(m));

        public static void Warning(string messageTemplate, params object[] args) =>
            Warning(Format(messageTemplate, args));

        // --- Debug ---
        public static void Debug(string message) =>
            Forward("[DEBUG] ", message, m => MainWindow.Current?.LogDebug(m));

        public static void Debug(string messageTemplate, params object[] args) =>
            Debug(Format(messageTemplate, args));

        // --- Error ---
        public static void Error(string message) =>
            Forward("[ERROR] ", message, m => MainWindow.Current?.LogError(m));

        public static void Error(string messageTemplate, params object[] args) =>
            Error(Format(messageTemplate, args));

        public static void Error(Exception ex, string message) =>
            Forward("[ERROR] ", $"{message} :: {ex}", m => MainWindow.Current?.LogError(m, ex));

        public static void Error(Exception ex, string messageTemplate, params object[] args) =>
            Error(ex, Format(messageTemplate, args));

        // Optional: Verbose => Debug
        public static void Verbose(string message) => Debug(message);
        public static void Verbose(string messageTemplate, params object[] args) => Debug(messageTemplate, args);

        // ---- helpers ----
        private static void Forward(string prefix, string message, Action<string>? uiSink)
        {
            var line = $"{prefix}{message}";
            SysDiag.Debug.WriteLine(line);  // VS Output window
            uiSink?.Invoke(message);        // In-window ConsoleBox (if MainWindow is available)
        }

        private static string Format(string template, object[] args)
        {
            try { return (args is { Length: > 0 }) ? string.Format(template, args) : template; }
            catch { return template; } // tolerate bad format strings
        }
    }
}
