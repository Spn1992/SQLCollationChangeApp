using CollationModifier.Models;
using CollationModifier.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CollationModifier
{
    public partial class MainForm : Form
    {
        private readonly LoggerService _logger;
        private readonly DatabaseService _databaseService;

        private bool _isBusy;
        private bool _isConnected;
        private List<DatabaseInfo> _databases = new List<DatabaseInfo>();

        public MainForm()
        {
            InitializeComponent();

            _logger = new LoggerService();
            _databaseService = new DatabaseService(_logger);

            InitializeFormState();
        }

        private void InitializeFormState()
        {
            cmbAuthType.Items.Clear();
            cmbAuthType.Items.Add("Windows Authentication");
            cmbAuthType.Items.Add("SQL Server Authentication");
            cmbAuthType.SelectedIndex = 0;

            txtBackupDirectory.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "SQLBackups"
            );

            UpdateUIState();
        }

        private void cmbAuthType_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateUIState();
        }

        private void cmbDatabases_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateMetadataPanel();
            UpdateUIState();
        }

        private void txtTargetCollation_TextChanged(object sender, EventArgs e)
        {
            UpdateUIState();
        }

        private void txtBackupDirectory_TextChanged(object sender, EventArgs e)
        {
            UpdateUIState();
        }

        private void UpdateMetadataPanel()
        {
            if (cmbDatabases.SelectedItem is DatabaseInfo selectedDb)
            {
                lblCollationValue.Text = string.IsNullOrEmpty(selectedDb.Collation) ? "-" : selectedDb.Collation;
                lblCompatLevelValue.Text = string.IsNullOrEmpty(selectedDb.CompatibilityLevel) ? "-" : selectedDb.CompatibilityLevel;
                lblOwnerValue.Text = string.IsNullOrEmpty(selectedDb.Owner) ? "-" : selectedDb.Owner;
                lblSizeValue.Text = string.IsNullOrEmpty(selectedDb.Size) ? "-" : selectedDb.Size;
            }
            else
            {
                lblCollationValue.Text = "-";
                lblCompatLevelValue.Text = "-";
                lblOwnerValue.Text = "-";
                lblSizeValue.Text = "-";
            }
        }

        private void UpdateUIState()
        {
            bool isSqlServerAuth = cmbAuthType.SelectedItem?.ToString() == "SQL Server Authentication";

            txtUsername.Enabled = !_isBusy && isSqlServerAuth;
            txtPassword.Enabled = !_isBusy && isSqlServerAuth;
            txtServerName.Enabled = !_isBusy;
            cmbAuthType.Enabled = !_isBusy;
            btnConnect.Enabled = !_isBusy;

            lblDisconnectedOverlay.Visible = !_isConnected;
            panelDashboardContent.Enabled = _isConnected;

            cmbDatabases.Enabled = !_isBusy && _isConnected;
            txtTargetCollation.Enabled = !_isBusy && _isConnected;
            txtBackupDirectory.Enabled = !_isBusy && _isConnected;

            var selectedDb = cmbDatabases.SelectedItem as DatabaseInfo;
            bool canProcess = _isConnected && !_isBusy && selectedDb != null && !string.IsNullOrWhiteSpace(txtTargetCollation.Text);
            btnProcess.Enabled = canProcess;

            progressBar.Visible = _isBusy;
        }

        private void AnnounceStatusMessage(string message, bool isError = false)
        {
            statusLabel.Text = message;
            try
            {
                statusStrip.AccessibilityNotifyClients(AccessibleEvents.NameChanged, -1);
            }
            catch
            {
                // Ignore accessibility notification failures
            }
        }

        private void ClearFieldErrors()
        {
            errorProviderMain.SetError(txtServerName, string.Empty);
            errorProviderMain.SetError(txtUsername, string.Empty);
            errorProviderMain.SetError(txtPassword, string.Empty);
            errorProviderMain.SetError(txtTargetCollation, string.Empty);
            errorProviderMain.SetError(txtBackupDirectory, string.Empty);
            errorProviderMain.SetError(cmbDatabases, string.Empty);
        }

        private void SetFieldError(Control control, string errorMessage)
        {
            errorProviderMain.SetError(control, errorMessage);
            if (control.CanFocus && control.Enabled)
            {
                control.Focus();
            }
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            ClearFieldErrors();
            _isBusy = true;
            AnnounceStatusMessage("Connecting to server...");
            UpdateUIState();

            string server = txtServerName.Text.Trim();
            string authType = cmbAuthType.SelectedItem?.ToString() ?? "Windows Authentication";
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Text;

            _logger.LogInfo($"Attempting to connect to server: {server} using {authType}");

            try
            {
                _databaseService.SetConnectionString(server, authType, username, password);
                bool success = await _databaseService.TestConnectionAsync();

                if (success)
                {
                    _isConnected = true;
                    AnnounceStatusMessage("Connected successfully. Fetching databases...");
                    await LoadDatabasesAsync();
                    AnnounceStatusMessage("Ready");
                }
                else
                {
                    _isConnected = false;
                    string errorMsg = "Connection failed. Check credentials and server name.";
                    AnnounceStatusMessage(errorMsg, true);
                    SetFieldError(txtServerName, errorMsg);
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                AnnounceStatusMessage($"Error: {ex.Message}", true);
                SetFieldError(txtServerName, ex.Message);
            }
            finally
            {
                _isBusy = false;
                UpdateUIState();
            }
        }

        private async Task LoadDatabasesAsync()
        {
            try
            {
                _databases = await _databaseService.GetDatabasesAsync();
                cmbDatabases.DisplayMember = "Name";
                cmbDatabases.DataSource = null;
                cmbDatabases.DataSource = _databases;

                if (_databases.Count > 0)
                {
                    cmbDatabases.SelectedIndex = 0;
                }

                _logger.LogInfo($"Fetched {_databases.Count} databases.");
            }
            catch (Exception ex)
            {
                AnnounceStatusMessage($"Failed to load databases: {ex.Message}", true);
                SetFieldError(cmbDatabases, ex.Message);
            }
        }

        private async void btnProcess_Click(object sender, EventArgs e)
        {
            if (cmbDatabases.SelectedItem is not DatabaseInfo selectedDb) return;

            ClearFieldErrors();
            _isBusy = true;
            string targetCollation = txtTargetCollation.Text.Trim();
            string backupDir = txtBackupDirectory.Text.Trim();

            UpdateUIState();
            _logger.LogInfo($"Starting collation change process for database: {selectedDb.Name} to {targetCollation}");
            string? backupFile = null;

            try
            {
                // 1. Pre-flight
                AnnounceStatusMessage("Running pre-flight checks...");
                await _databaseService.PreflightCheckAsync(selectedDb.Name);

                // 2. Automated Backup
                AnnounceStatusMessage("Starting automated backup...");
                backupFile = await _databaseService.BackupDatabaseAsync(selectedDb.Name, backupDir);

                // 3. Execution Phase
                AnnounceStatusMessage("Altering database collation... This may take a while.");
                await _databaseService.AlterCollationAsync(selectedDb.Name, targetCollation);

                AnnounceStatusMessage("Collation changed successfully!");
                _logger.LogInfo($"Collation change process completed successfully for {selectedDb.Name}");

                // Refresh data
                string previousDbName = selectedDb.Name;
                await LoadDatabasesAsync();

                var reselected = _databases.FirstOrDefault(d => d.Name == previousDbName);
                if (reselected != null)
                {
                    cmbDatabases.SelectedItem = reselected;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Process collation change failed.", ex);

                if (backupFile != null)
                {
                    AnnounceStatusMessage($"Process failed: {ex.Message}. Attempting to restore from backup...", true);
                    try
                    {
                        await _databaseService.RestoreDatabaseAsync(selectedDb.Name, backupFile);
                        AnnounceStatusMessage($"Process failed: {ex.Message}. Database restored successfully.", true);
                    }
                    catch (Exception restoreEx)
                    {
                        _logger.LogError("Rollback/Restore failed.", restoreEx);
                        AnnounceStatusMessage($"Process failed: {ex.Message}. CRITICAL: Auto-restore also failed: {restoreEx.Message}", true);
                    }
                }
                else
                {
                    AnnounceStatusMessage($"Process failed before backup: {ex.Message}", true);
                    SetFieldError(txtTargetCollation, ex.Message);
                }
            }
            finally
            {
                _isBusy = false;
                UpdateUIState();
            }
        }
    }
}
