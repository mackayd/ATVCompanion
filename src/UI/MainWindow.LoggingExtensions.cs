using System;

/// <summary>
/// Lightweight logging shim to avoid external dependencies while matching existing call sites.
/// Provides Debug/Information/Info/Warning/Warn/Error overloads (including Exception overloads).
/// </summary>
public static class Log
{
    public static void Debug(string message) =>
        System.Diagnostics.Debug.WriteLine($"[DEBUG] {message}");

    // Serilog-style name used by some call sites
    public static void Information(string message) =>
        System.Diagnostics.Debug.WriteLine($"[INFO] {message}");

    // Alias to keep both naming styles working
    public static void Info(string message) => Information(message);

    public static void Warning(string message) =>
        System.Diagnostics.Debug.WriteLine($"[WARN] {message}");

    // Alias
    public static void Warn(string message) => Warning(message);

    public static void Error(string message) =>
        System.Diagnostics.Debug.WriteLine($"[ERROR] {message}");

    // Matches calls like: Log.Error("something failed", ex)
    public static void Error(string message, Exception ex) =>
        System.Diagnostics.Debug.WriteLine($"[ERROR] {message} :: {ex}");

    // Optional Serilog-style signature: Log.Error(ex, "message")
    public static void Error(Exception ex, string? message = null) =>
        System.Diagnostics.Debug.WriteLine($"[ERROR] {message ?? "(no message)"} :: {ex}");
}
