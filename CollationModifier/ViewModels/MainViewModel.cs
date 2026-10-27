using CollationModifier.Collections;
using CollationModifier.Exceptions;
using CollationModifier.Models;
using CollationModifier.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CollationModifier.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly IDatabaseService _databaseService;
        private readonly LoggerService _logger;
        private CancellationTokenSource? _cts;

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
            CancelCommand = new RelayCommand(_ => CancelOperation(), _ => IsBusy);

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
        public ICommand CancelCommand { get; }

        public void CancelOperation()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                StatusMessage = "Cancelling operation...";
                _logger.LogInfo("User requested cancellation of active operation.");
                _cts.Cancel();
            }
        }

        public async Task ConnectAsync()
        {
            IsBusy = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            StatusMessage = "Connecting to server...";
            _logger.LogInfo($"Attempting to connect to server: {ServerName} using {SelectedAuthenticationType}");

            try
            {
                _databaseService.SetConnectionString(ServerName, SelectedAuthenticationType, Username, Password);
                var success = await _databaseService.TestConnectionAsync(token);

                if (success)
                {
                    IsConnected = true;
                    StatusMessage = "Connected successfully. Querying default backup directory and fetching databases...";

                    string? serverBackupPath = await _databaseService.GetDefaultBackupDirectoryAsync(token);
                    if (!string.IsNullOrWhiteSpace(serverBackupPath))
                    {
                        BackupDirectory = serverBackupPath;
                        _logger.LogInfo($"Auto-populated backup directory from target SQL Server instance: {serverBackupPath}");
                    }
                    else
                    {
                        _logger.LogWarning("Could not retrieve default backup path from server. Preserving current backup directory entry.");
                    }

                    await LoadDatabasesAsync(token);
                    await LoadCollationsAsync(token);
                    StatusMessage = "Ready";
                }
                else
                {
                    IsConnected = false;
                    StatusMessage = "Connection failed. Check credentials and server name.";
                }
            }
            catch (OperationCanceledException)
            {
                IsConnected = false;
                _logger.LogInfo("Connect operation was cancelled by user.");
                StatusMessage = "Operation cancelled by user.";
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsBusy = false;
            }
        }

        public async Task LoadDatabasesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var dbs = await _databaseService.GetDatabasesAsync(cancellationToken);
                Databases.ReplaceRange(dbs);
                _logger.LogInfo($"Fetched {Databases.Count} databases.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Load databases operation was cancelled by user.");
                StatusMessage = "Operation cancelled by user.";
                throw;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load databases: {ex.Message}";
            }
        }

        private async Task LoadCollationsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var collations = await _databaseService.GetSupportedCollationsAsync(cancellationToken);
                SupportedCollations.Clear();
                foreach (var collation in collations)
                {
                    SupportedCollations.Add(collation);
                }
                _logger.LogInfo($"Fetched {SupportedCollations.Count} supported collations.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Load collations operation was cancelled by user.");
                throw;
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
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _logger.LogInfo($"Starting collation change process for database: {SelectedDatabase.Name} to {TargetCollation}");
            string? backupFile = null;

            try
            {
                token.ThrowIfCancellationRequested();

                // 1. Pre-flight (validates collation against server catalog & user permissions)
                StatusMessage = "Running pre-flight checks...";
                var preflightReport = await _databaseService.PreflightCheckAsync(SelectedDatabase.Name, BackupDirectory, token);
                if (preflightReport.HasBlockingConditions)
                {
                    var summary = preflightReport.GetDiagnosticSummary();
                    _logger.LogWarning($"Pre-flight validation failed for {SelectedDatabase.Name}:\n{summary}");
                    throw new PreflightValidationException(preflightReport);
                }

                token.ThrowIfCancellationRequested();

                // 2. Automated Backup
                StatusMessage = "Starting automated backup...";
                backupFile = await _databaseService.BackupDatabaseAsync(SelectedDatabase.Name, BackupDirectory, token);

                token.ThrowIfCancellationRequested();

                // 3. Execution Phase
                StatusMessage = "Altering database collation... This may take a while.";
                await _databaseService.AlterCollationAsync(SelectedDatabase.Name, TargetCollation, token);

                StatusMessage = "Collation changed successfully!";
                _logger.LogInfo($"Collation change process completed successfully for {SelectedDatabase.Name}");

                // Update in-memory model directly without calling LoadDatabasesAsync()
                SelectedDatabase.Collation = TargetCollation;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Process collation change was cancelled by user.");
                StatusMessage = "Operation cancelled by user.";
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
                _cts?.Dispose();
                _cts = null;
                IsBusy = false;
            }
        }

        #endregion

        public void Dispose()
        {
            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch { }
                _cts.Dispose();
                _cts = null;
            }
            _logger?.Dispose();
        }
    }
}
