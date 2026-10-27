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
        private readonly DatabaseService _databaseService;
        private readonly LoggerService _logger;

        public MainViewModel()
        {
            _logger = new LoggerService();
            _databaseService = new DatabaseService(_logger);

            AuthenticationTypes = new ObservableCollection<string>
            {
                "Windows Authentication",
                "SQL Server Authentication"
            };
            SelectedAuthenticationType = AuthenticationTypes[0];

            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsBusy);
            ProcessCommand = new RelayCommand(async _ => await ProcessCollationChangeAsync(), _ => CanProcessCollationChange());

            Databases = new ObservableCollection<DatabaseInfo>();
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
                CommandManager.InvalidateRequerySuggested();
            }
        }
        public bool IsNotBusy => !IsBusy;

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
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

        // Password handling in MVVM can be tricky, keeping it simple for string here,
        // ideally SecureString would be used in a real enterprise app.
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
        public ObservableCollection<DatabaseInfo> Databases { get; }

        private DatabaseInfo? _selectedDatabase;
        public DatabaseInfo? SelectedDatabase
        {
            get => _selectedDatabase;
            set
            {
                SetProperty(ref _selectedDatabase, value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _targetCollation = string.Empty;
        public string TargetCollation
        {
            get => _targetCollation;
            set
            {
                SetProperty(ref _targetCollation, value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SQLBackups");
        public string BackupDirectory
        {
            get => _backupDirectory;
            set => SetProperty(ref _backupDirectory, value);
        }

        #endregion

        #region Commands

        public ICommand ConnectCommand { get; }
        public ICommand ProcessCommand { get; }

        private async Task ConnectAsync()
        {
            IsBusy = true;
            StatusMessage = "Connecting to server...";
            _logger.LogInfo($"Attempting to connect to server: {ServerName} using {SelectedAuthenticationType}");

            try
            {
                _databaseService.SetConnectionString(ServerName, SelectedAuthenticationType, Username, Password);
                var success = await _databaseService.TestConnectionAsync();

                if (success)
                {
                    IsConnected = true;
                    StatusMessage = "Connected successfully. Querying default backup directory and fetching databases...";

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
                    StatusMessage = "Ready";
                }
                else
                {
                    IsConnected = false;
                    StatusMessage = "Connection failed. Check credentials and server name.";
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadDatabasesAsync()
        {
            try
            {
                var dbs = await _databaseService.GetDatabasesAsync();
                Databases.Clear();
                foreach (var db in dbs)
                {
                    Databases.Add(db);
                }
                _logger.LogInfo($"Fetched {Databases.Count} databases.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load databases: {ex.Message}";
            }
        }

        private bool CanProcessCollationChange()
        {
            return IsConnected && !IsBusy && SelectedDatabase != null && !string.IsNullOrWhiteSpace(TargetCollation);
        }

        private async Task ProcessCollationChangeAsync()
        {
            if (SelectedDatabase == null) return;

            IsBusy = true;
            _logger.LogInfo($"Starting collation change process for database: {SelectedDatabase.Name} to {TargetCollation}");
            string? backupFile = null;

            try
            {
                // 1. Pre-flight
                StatusMessage = "Running pre-flight checks...";
                await _databaseService.PreflightCheckAsync(SelectedDatabase.Name, BackupDirectory);

                // 2. Automated Backup
                StatusMessage = "Starting automated backup...";
                backupFile = await _databaseService.BackupDatabaseAsync(SelectedDatabase.Name, BackupDirectory);

                // 3. Execution Phase
                StatusMessage = "Altering database collation... This may take a while.";
                await _databaseService.AlterCollationAsync(SelectedDatabase.Name, TargetCollation);

                StatusMessage = "Collation changed successfully!";
                _logger.LogInfo($"Collation change process completed successfully for {SelectedDatabase.Name}");

                // Refresh data
                await LoadDatabasesAsync();
                SelectedDatabase = Databases.FirstOrDefault(d => d.Name == SelectedDatabase?.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError("Process collation change failed.", ex);

                if (backupFile != null)
                {
                    StatusMessage = $"Process failed: {ex.Message}. Attempting to restore from backup...";
                    try
                    {
                        await _databaseService.RestoreDatabaseAsync(SelectedDatabase.Name, backupFile);
                        StatusMessage = $"Process failed: {ex.Message}. Database restored successfully.";
                    }
                    catch (Exception restoreEx)
                    {
                        _logger.LogError("Rollback/Restore failed.", restoreEx);
                        StatusMessage = $"Process failed: {ex.Message}. CRITICAL: Auto-restore also failed: {restoreEx.Message}";
                    }
                }
                else
                {
                    StatusMessage = $"Process failed before backup: {ex.Message}";
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
