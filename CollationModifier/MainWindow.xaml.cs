using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace CollationModifier
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Closed += (s, e) => (DataContext as System.IDisposable)?.Dispose();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is ViewModels.MainViewModel viewModel)
            {
                viewModel.Password = ((System.Windows.Controls.PasswordBox)sender).Password;
            }
        }

        private void TargetCollationInput_Loaded(object sender, RoutedEventArgs e)
        {
            if (TargetCollationInput?.Template != null)
            {
                var textBox = TargetCollationInput.Template.FindName("PART_EditableTextBox", TargetCollationInput) as TextBox;
                if (textBox != null)
                {
                    AutomationProperties.SetName(textBox, AutomationProperties.GetName(TargetCollationInput));
                    AutomationProperties.SetLabeledBy(textBox, AutomationProperties.GetLabeledBy(TargetCollationInput));
                    AutomationProperties.SetHelpText(textBox, AutomationProperties.GetHelpText(TargetCollationInput));
                }
            }
        }
    }
}
