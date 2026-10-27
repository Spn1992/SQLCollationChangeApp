using CollationModifier.Models;
using CollationModifier.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: InternalsVisibleTo("CollationModifier.Tests")]

namespace CollationModifier
{
    public partial class MainForm : Form
    {
        private readonly LoggerService _logger;
        private readonly DatabaseService _databaseService;

        private bool _isBusy;
        private bool _isConnected;
        private bool _isInitializing;
        private string? _previousAuthType;
        private List<DatabaseInfo> _databases = new List<DatabaseInfo>();

        internal ComboBox CmbAuthType => cmbAuthType;
        internal TextBox TxtUsername => txtUsername;
        internal TextBox TxtPassword => txtPassword;
        internal ToolStripStatusLabel StatusLabel => statusLabel;
        internal StatusStrip StatusStrip => statusStrip;

        public MainForm()
        {
            InitializeComponent();

            _logger = new LoggerService();
            _databaseService = new DatabaseService(_logger);

            InitializeFormState();
        }

        private void InitializeFormState()
        {
            _isInitializing = true;
            try
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
                _previousAuthType = cmbAuthType.SelectedItem?.ToString();
            }
            finally
            {
                _isInitializing = false;
            }
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
            bool prevUsernameEnabled = txtUsername.Enabled;
            bool prevPasswordEnabled = txtPassword.Enabled;

            string? currentAuthType = cmbAuthType.SelectedItem?.ToString();
            bool isSqlServerAuth = currentAuthType == "SQL Server Authentication";

            bool newUsernameEnabled = !_isBusy && isSqlServerAuth;
            bool newPasswordEnabled = !_isBusy && isSqlServerAuth;

            txtUsername.Enabled = newUsernameEnabled;
            txtPassword.Enabled = newPasswordEnabled;

            if (prevUsernameEnabled != newUsernameEnabled)
            {
                txtUsername.AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }

            if (prevPasswordEnabled != newPasswordEnabled)
            {
                txtPassword.AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }

            if (!_isInitializing && !_isBusy && currentAuthType != null && currentAuthType != _previousAuthType)
            {
                if (isSqlServerAuth)
                {
                    statusLabel.Text = "SQL Server Authentication selected. Username and Password fields enabled.";
                }
                else
                {
                    statusLabel.Text = "Windows Authentication selected. Username and Password fields disabled.";
                }

                statusStrip.AccessibilityNotifyClients(AccessibleEvents.SystemAlert, -1);
                _previousAuthType = currentAuthType;
            }

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

        private async void btnConnect_Click(object sender, EventArgs e)
        {
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
            }
        }
    }
}
