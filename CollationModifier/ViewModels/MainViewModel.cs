using CollationModifier.Collections;
using CollationModifier.Exceptions;
using CollationModifier.Models;
using CollationModifier.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
            OpenLogCommand = new RelayCommand(_ => OpenLogFile());

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
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsNotBusy));
                    OnPropertyChanged(nameof(CanEditConnection));
                    InvalidateRequerySuggested();
                }
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
            set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(CanEditConnection));
                }
            }
        }

        // Dashboard Properties
        public ObservableCollection<string> SupportedCollations { get; } = new ObservableCollection<string>();
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

        private static string FormatExceptionMessage(Exception ex)
        {
            if (ex is AggregateException aggEx)
            {
                var messages = aggEx.Flatten().InnerExceptions.Select(e => e.Message);
                return string.Join(" | ", messages);
            }
            return ex.Message;
        }

        private static bool IsSingleUserLockState(Exception ex)
        {
            if (ex is AggregateException aggEx)
            {
                foreach (var inner in aggEx.Flatten().InnerExceptions)
                {
                    if (IsSingleUserLockState(inner)) return true;
                }
            }
            if (ex == null || string.IsNullOrEmpty(ex.Message)) return false;
            var msg = ex.Message;
            return msg.IndexOf("SINGLE_USER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("reset failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("emergency MULTI_USER reset", StringComparison.OrdinalIgnoreCase) >= 0;
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
        public ICommand OpenLogCommand { get; }

        public void OpenLogFile()
        {
            try
            {
                string logPath = _logger.LogFilePath;
                if (File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = logPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    string? directory = Path.GetDirectoryName(logPath);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = directory,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        StatusMessage = "Log file does not exist yet.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to open log file.", ex);
                StatusMessage = $"Unable to open log file: {ex.Message}";
            }
        }

        public bool CanEditConnection => !IsBusy && !IsConnected;

        public async Task ConnectAsync()
        {
            if (IsConnected)
            {
                IsConnected = false;
                Databases.Clear();
                SupportedCollations.Clear();
                SelectedDatabase = null;
                StatusMessage = "Disconnected.";
                _logger.LogInfo("Disconnected from server.");
                return;
            }

            IsBusy = true;
            StatusMessage = "Connecting to server...";
            _logger.LogInfo($"Attempting to connect to server: {ServerName} using {SelectedAuthenticationType}");

            try
            {
                _databaseService.SetConnectionString(ServerName, SelectedAuthenticationType, Username, Password);
                var success = await _databaseService.TestConnectionAsync();

                if (success)
                {
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

                    bool dbLoaded = await LoadDatabasesAsync();
                    if (dbLoaded)
                    {
                        IsConnected = true;
                        await LoadCollationsAsync();
                        StatusMessage = "Ready";
                    }
                    else
                    {
                        IsConnected = false;
                    }
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

        public async Task<bool> LoadDatabasesAsync()
        {
            try
            {
                var dbs = await _databaseService.GetDatabasesAsync();
                Databases.ReplaceRange(dbs);
                _logger.LogInfo($"Fetched {Databases.Count} databases.");
                return true;
            }
            catch (Exception ex)
            {
                Databases.Clear();
                _logger.LogError("Failed to load databases.", ex);
                StatusMessage = $"Failed to load databases: {ex.Message}";
                return false;
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
                StatusMessage = "Running pre-flight checks...";
                var preflightReport = await _databaseService.PreflightCheckAsync(SelectedDatabase.Name, BackupDirectory);
                if (preflightReport.HasBlockingConditions)
                {
                    var summary = preflightReport.GetDiagnosticSummary();
                    _logger.LogWarning($"Pre-flight validation failed for {SelectedDatabase.Name}:\n{summary}");
                    throw new PreflightValidationException(preflightReport);
                }

                // 2. Automated Backup
                StatusMessage = "Starting automated backup...";
                backupFile = await _databaseService.BackupDatabaseAsync(SelectedDatabase.Name, BackupDirectory);

                // 3. Execution Phase
                StatusMessage = "Altering database collation... This may take a while.";
                await _databaseService.AlterCollationAsync(SelectedDatabase.Name, TargetCollation);

                StatusMessage = "Collation changed successfully!";
                _logger.LogInfo($"Collation change process completed successfully for {SelectedDatabase.Name}");

                // Update in-memory model directly without calling LoadDatabasesAsync()
                SelectedDatabase.Collation = TargetCollation;
            }
            catch (Exception ex)
            {
                _logger.LogError("Process collation change failed.", ex);

                string formattedError = FormatExceptionMessage(ex);
                bool isSingleUser = IsSingleUserLockState(ex);

                if (backupFile != null)
                {
                    StatusMessage = $"Process failed: {formattedError}. Attempting to restore from backup...";
                    try
                    {
                        await _databaseService.RestoreDatabaseAsync(SelectedDatabase.Name, backupFile);
                        if (ex is AggregateException)
                        {
                            StatusMessage = $"Process failed: {ex.Message}. WARNING: Database remains locked in SINGLE_USER mode.";
                        }
                        else
                        {
                            StatusMessage = $"Process failed: {ex.Message}. Database restored to MULTI_USER mode successfully.";
                        }
                    }
                    catch (Exception restoreEx)
                    {
                        _logger.LogError("Rollback/Restore failed.", restoreEx);
                        bool isSingleUserLocked = ex is AggregateException || restoreEx is AggregateException;
                        string lockWarning = isSingleUserLocked ? " WARNING: Database remains locked in SINGLE_USER mode." : "";
                        StatusMessage = $"Process failed: {ex.Message}. CRITICAL: Auto-restore also failed: {restoreEx.Message}.{lockWarning}";
                    }
                }
                else
                {
                    if (ex is AggregateException)
                    {
                        StatusMessage = $"Process failed: {ex.Message}. WARNING: Database remains locked in SINGLE_USER mode.";
                    }
                    else
                    {
                        StatusMessage = $"Process failed before backup: {ex.Message}";
                    }
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
