using CollationModifier.Collections;
using CollationModifier.Exceptions;
using CollationModifier.Models;
using CollationModifier.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CollationModifier.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly IDatabaseService _databaseService;
        private readonly LoggerService _logger;

        public MainViewModel() : this(null, null) { }

        public MainViewModel(IDatabaseService? databaseService = null, LoggerService? logger = null)
        {
            _logger = logger ?? new LoggerService();
            _databaseService = databaseService ?? CreateDefaultDatabaseService(_logger);

            AuthenticationTypes = new ObservableCollection<string>
            {
                "Windows Authentication",
                "SQL Server Authentication"
            };
            SelectedAuthenticationType = AuthenticationTypes[0];

            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsBusy);
            ProcessCommand = new RelayCommand(async _ => await ProcessCollationChangeAsync(), _ => CanProcessCollationChange());

            Databases = new ObservableRangeCollection<DatabaseInfo>();
            SupportedCollations = new ObservableCollection<string>();
        }

        private static IDatabaseService CreateDefaultDatabaseService(LoggerService logger)
        {
            try
            {
                var type = Type.GetType("CollationModifier.Services.DatabaseService, CollationModifier");
                if (type != null)
                {
                    return (IDatabaseService)Activator.CreateInstance(type, logger)!;
                }
            }
            catch { }
            throw new InvalidOperationException("No IDatabaseService implementation was provided or found.");
        }

        #region Properties

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                SetProperty(ref _isBusy, value);
                OnPropertyChanged(nameof(IsNotBusy));
                InvalidateRequerySuggested();
            }
        }
        public bool IsNotBusy => !IsBusy;

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string? _statusToolTip;
        public string? StatusToolTip
        {
            get => _statusToolTip;
            set => SetProperty(ref _statusToolTip, value);
        }

        // Authentication Properties
        private string _serverName = "localhost";
        public string ServerName
        {
            get => _serverName;
            set => SetProperty(ref _serverName, value);
        }

        public ObservableCollection<string> AuthenticationTypes { get; }

        private string _selectedAuthenticationType = string.Empty;
        public string SelectedAuthenticationType
        {
            get => _selectedAuthenticationType;
            set
            {
                SetProperty(ref _selectedAuthenticationType, value);
                OnPropertyChanged(nameof(IsSqlServerAuth));
            }
        }

        public bool IsSqlServerAuth => SelectedAuthenticationType == "SQL Server Authentication";

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        // Dashboard Properties
        public ObservableRangeCollection<DatabaseInfo> Databases { get; }
        public ObservableCollection<string> SupportedCollations { get; }

        private DatabaseInfo? _selectedDatabase;
        public DatabaseInfo? SelectedDatabase
        {
            get => _selectedDatabase;
            set
            {
                SetProperty(ref _selectedDatabase, value);
                InvalidateRequerySuggested();
            }
        }

        private string _targetCollation = string.Empty;
        public string TargetCollation
        {
            get => _targetCollation;
            set
            {
                SetProperty(ref _targetCollation, value);
                InvalidateRequerySuggested();
            }
        }

        private string _backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SQLBackups");
        public string BackupDirectory
        {
            get => _backupDirectory;
            set => SetProperty(ref _backupDirectory, value);
        }

        #endregion

        #region Helpers

        public Action<string, string>? ShowErrorDialogAction { get; set; }

        private void SetStatus(string message, string? toolTip = null)
        {
            StatusMessage = message;
            StatusToolTip = toolTip;
        }

        private void ShowErrorDialog(string message, string title = "Collation Change Error")
        {
            if (string.IsNullOrEmpty(message)) return;

            if (ShowErrorDialogAction != null)
            {
                ShowErrorDialogAction(message, title);
            }
            else
            {
                try
                {
                    var msgBoxType = Type.GetType("System.Windows.MessageBox, PresentationFramework");
                    if (msgBoxType != null)
                    {
                        var okVal = Enum.Parse(Type.GetType("System.Windows.MessageBoxButton, PresentationFramework") ?? typeof(int), "OK");
                        var errorVal = Enum.Parse(Type.GetType("System.Windows.MessageBoxImage, PresentationFramework") ?? typeof(int), "Error");
                        var showMethod = msgBoxType.GetMethod("Show", new[] { typeof(string), typeof(string), okVal.GetType(), errorVal.GetType() });
                        showMethod?.Invoke(null, new object[] { message, title, okVal, errorVal });
                    }
                }
                catch
                {
                    // Fallback when running without an active WPF Window / Desktop session
                }
            }
        }

        private void InvalidateRequerySuggested()
        {
            try
            {
                CommandManager.InvalidateRequerySuggested();
            }
            catch
            {
                // Safe fallthrough when running in test environment outside WPF runtime
            }
        }

        #endregion

        #region Commands

        public ICommand ConnectCommand { get; }
        public ICommand ProcessCommand { get; }

        public async Task ConnectAsync()
        {
            IsBusy = true;
            SetStatus("Connecting to server...");
            _logger.LogInfo($"Attempting to connect to server: {ServerName} using {SelectedAuthenticationType}");

            try
            {
                _databaseService.SetConnectionString(ServerName, SelectedAuthenticationType, Username, Password);
                var success = await _databaseService.TestConnectionAsync();

                if (success)
                {
                    IsConnected = true;
                    SetStatus("Connected successfully. Querying default backup directory and fetching databases...");

                    string? serverBackupPath = await _databaseService.GetDefaultBackupDirectoryAsync();
                    if (!string.IsNullOrWhiteSpace(serverBackupPath))
                    {
                        BackupDirectory = serverBackupPath;
                        _logger.LogInfo($"Auto-populated backup directory from target SQL Server instance: {serverBackupPath}");
                    }
                    else
                    {
                        _logger.LogWarning("Could not retrieve default backup path from server. Preserving current backup directory entry.");
                    }

                    await LoadDatabasesAsync();
                    await LoadCollationsAsync();
                    SetStatus("Ready");
                }
                else
                {
                    IsConnected = false;
                    SetStatus("Connection failed. Check credentials and server name.");
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                var (statusText, toolTipText) = ErrorFormattingHelper.FormatConnectionError(ex);
                SetStatus(statusText, toolTipText);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task LoadDatabasesAsync()
        {
            try
            {
                var dbs = await _databaseService.GetDatabasesAsync();
                Databases.ReplaceRange(dbs);
                _logger.LogInfo($"Fetched {Databases.Count} databases.");
            }
            catch (Exception ex)
            {
                var (statusText, toolTipText) = ErrorFormattingHelper.FormatLoadDatabasesError(ex);
                SetStatus(statusText, toolTipText);
            }
        }

        private async Task LoadCollationsAsync()
        {
            try
            {
                var collations = await _databaseService.GetSupportedCollationsAsync();
                SupportedCollations.Clear();
                foreach (var collation in collations)
                {
                    SupportedCollations.Add(collation);
                }
                _logger.LogInfo($"Fetched {SupportedCollations.Count} supported collations.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to load supported collations.", ex);
            }
        }

        private bool CanProcessCollationChange()
        {
            return IsConnected && !IsBusy && SelectedDatabase != null && !string.IsNullOrWhiteSpace(TargetCollation);
        }

        public async Task ProcessCollationChangeAsync()
        {
            if (SelectedDatabase == null) return;

            IsBusy = true;
            _logger.LogInfo($"Starting collation change process for database: {SelectedDatabase.Name} to {TargetCollation}");
            string? backupFile = null;

            try
            {
                // 1. Pre-flight (validates collation against server catalog & user permissions)
                SetStatus("Running pre-flight checks...");
                var preflightReport = await _databaseService.PreflightCheckAsync(SelectedDatabase.Name, BackupDirectory);
                if (preflightReport.HasBlockingConditions)
                {
                    var summary = preflightReport.GetDiagnosticSummary();
                    _logger.LogWarning($"Pre-flight validation failed for {SelectedDatabase.Name}:\n{summary}");
                    throw new PreflightValidationException(preflightReport);
                }

                // 2. Automated Backup
                SetStatus("Starting automated backup...");
                backupFile = await _databaseService.BackupDatabaseAsync(SelectedDatabase.Name, BackupDirectory);

                // 3. Execution Phase
                SetStatus("Altering database collation... This may take a while.");
                await _databaseService.AlterCollationAsync(SelectedDatabase.Name, TargetCollation);

                SetStatus("Collation changed successfully!");
                _logger.LogInfo($"Collation change process completed successfully for {SelectedDatabase.Name}");

                // Update in-memory model directly without calling LoadDatabasesAsync()
                SelectedDatabase.Collation = TargetCollation;
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
                        await _databaseService.RestoreDatabaseAsync(SelectedDatabase.Name, backupFile);
                        var (restoredStatus, restoredToolTip, dialogMsg) = ErrorFormattingHelper.FormatProcessError(ex, backupFile, restoreAttempted: true);
                        SetStatus(restoredStatus, restoredToolTip);
                        ShowErrorDialog(dialogMsg);
                    }
                    catch (Exception restoreEx)
                    {
                        _logger.LogError("Rollback/Restore failed.", restoreEx);
                        var (failStatus, failToolTip, dialogMsg) = ErrorFormattingHelper.FormatProcessError(ex, backupFile, restoreEx: restoreEx, restoreAttempted: true);
                        SetStatus(failStatus, failToolTip);
                        ShowErrorDialog(dialogMsg);
                    }
                }
                else
                {
                    var (noBackupStatus, noBackupToolTip, dialogMsg) = ErrorFormattingHelper.FormatProcessError(ex, backupFile: null);
                    SetStatus(noBackupStatus, noBackupToolTip);
                    ShowErrorDialog(dialogMsg);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        public void Dispose()
        {
            _logger?.Dispose();
        }
    }
}
