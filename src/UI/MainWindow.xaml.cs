using System;
using System.Windows;
using System.Windows.Controls;
using Core.Config; // NEW: load persisted config (Ip, Mac, Manufacturer)

namespace UI
{
    public partial class MainWindow : Window
    {
        // Allow other partials / classes in the same process to log to the UI:
        // e.g., MainWindow.Current?.LogInfo("Hello");
        public static MainWindow? Current { get; private set; }

        // Simple cap to keep the log responsive
        private const int MaxLogLines = 500;
        private readonly object _logLock = new();

        public MainWindow()
        {
            InitializeComponent();

            // >>> NEW: Restore saved config (Ip, Mac, Manufacturer) into UI <<<
            try
            {
                var cfg = ConfigStore.Load();
                if (cfg != null)
                {
                    if (!string.IsNullOrWhiteSpace(cfg.Ip) && FindName("IpBox") is TextBox ipBox)
                        ipBox.Text = cfg.Ip;

                    if (!string.IsNullOrWhiteSpace(cfg.Mac) && FindName("MacBox") is TextBox macBox)
                        macBox.Text = cfg.Mac;

                    if (FindName("BrandBox") is ComboBox brandBox)
                    {
                        var brand = string.IsNullOrWhiteSpace(cfg.Manufacturer) ? "Philips" : cfg.Manufacturer!;
                        brandBox.SelectedValue = brand;
                    }
                }
                else
                {
                    if (FindName("BrandBox") is ComboBox brandBox)
                        brandBox.SelectedValue = "Philips";
                }
            }
            catch
            {
                // Keep UI running even if config read fails
                if (FindName("BrandBox") is ComboBox brandBox)
                    brandBox.SelectedValue = "Philips";
            }
            // <<< END NEW >>>

            Current = this;
            Loaded += OnLoadedBringToFrontAndCenter;
            Closed += (_, __) => { if (ReferenceEquals(Current, this)) Current = null; };
        }

        private void OnLoadedBringToFrontAndCenter(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;

                ShowInTaskbar = true;

                // Bring to front.
                Topmost = true;
                Topmost = false;
                Activate();

                // If the window ends up off-screen (multi-monitor changes, etc.), center it.
                var wa = SystemParameters.WorkArea;

                // ActualWidth/ActualHeight are only valid after layout; if still 0, use a fallback size.
                double width = ActualWidth > 0 ? ActualWidth : Width > 0 ? Width : 800;
                double height = ActualHeight > 0 ? ActualHeight : Height > 0 ? Height : 500;

                bool offLeft = double.IsNaN(Left) || Left < wa.Left || Left > wa.Right - 50;
                bool offTop = double.IsNaN(Top) || Top < wa.Top || Top > wa.Bottom - 50;

                if (offLeft || offTop)
                {
                    Left = (wa.Left + wa.Right - width) / 2;
                    Top  = (wa.Top + wa.Bottom - height) / 2;
                }

                // Optional: announce that logging is ready
                LogInfo("UI ready.");
            }
            catch (Exception ex)
            {
                // Avoid throwing during startup. Log if possible.
                LogError("Failed to finalize window placement.", ex);
            }
        }

        // -------- In-window logging helpers --------

        /// <summary>
        /// Append a line to the on-window log TextBox (ConsoleBox or LogBox).
        /// Thread-safe; marshals to UI thread when needed. Silently no-ops if no TextBox is present.
        /// </summary>
        public void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // Ensure UI thread access
            if (!CheckAccess())
            {
                Dispatcher.Invoke(() => AppendLog(message));
                return;
            }

            try
            {
                // Try ConsoleBox first, then LogBox
                var tb = FindName("ConsoleBox") as TextBox ?? FindName("LogBox") as TextBox;
                if (tb == null) return;

                lock (_logLock)
                {
                    var stamp = DateTime.Now.ToString("HH:mm:ss");
                    tb.AppendText($"[{stamp}] {message}{Environment.NewLine}");

                    // Scroll to end
                    tb.CaretIndex = tb.Text.Length;
                    tb.ScrollToEnd();

                    // Trim to last MaxLogLines lines to keep performance snappy
                    if (tb.LineCount > MaxLogLines)
                    {
                        int firstKeepLine = tb.LineCount - MaxLogLines;
                        int charIndex = tb.GetCharacterIndexFromLineIndex(firstKeepLine);
                        tb.Text = tb.Text[charIndex..];
                        tb.CaretIndex = tb.Text.Length;
                        tb.ScrollToEnd();
                    }
                }
            }
            catch
            {
                // Never throw from logging.
            }
        }

        public void LogInfo(string message)  => AppendLog($"INFO  {message}");
        public void LogWarn(string message)  => AppendLog($"WARN  {message}");
        public void LogDebug(string message) => AppendLog($"DEBUG {message}");

        public void LogError(string message, Exception? ex = null)
        {
            if (ex == null)
                AppendLog($"ERROR {message}");
            else
                AppendLog($"ERROR {message} ({ex.GetType().Name}: {ex.Message})");
        }
    }
}
