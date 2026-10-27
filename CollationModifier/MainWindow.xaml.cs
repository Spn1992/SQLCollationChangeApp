using System.Windows;

namespace CollationModifier
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is ViewModels.MainViewModel viewModel)
            {
                viewModel.Password = ((System.Windows.Controls.PasswordBox)sender).Password;
            }
        }
    }
}
