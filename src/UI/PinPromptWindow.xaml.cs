using System.Windows;

namespace UI
{
    public partial class PinPromptWindow : Window
    {
        public string EnteredPin => PinText.Text?.Trim() ?? string.Empty;

        public PinPromptWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                PinText.Focus();
                PinText.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
