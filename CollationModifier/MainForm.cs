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

        private void btnBrowseBackupDir_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtBackupDirectory.Text) && Directory.Exists(txtBackupDirectory.Text))
            {
                folderBrowserDialog.SelectedPath = txtBackupDirectory.Text;
            }

            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                txtBackupDirectory.Text = folderBrowserDialog.SelectedPath;
            }
        }

        private void UpdateMetadataPanel()
        {
            if (cmbDatabases.SelectedItem is DatabaseInfo selectedDb)
            {
                UpdateMetadataLabel(lblCollationValue, "Current Collation", selectedDb.Collation);
                UpdateMetadataLabel(lblCompatLevelValue, "Compatibility Level", selectedDb.CompatibilityLevel);
                UpdateMetadataLabel(lblOwnerValue, "Owner", selectedDb.Owner);
                UpdateMetadataLabel(lblSizeValue, "Size", selectedDb.Size);
            }
            else
            {
                UpdateMetadataLabel(lblCollationValue, "Current Collation", null);
                UpdateMetadataLabel(lblCompatLevelValue, "Compatibility Level", null);
                UpdateMetadataLabel(lblOwnerValue, "Owner", null);
                UpdateMetadataLabel(lblSizeValue, "Size", null);
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
            btnBrowseBackupDir.Enabled = !_isBusy && _isConnected;

            var selectedDb = cmbDatabases.SelectedItem as DatabaseInfo;
            bool canProcess = _isConnected && !_isBusy && selectedDb != null && !string.IsNullOrWhiteSpace(txtTargetCollation.Text);
            btnProcess.Enabled = canProcess;

            progressBar.Visible = _isBusy;
        }

        private static void RestoreOrSetFocus(Control? preferredControl, Control? fallbackControl = null)
        {
            if (preferredControl != null && preferredControl.Enabled && preferredControl.Visible)
            {
                preferredControl.Focus();
            }
            else if (fallbackControl != null && fallbackControl.Enabled && fallbackControl.Visible)
            {
                fallbackControl.Focus();
            }
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            Control? activeControl = ActiveControl;
            _isBusy = true;
            statusLabel.Text = "Connecting to server...";
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
                    statusLabel.Text = "Connected successfully. Fetching databases...";
                    await LoadDatabasesAsync();
                    statusLabel.Text = "Ready";
                }
                else
                {
                    _isConnected = false;
                    statusLabel.Text = "Connection failed. Check credentials and server name.";
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                statusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isBusy = false;
                UpdateUIState();

                if (_isConnected)
                {
                    RestoreOrSetFocus(cmbDatabases, activeControl ?? btnConnect);
                }
                else
                {
                    RestoreOrSetFocus(activeControl, btnConnect);
                }
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
                statusLabel.Text = $"Failed to load databases: {ex.Message}";
            }
        }

        private async void btnProcess_Click(object sender, EventArgs e)
        {
            if (cmbDatabases.SelectedItem is not DatabaseInfo selectedDb) return;

            Control? activeControl = ActiveControl;
            _isBusy = true;
            string targetCollation = txtTargetCollation.Text.Trim();
            string backupDir = txtBackupDirectory.Text.Trim();

            UpdateUIState();
            _logger.LogInfo($"Starting collation change process for database: {selectedDb.Name} to {targetCollation}");
            string? backupFile = null;

            try
            {
                // 1. Pre-flight
                statusLabel.Text = "Running pre-flight checks...";
                await _databaseService.PreflightCheckAsync(selectedDb.Name);

                // 2. Automated Backup
                statusLabel.Text = "Starting automated backup...";
                backupFile = await _databaseService.BackupDatabaseAsync(selectedDb.Name, backupDir);

                // 3. Execution Phase
                statusLabel.Text = "Altering database collation... This may take a while.";
                await _databaseService.AlterCollationAsync(selectedDb.Name, targetCollation);

                statusLabel.Text = "Collation changed successfully!";
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
                    statusLabel.Text = $"Process failed: {ex.Message}. Attempting to restore from backup...";
                    try
                    {
                        await _databaseService.RestoreDatabaseAsync(selectedDb.Name, backupFile);
                        statusLabel.Text = $"Process failed: {ex.Message}. Database restored successfully.";
                    }
                    catch (Exception restoreEx)
                    {
                        _logger.LogError("Rollback/Restore failed.", restoreEx);
                        statusLabel.Text = $"Process failed: {ex.Message}. CRITICAL: Auto-restore also failed: {restoreEx.Message}";
                    }
                }
                else
                {
                    statusLabel.Text = $"Process failed before backup: {ex.Message}";
                }
            }
            finally
            {
                _isBusy = false;
                UpdateUIState();

                RestoreOrSetFocus(activeControl, btnProcess);
            }
        }
    }
}
