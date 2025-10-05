using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Core.Config;

namespace UI
{
    public partial class MainWindow : Window
    {
        private void CreateTasksButton_Click(object sender, RoutedEventArgs e)
        {
            var cfg = ConfigStore.Load();
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.DeviceId) || string.IsNullOrWhiteSpace(cfg.AuthKey))
            {
                MessageBox.Show("No configuration found. Pair the app with the TV first.", "ATVCompanion", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // CLI should be next to the UI executable after publish; during dev you may need to copy it.
            string cliPath = Path.Combine(AppContext.BaseDirectory, "CLI.exe");
            if (!File.Exists(cliPath))
            {
                MessageBox.Show($"CLI tool not found:\n{cliPath}", "ATVCompanion", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // NOTE: CreateTasks(out string output, string cliPath)
            if (ScheduledTaskCreator.CreateTasks(out string output, cliPath))
            {
                MessageBox.Show("Scheduled tasks created.\n\n" + output, "ATVCompanion", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to create tasks.\n\n" + output, "ATVCompanion", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
