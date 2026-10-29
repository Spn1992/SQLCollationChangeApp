using System;
using System.Windows;

namespace CollationModifier
{
    public partial class ErrorDetailsWindow : Window
    {
        public ErrorDetailsWindow()
        {
            InitializeComponent();
        }

        public ErrorDetailsWindow(string errorDetails) : this()
        {
            txtErrorDetails.Text = errorDetails;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(txtErrorDetails.Text);
                lblCopiedStatus.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy to clipboard: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
