// Added to fix CS1929 by providing Trim() as an extension on TextBox
using System;
using System.Windows.Controls;

namespace UI.Helpers
{
    internal static class TextBoxExtensions
    {
        /// <summary>
        /// Returns the TextBox.Text trimmed (or empty string if null).
        /// Allows calling code to use: myTextBox.Trim()
        /// </summary>
        public static string Trim(this TextBox textBox)
        {
            return (textBox?.Text ?? string.Empty).Trim();
        }
    }
}