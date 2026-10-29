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
            statusStrip.ShowItemToolTips = true;
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

        private void SetStatus(string message, string? toolTipText = null)
        {
            statusLabel.Text = message;
            statusLabel.ToolTipText = toolTipText ?? message;
        }

        private void ShowErrorDetailsDialog(string title, string details)
        {
#if NETFRAMEWORK
            try
            {
                using (var form = new Form())
                {
                    form.Text = title;
                    form.Size = new Size(550, 420);
                    form.StartPosition = FormStartPosition.CenterParent;
                    form.MinimizeBox = false;
                    form.MaximizeBox = false;

                    var txtBox = new TextBox
                    {
                        Multiline = true,
                        ReadOnly = true,
                        ScrollBars = ScrollBars.Both,
                        Dock = DockStyle.Fill,
                        Text = details,
                        Font = new Font("Consolas", 9.5f)
                    };

                    var panelButtons = new Panel
                    {
                        Dock = DockStyle.Bottom,
                        Height = 45
                    };

                    var btnCopy = new Button
                    {
                        Text = "Copy Details",
                        Width = 110,
                        Height = 30,
                        Left = 12,
                        Top = 8
                    };
                    btnCopy.Click += (s, e) =>
                    {
                        try
                        {
                            Clipboard.SetText(details);
                            MessageBox.Show("Error details copied to clipboard.", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception clipEx)
                        {
                            MessageBox.Show($"Unable to copy to clipboard: {clipEx.Message}", "Clipboard Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    };

                    var btnClose = new Button
                    {
                        Text = "Close",
                        Width = 85,
                        Height = 30,
                        Left = 435,
                        Top = 8,
                        DialogResult = DialogResult.OK
                    };

                    panelButtons.Controls.Add(btnCopy);
                    panelButtons.Controls.Add(btnClose);

                    form.Controls.Add(txtBox);
                    form.Controls.Add(panelButtons);
                    form.AcceptButton = btnClose;
                    form.CancelButton = btnClose;

                    form.ShowDialog(this);
                }
            }
            catch
            {
                MessageBox.Show(details, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
#endif
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

            bool canEditConnection = !_isBusy && !_isConnected;
            txtUsername.Enabled = canEditConnection && isSqlServerAuth;
            txtPassword.Enabled = canEditConnection && isSqlServerAuth;
            txtServerName.Enabled = canEditConnection;
            cmbAuthType.Enabled = canEditConnection;

            btnConnect.Text = _isConnected ? "Disconnect" : "Connect";
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
            if (_isConnected)
            {
                _isConnected = false;
                _databases.Clear();
                cmbDatabases.DataSource = null;
                UpdateMetadataPanel();
                statusLabel.Text = "Disconnected.";
                _logger.LogInfo("Disconnected from server.");
                UpdateUIState();
                return;
            }
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
                var (statusText, toolTipText, detailedError) = ErrorFormattingHelper.FormatConnectionError(ex);
                SetStatus(statusText, toolTipText);
                ShowErrorDetailsDialog("Connection Error", detailedError);
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
                var (statusText, toolTipText, detailedError) = ErrorFormattingHelper.FormatLoadDatabasesError(ex);
                SetStatus(statusText, toolTipText);
                ShowErrorDetailsDialog("Load Databases Error", detailedError);
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
                    try
                    {
                        await _databaseService.RestoreDatabaseAsync(selectedDb.Name, backupFile);
                        var (statusText, toolTipText, detailedError) = ErrorFormattingHelper.FormatProcessError(ex, backupFile, null, true);
                        SetStatus(statusText, toolTipText);
                        ShowErrorDetailsDialog("Operation Failed", detailedError);
                    }
                    catch (Exception restoreEx)
                    {
                        _logger.LogError("Rollback/Restore failed.", restoreEx);
                        var (statusText, toolTipText, detailedError) = ErrorFormattingHelper.FormatProcessError(ex, backupFile, restoreEx, true);
                        SetStatus(statusText, toolTipText);
                        ShowErrorDetailsDialog("Critical Operation Failure", detailedError);
                    }
                }
                else
                {
                    var (statusText, toolTipText, detailedError) = ErrorFormattingHelper.FormatProcessError(ex, null);
                    SetStatus(statusText, toolTipText);
                    ShowErrorDetailsDialog("Operation Failed", detailedError);
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
