using System;
using System.Windows.Forms;
using CollationModifier;

namespace CollationModifier.Tests
{
    public static class MainFormTests
    {
        public static void RunAllTests()
        {
            Console.WriteLine("=== Running MainForm Accessibility & UI State Tests ===");
            
            TestInitialFormState();
            TestAuthenticationSelectionChangeToSqlServer();
            TestAuthenticationSelectionChangeToWindows();

            Console.WriteLine("=== All MainForm Tests Passed Successfully! ===");
        }

        private static void TestInitialFormState()
        {
            Console.WriteLine("Running TestInitialFormState...");
            using (var form = new MainForm())
            {
                Assert(form.CmbAuthType.SelectedIndex == 0, "Default auth type index should be 0 (Windows Auth).");
                Assert(!form.TxtUsername.Enabled, "Username should be disabled by default for Windows Auth.");
                Assert(!form.TxtPassword.Enabled, "Password should be disabled by default for Windows Auth.");
                Assert(form.StatusLabel.Text == "Ready", $"Initial status label text should be 'Ready', but was '{form.StatusLabel.Text}'.");
            }
            Console.WriteLine("PASS: TestInitialFormState");
        }

        private static void TestAuthenticationSelectionChangeToSqlServer()
        {
            Console.WriteLine("Running TestAuthenticationSelectionChangeToSqlServer...");
            using (var form = new MainForm())
            {
                // Switch to SQL Server Authentication (Index 1)
                form.CmbAuthType.SelectedIndex = 1;

                Assert(form.TxtUsername.Enabled, "Username should be enabled when SQL Server Authentication is selected.");
                Assert(form.TxtPassword.Enabled, "Password should be enabled when SQL Server Authentication is selected.");
                
                string expectedStatus = "SQL Server Authentication selected. Username and Password fields enabled.";
                Assert(form.StatusLabel.Text == expectedStatus, $"Expected status label '{expectedStatus}', but got '{form.StatusLabel.Text}'.");
            }
            Console.WriteLine("PASS: TestAuthenticationSelectionChangeToSqlServer");
        }

        private static void TestAuthenticationSelectionChangeToWindows()
        {
            Console.WriteLine("Running TestAuthenticationSelectionChangeToWindows...");
            using (var form = new MainForm())
            {
                // Switch to SQL Server Auth first
                form.CmbAuthType.SelectedIndex = 1;
                Assert(form.TxtUsername.Enabled, "Username should be enabled for SQL Server Auth.");

                // Switch back to Windows Auth
                form.CmbAuthType.SelectedIndex = 0;

                Assert(!form.TxtUsername.Enabled, "Username should be disabled when Windows Authentication is selected.");
                Assert(!form.TxtPassword.Enabled, "Password should be disabled when Windows Authentication is selected.");

                string expectedStatus = "Windows Authentication selected. Username and Password fields disabled.";
                Assert(form.StatusLabel.Text == expectedStatus, $"Expected status label '{expectedStatus}', but got '{form.StatusLabel.Text}'.");
            }
            Console.WriteLine("PASS: TestAuthenticationSelectionChangeToWindows");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception($"Assertion Failed: {message}");
            }
        }
    }
}
