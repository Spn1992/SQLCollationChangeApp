using System;
using System.Windows;
using System.Windows.Automation.Peers;

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
                var peer = UIElementAutomationPeer.FromElement(lblCopiedStatus) ?? UIElementAutomationPeer.CreatePeerForElement(lblCopiedStatus);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
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
