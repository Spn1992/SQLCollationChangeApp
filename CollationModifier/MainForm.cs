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

        private void SetStatus(string text, string? toolTipText = null)
        {
            statusLabel.Text = text;
            statusLabel.ToolTipText = toolTipText ?? text;
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            _isBusy = true;
            SetStatus("Connecting to server...");
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
                    SetStatus("Connected successfully. Fetching databases...");
                    await LoadDatabasesAsync();
                    SetStatus("Ready");
                }
                else
                {
                    _isConnected = false;
                    SetStatus("Connection failed. Check credentials and server name.");
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                var (statusText, toolTipText) = ErrorFormattingHelper.FormatConnectionError(ex);
                SetStatus(statusText, toolTipText);
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
                var (statusText, toolTipText) = ErrorFormattingHelper.FormatLoadDatabasesError(ex);
                SetStatus(statusText, toolTipText);
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
                SetStatus("Running pre-flight checks...");
                await _databaseService.PreflightCheckAsync(selectedDb.Name);

                // 2. Automated Backup
                SetStatus("Starting automated backup...");
                backupFile = await _databaseService.BackupDatabaseAsync(selectedDb.Name, backupDir);

                // 3. Execution Phase
                SetStatus("Altering database collation... This may take a while.");
                await _databaseService.AlterCollationAsync(selectedDb.Name, targetCollation);

                SetStatus("Collation changed successfully!");
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
                    var (restoringStatus, restoringToolTip, _) = ErrorFormattingHelper.FormatProcessError(ex, backupFile, restoreAttempted: false);
                    SetStatus(restoringStatus, restoringToolTip);
                    try
                    {
                        await _databaseService.RestoreDatabaseAsync(selectedDb.Name, backupFile);
                        var (restoredStatus, restoredToolTip, dialogMsg) = ErrorFormattingHelper.FormatProcessError(ex, backupFile, restoreAttempted: true);
                        SetStatus(restoredStatus, restoredToolTip);
                        MessageBox.Show(this, dialogMsg, "Collation Change Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch (Exception restoreEx)
                    {
                        _logger.LogError("Rollback/Restore failed.", restoreEx);
                        var (failStatus, failToolTip, dialogMsg) = ErrorFormattingHelper.FormatProcessError(ex, backupFile, restoreEx: restoreEx, restoreAttempted: true);
                        SetStatus(failStatus, failToolTip);
                        MessageBox.Show(this, dialogMsg, "Collation Change Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    var (noBackupStatus, noBackupToolTip, dialogMsg) = ErrorFormattingHelper.FormatProcessError(ex, backupFile: null);
                    SetStatus(noBackupStatus, noBackupToolTip);
                    MessageBox.Show(this, dialogMsg, "Collation Change Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                _isBusy = false;
                UpdateUIState();
            }
        }
    }

    public static class ErrorFormattingHelper
    {
        public static string GetSafeErrorMessage(Exception? ex)
        {
            if (ex == null) return "An unknown error occurred.";

            string message = ex.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = ex.GetType().Name;
            }

            if (ex.InnerException != null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
            {
                message += $" ({ex.InnerException.Message})";
            }

            return message;
        }

        public static (string StatusText, string ToolTipText) FormatConnectionError(Exception ex)
        {
            string details = GetSafeErrorMessage(ex);
            return ("Connection error occurred.", $"Error: {details}");
        }

        public static (string StatusText, string ToolTipText) FormatLoadDatabasesError(Exception ex)
        {
            string details = GetSafeErrorMessage(ex);
            return ("Failed to load databases.", $"Failed to load databases: {details}");
        }

        public static (string StatusText, string ToolTipText, string DialogMessage) FormatProcessError(Exception ex, string? backupFile, Exception? restoreEx = null, bool restoreAttempted = false)
        {
            string primaryError = GetSafeErrorMessage(ex);

            if (backupFile != null)
            {
                if (restoreAttempted)
                {
                    if (restoreEx != null)
                    {
                        string restoreError = GetSafeErrorMessage(restoreEx);
                        return (
                            "Process failed. Auto-restore failed.",
                            $"Process failed: {primaryError}. CRITICAL: Auto-restore also failed: {restoreError}",
                            $"Process failed: {primaryError}\n\nCRITICAL: Auto-restore from backup also failed: {restoreError}"
                        );
                    }
                    else
                    {
                        return (
                            "Process failed. Database restored successfully.",
                            $"Process failed: {primaryError}. Database restored successfully.",
                            $"Process failed: {primaryError}\n\nDatabase was restored successfully from backup."
                        );
                    }
                }
                else
                {
                    return (
                        "Process failed. Restoring from backup...",
                        $"Process failed: {primaryError}. Attempting to restore from backup...",
                        ""
                    );
                }
            }
            else
            {
                return (
                    "Process failed before backup.",
                    $"Process failed before backup: {primaryError}",
                    $"Process failed before backup: {primaryError}"
                );
            }
        }
    }
}
