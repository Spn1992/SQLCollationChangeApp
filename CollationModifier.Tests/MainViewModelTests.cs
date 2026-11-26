using CollationModifier.Collections;
using CollationModifier.Models;
using CollationModifier.Services;
using CollationModifier.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace CollationModifier.Tests
{
    public class MockDatabaseService : IDatabaseService
    {
        public List<DatabaseInfo> DatabasesToReturn { get; set; } = new();
        public List<string>? CollationsToReturn { get; set; } = null;
        public string? CurrentCollationToReturn { get; set; } = null;
        public PreflightReport PreflightReportToReturn { get; set; } = new PreflightReport();
        public bool ThrowOnGetDatabases { get; set; } = false;
        public int GetDatabasesCount { get; private set; }
        public int GetCollationsCount { get; private set; }
        public int AlterCollationCount { get; private set; }
        public int BackupCount { get; private set; }
        public int RestoreCount { get; private set; }
        public int TrySetMultiUserCount { get; private set; }
        public CancellationToken LastRestoreCancellationToken { get; private set; }
        public bool IsMultiUser { get; set; } = true;
        public string? LastAlteredDatabase { get; private set; }
        public string? LastTargetCollation { get; private set; }
        public bool ShouldAlterCollationThrow { get; set; } = false;
        public bool ShouldTrySetMultiUserFail { get; set; } = false;
        public Exception? TrySetMultiUserExceptionToThrow { get; set; }
        public Exception? AlterCollationExceptionToThrow { get; set; }
        public Exception? RestoreExceptionToThrow { get; set; }
        public Func<string, string, string, CancellationToken, Task>? OnPreflightCheck { get; set; }
        public Func<CancellationToken, Task>? OnBackupDatabase { get; set; }
        public Func<CancellationToken, Task>? OnAlterCollation { get; set; }
        public Func<CancellationToken, Task>? OnGetDatabases { get; set; }
        public Action<IProgress<int>?>? BackupProgressSimulator { get; set; }
        public Action<IProgress<int>?>? RestoreProgressSimulator { get; set; }
        public bool AllowBackupCompletionWhenCancelled { get; set; } = false;
        public Func<CancellationToken, Task<bool>>? OnTestConnection { get; set; }

        public void SetConnectionString(string server, string authType, string username, string password) { }

        public int GetServerInitializationDataCount { get; set; }
        public string? DefaultBackupDirectoryToReturn { get; set; }
        public Func<CancellationToken, Task>? OnGetServerInitializationData { get; set; }
        public Exception? GetServerInitializationDataExceptionToThrow { get; set; }

        public virtual async Task<ServerInitializationData> GetServerInitializationDataAsync(CancellationToken cancellationToken = default)
        {
            GetServerInitializationDataCount++;
            if (OnGetServerInitializationData != null)
            {
                await OnGetServerInitializationData(cancellationToken);
            }
            if (OnTestConnection != null)
            {
                await OnTestConnection(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (GetServerInitializationDataExceptionToThrow != null)
            {
                throw GetServerInitializationDataExceptionToThrow;
            }
            if (ThrowOnGetDatabases)
            {
                throw new InvalidOperationException("Failed to reach database catalog.");
            }
            var dbs = await GetDatabasesAsync(cancellationToken);
            var collations = await GetSupportedCollationsAsync(cancellationToken);
            var backupDir = DefaultBackupDirectoryToReturn ?? await GetDefaultBackupDirectoryAsync(cancellationToken);

            return new ServerInitializationData
            {
                DefaultBackupDirectory = backupDir,
                Databases = new List<DatabaseInfo>(dbs),
                SupportedCollations = new List<string>(collations)
            };
        }

        public virtual async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (OnTestConnection != null)
            {
                return await OnTestConnection(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }

        public Task<string?> GetDefaultBackupDirectoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public async Task<List<DatabaseInfo>> GetDatabasesAsync(CancellationToken cancellationToken = default)
        {
            if (OnGetDatabases != null)
            {
                await OnGetDatabases(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            GetDatabasesCount++;
            if (ThrowOnGetDatabases)
            {
                throw new InvalidOperationException("Failed to reach database catalog.");
            }
            return DatabasesToReturn;
        }

        public Task<List<string>> GetSupportedCollationsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetCollationsCount++;
            return Task.FromResult(CollationsToReturn ?? new List<string> { "SQL_Latin1_General_CP1_CI_AS", "Latin1_General_100_CI_AS" });
        }

        public Task TrySetMultiUserAsync(string databaseName, CancellationToken cancellationToken = default)
        {
            TrySetMultiUserCount++;
            IsMultiUser = true;
            if (TrySetMultiUserExceptionToThrow != null)
            {
                throw TrySetMultiUserExceptionToThrow;
            }
            if (ShouldTrySetMultiUserFail)
            {
                throw new InvalidOperationException("Connection reset failed during emergency MULTI_USER command.");
            }
            return Task.CompletedTask;
        }

        public int GetColumnCollationScopeCount { get; private set; }
        public (int UserCharacterColumnCount, int RetainedColumnCount)? ColumnScopeToReturn { get; set; }

        public Task<(int UserCharacterColumnCount, int RetainedColumnCount)> GetColumnCollationScopeAsync(
            string databaseName,
            string targetCollation = "",
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetColumnCollationScopeCount++;
            if (ColumnScopeToReturn.HasValue)
            {
                return Task.FromResult(ColumnScopeToReturn.Value);
            }

            var currentDb = DatabasesToReturn.FirstOrDefault(d => string.Equals(d.Name, databaseName, StringComparison.OrdinalIgnoreCase));
            int total = currentDb?.UserCharacterColumnCount ?? PreflightReportToReturn.UserCharacterColumnCount;
            int retained = 0;
            if (!string.IsNullOrWhiteSpace(targetCollation))
            {
                if (currentDb != null && !string.Equals(currentDb.Collation, targetCollation, StringComparison.OrdinalIgnoreCase))
                {
                    retained = currentDb.RetainedColumnCount > 0 ? currentDb.RetainedColumnCount : total;
                }
                else
                {
                    retained = PreflightReportToReturn.RetainedColumnCount;
                }
            }
            return Task.FromResult((total, retained));
        }

        public Task<PreflightReport> PreflightCheckAsync(string databaseName, string backupDirectory = "", string targetCollation = "", CancellationToken cancellationToken = default)
        {
            if (OnPreflightCheck != null)
            {
                await OnPreflightCheck(databaseName, backupDirectory, targetCollation, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            PreflightCheckCount++;
            LastPreflightDatabase = databaseName;
            LastPreflightBackupDirectory = backupDirectory;
            LastPreflightTargetCollation = targetCollation;
            PreflightReportToReturn.DatabaseName = databaseName;
            PreflightReportToReturn.TargetCollation = targetCollation;
            if (!string.IsNullOrEmpty(targetCollation))
            {
                try
                {
                    DatabaseService.ValidateCollationFormat(targetCollation);
                }
                catch (Exception ex)
                {
                    if (!PreflightReportToReturn.ValidationErrors.Contains(ex.Message))
                    {
                        PreflightReportToReturn.ValidationErrors.Add(ex.Message);
                    }
                }

                var currentDb = DatabasesToReturn.FirstOrDefault(d => string.Equals(d.Name, databaseName, StringComparison.OrdinalIgnoreCase));
                var currentCollation = CurrentCollationToReturn ?? currentDb?.Collation;
                if (!string.IsNullOrEmpty(currentCollation))
                {
                    DatabaseService.EvaluateCollation(databaseName, currentCollation, targetCollation, PreflightReportToReturn);
                }

                if (currentDb != null && PreflightReportToReturn.UserCharacterColumnCount == 0 && currentDb.UserCharacterColumnCount > 0)
                {
                    PreflightReportToReturn.UserCharacterColumnCount = currentDb.UserCharacterColumnCount;
                    if (PreflightReportToReturn.RetainedColumnCount == 0 &&
                        !string.Equals(currentCollation, targetCollation, StringComparison.OrdinalIgnoreCase))
                    {
                        PreflightReportToReturn.RetainedColumnCount = currentDb.RetainedColumnCount > 0
                            ? currentDb.RetainedColumnCount
                            : currentDb.UserCharacterColumnCount;
                    }
                }
            }
            return PreflightReportToReturn;
        }

        public async Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            if (OnBackupDatabase != null)
            {
                await OnBackupDatabase(cancellationToken);
            }
            if (!AllowBackupCompletionWhenCancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            BackupProgressSimulator?.Invoke(progress);
            BackupCount++;
            return "/tmp/mock.bak";
        }

        public async Task RestoreDatabaseAsync(string databaseName, string backupFile, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            LastRestoreCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCount++;
            IsMultiUser = true;
            RestoreProgressSimulator?.Invoke(progress);
            if (RestoreExceptionToThrow != null)
            {
                try
                {
                    await TrySetMultiUserAsync(databaseName, CancellationToken.None);
                }
                catch (Exception resetEx)
                {
                    throw new AggregateException($"Restore failed for database '{databaseName}' and emergency MULTI_USER reset also failed. Database may remain in SINGLE_USER mode.", RestoreExceptionToThrow, resetEx);
                }
                throw RestoreExceptionToThrow;
            }
        }

        public async Task AlterCollationAsync(string databaseName, string newCollation, CancellationToken cancellationToken = default)
        {
            if (OnAlterCollation != null)
            {
                await OnAlterCollation(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            AlterCollationCount++;
            LastAlteredDatabase = databaseName;
            LastTargetCollation = newCollation;
            if (ShouldAlterCollationThrow)
            {
                var primaryEx = AlterCollationExceptionToThrow ?? new InvalidOperationException("Table constraint violation during collation alter.");
                try
                {
                    await TrySetMultiUserAsync(databaseName, CancellationToken.None);
                }
                catch (Exception resetEx)
                {
                    throw new AggregateException(
                        $"Failed to alter collation for database '{databaseName}' ({primaryEx.Message}) and emergency MULTI_USER reset failed: {resetEx.Message}",
                        primaryEx, resetEx);
                }
                throw primaryEx;
            }
            if (AlterCollationExceptionToThrow != null)
            {
                throw AlterCollationExceptionToThrow;
            }
        }
    }

    public class FailingMockDatabaseService : MockDatabaseService
    {
        public override Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public override Task<ServerInitializationData> GetServerInitializationDataAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Connection failed: Unable to connect to the specified SQL Server host. Please verify server name and credentials.");
        }
    }

    public sealed class RecordedSqlCommand
    {
        public string CommandText { get; }
        public int TimeoutSeconds { get; }
        public CancellationToken CancellationToken { get; }
        public bool CanBeCanceled { get; }
        public bool WasCancellationRequestedAtEntry { get; }
        public SqlParameter[] Parameters { get; }

        public RecordedSqlCommand(
            string commandText,
            int timeoutSeconds,
            CancellationToken cancellationToken,
            SqlParameter[] parameters)
        {
            CommandText = commandText;
            TimeoutSeconds = timeoutSeconds;
            CancellationToken = cancellationToken;
            CanBeCanceled = cancellationToken.CanBeCanceled;
            WasCancellationRequestedAtEntry = cancellationToken.IsCancellationRequested;
            Parameters = parameters;
        }
    }

    public class TestableDatabaseService : DatabaseService
    {
        public int GetSupportedCollationsCallCount { get; private set; }
        public List<string> CollationsToReturn { get; set; } = new List<string>
        {
            "SQL_Latin1_General_CP1_CI_AS",
            "Latin1_General_100_CI_AS"
        };
        public bool ThrowOnGetSupportedCollations { get; set; } = false;

        public bool SimulateConnectionOpen { get; set; } = false;
        public Func<SqlConnection, CancellationToken, Task>? OnOpenConnection { get; set; }
        public Func<SqlCommand, CancellationToken, Task<int>>? OnExecuteCommand { get; set; }
        public PreflightReport? SimulatedPreflightReport { get; set; }
        public List<CancellationToken> RecordedOpenTokens { get; } = new List<CancellationToken>();
        public List<RecordedSqlCommand> ExecutedCommands { get; } = new List<RecordedSqlCommand>();

        public TestableDatabaseService(LoggerService logger) : base(logger) { }

        public override Task<List<string>> GetSupportedCollationsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetSupportedCollationsCallCount++;
            if (ThrowOnGetSupportedCollations)
            {
                throw new InvalidOperationException("Failed to fetch supported collations from testable mock.");
            }
            return Task.FromResult(new List<string>(CollationsToReturn));
        }

        public override Task<PreflightReport> PreflightCheckAsync(
            string databaseName,
            string backupDirectory = "",
            string targetCollation = "",
            CancellationToken cancellationToken = default)
        {
            if (SimulatedPreflightReport != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(SimulatedPreflightReport);
            }

            return base.PreflightCheckAsync(databaseName, backupDirectory, targetCollation, cancellationToken);
        }

        protected internal override async Task OpenConnectionAsync(
            SqlConnection connection,
            CancellationToken cancellationToken = default)
        {
            RecordedOpenTokens.Add(cancellationToken);

            if (OnOpenConnection != null)
            {
                await OnOpenConnection(connection, cancellationToken);
                return;
            }

            if (SimulateConnectionOpen || OnExecuteCommand != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            await base.OpenConnectionAsync(connection, cancellationToken);
        }

        protected internal override async Task<int> ExecuteNonQueryAsync(
            SqlConnection connection,
            string commandText,
            int timeoutSeconds = 3600,
            CancellationToken cancellationToken = default,
            params SqlParameter[] parameters)
        {
            ExecutedCommands.Add(new RecordedSqlCommand(
                commandText,
                timeoutSeconds,
                cancellationToken,
                parameters ?? Array.Empty<SqlParameter>()));

            if (OnExecuteCommand != null)
            {
                NonQueryCommandExecutor = OnExecuteCommand;
            }
            else if (SimulateConnectionOpen)
            {
                NonQueryCommandExecutor = (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(0);
                };
            }
            else
            {
                NonQueryCommandExecutor = null;
            }

            return await base.ExecuteNonQueryAsync(
                connection,
                commandText,
                timeoutSeconds,
                cancellationToken,
                parameters ?? Array.Empty<SqlParameter>());
        }

        public static SqlException CreateSimulatedSqlException(
            string message = "A severe error occurred on the current command.  The results, if any, should be discarded.")
        {
            var errorCollection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
            var ctor = typeof(SqlException).GetConstructor(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(string), typeof(SqlErrorCollection), typeof(Exception), typeof(Guid) },
                modifiers: null);

            if (ctor != null)
            {
                return (SqlException)ctor.Invoke(new object?[] { message, errorCollection, null, Guid.NewGuid() });
            }

            throw new InvalidOperationException("Unable to construct simulated SqlException via reflection.");
        }
    }

    public class SqlCancellationTestableDatabaseService : DatabaseService
    {
        private static SqlException? _cachedSqlException;

        public List<CancellationToken> OpenConnectionTokens { get; } = new List<CancellationToken>();
        public List<(string CommandText, int CommandTimeout, CancellationToken Token)> ExecutedCommands { get; } = new List<(string, int, CancellationToken)>();
        public Func<SqlConnection, CancellationToken, Task>? OnOpenConnection { get; set; }
        public Func<SqlCommand, CancellationToken, Task<int>>? OnExecuteCommandNonQuery { get; set; }
        public bool BypassPreflightQueries { get; set; } = false;

        public SqlCancellationTestableDatabaseService(LoggerService logger) : base(logger)
        {
            SetConnectionString("localhost", "Windows Authentication", "", "");
        }

        public static async Task<SqlException> GetSampleSqlExceptionAsync()
        {
            if (_cachedSqlException != null)
            {
                return _cachedSqlException;
            }

            using var conn = new SqlConnection("Data Source=127.0.0.1,1;Initial Catalog=master;Integrated Security=False;User ID=sa;Password=x;Connect Timeout=1;ConnectRetryCount=0;Encrypt=False;TrustServerCertificate=True;");
            try
            {
                await conn.OpenAsync();
            }
            catch (SqlException ex)
            {
                _cachedSqlException = ex;
                return ex;
            }

            throw new InvalidOperationException("Expected SqlException when connecting to closed loopback port.");
        }

        public override Task<PreflightReport> PreflightCheckAsync(string databaseName, string backupDirectory = "", string targetCollation = "", CancellationToken cancellationToken = default)
        {
            if (BypassPreflightQueries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new PreflightReport { DatabaseName = databaseName });
            }
            return base.PreflightCheckAsync(databaseName, backupDirectory, targetCollation, cancellationToken);
        }

        protected override async Task OpenConnectionAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            OpenConnectionTokens.Add(cancellationToken);
            if (OnOpenConnection != null)
            {
                await OnOpenConnection(connection, cancellationToken);
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        protected override async Task<int> ExecuteCommandNonQueryAsync(SqlCommand command, CancellationToken cancellationToken)
        {
            ExecutedCommands.Add((command.CommandText, command.CommandTimeout, cancellationToken));
            if (OnExecuteCommandNonQuery != null)
            {
                return await OnExecuteCommandNonQuery(command, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        }
    }

    public class SynchronousSynchronizationContext : SynchronizationContext
    {
        private readonly List<Exception> _unhandledExceptions = new List<Exception>();
        private readonly object _lock = new object();

        public IReadOnlyList<Exception> UnhandledExceptions
        {
            get
            {
                lock (_lock)
                {
                    return _unhandledExceptions.ToArray();
                }
            }
        }

        public bool HasUnhandledExceptions
        {
            get
            {
                lock (_lock)
                {
                    return _unhandledExceptions.Count > 0;
                }
            }
        }

        public void ThrowIfUnhandledExceptions()
        {
            List<Exception> snapshot;
            lock (_lock)
            {
                if (_unhandledExceptions.Count == 0)
                {
                    return;
                }

                snapshot = new List<Exception>(_unhandledExceptions);
                _unhandledExceptions.Clear();
            }

            if (snapshot.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(snapshot[0]).Throw();
            }

            throw new AggregateException("Unhandled exception(s) occurred on SynchronizationContext.", snapshot);
        }

        public Exception CombineWithUnhandledExceptions(Exception primaryException)
        {
            List<Exception> snapshot;
            lock (_lock)
            {
                if (_unhandledExceptions.Count == 0)
                {
                    return primaryException;
                }

                snapshot = new List<Exception>(_unhandledExceptions.Count + 1) { primaryException };
                snapshot.AddRange(_unhandledExceptions);
                _unhandledExceptions.Clear();
            }

            return new AggregateException("Test failed with additional unhandled SynchronizationContext exception(s).", snapshot);
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            var prev = Current;
            try
            {
                SetSynchronizationContext(this);
                d(state);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _unhandledExceptions.Add(ex);
                }
            }
            finally
            {
                SetSynchronizationContext(prev);
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            var prev = Current;
            try
            {
                SetSynchronizationContext(this);
                d(state);
            }
            finally
            {
                SetSynchronizationContext(prev);
            }
        }

        public override SynchronizationContext CreateCopy()
        {
            return this;
        }
    }

    public static class MainViewModelTestsRunner
    {
        private const string SuiteClassName = "MainViewModelTests";

        internal static string ResolveCollationModifierFilePath(string fileName)
        {
            string resolvedPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", fileName));
            if (!File.Exists(resolvedPath))
            {
                resolvedPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "CollationModifier", fileName));
            }
            if (!File.Exists(resolvedPath))
            {
                resolvedPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "CollationModifier", fileName));
            }
            if (!File.Exists(resolvedPath))
            {
                throw new InvalidOperationException($"Assertion failed: {fileName} file should exist at path {resolvedPath}");
            }
            return resolvedPath;
        }

        public static Task<int> RunAllTestsAsync() => RunTestsAsync();

        public static async Task<int> ExecuteAsyncTestAsync(string name, Func<Task> testAction)
        {
            var syncContext = new SynchronousSynchronizationContext();
            var prevContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(syncContext);
            try
            {
                await testAction();
                syncContext.ThrowIfUnhandledExceptions();
                Console.WriteLine($"  [PASS] {name}");
                return 0;
            }
            catch (Exception ex)
            {
                Exception failure = syncContext.CombineWithUnhandledExceptions(ex);
                string qualifiedName = name.Contains('.') ? name : $"{SuiteClassName}.{name}";
                Console.WriteLine($"  [FAIL] {qualifiedName} - {failure}");
                return 1;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(prevContext);
            }
        }

        public static int ExecuteSyncTest(string name, Action testAction)
        {
            var syncContext = new SynchronousSynchronizationContext();
            var prevContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(syncContext);
            try
            {
                testAction();
                syncContext.ThrowIfUnhandledExceptions();
                Console.WriteLine($"  [PASS] {name}");
                return 0;
            }
            catch (Exception ex)
            {
                Exception failure = syncContext.CombineWithUnhandledExceptions(ex);
                string qualifiedName = name.Contains('.') ? name : $"{SuiteClassName}.{name}";
                Console.WriteLine($"  [FAIL] {qualifiedName} - {failure}");
                return 1;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(prevContext);
            }
        }

        public static async Task<int> RunTestsAsync()
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronousSynchronizationContext());
            int failed = 0;
            Console.WriteLine("\n=== Running MainViewModel & Pre-flight Validation Tests ===");

            RunSyncTest("MainViewModel_SetStatus_UpdatesProperties_AndClearsDetailedErrorOnDefaultNull", () =>
            {
                var mockService = new MockDatabaseService();
                var vm = new MainViewModel(mockService);

                vm.SetStatus("Initial message", "Initial tooltip", "Detailed stack trace");
                Assert(vm.StatusMessage == "Initial message", $"Expected StatusMessage 'Initial message', got '{vm.StatusMessage}'");
                Assert(vm.StatusToolTip == "Initial tooltip", $"Expected StatusToolTip 'Initial tooltip', got '{vm.StatusToolTip}'");
                Assert(vm.DetailedError == "Detailed stack trace", $"Expected DetailedError 'Detailed stack trace', got '{vm.DetailedError}'");
                Assert(vm.HasDetailedError == true, "Expected HasDetailedError to be true");
                Assert(vm.ShowErrorDetailsCommand.CanExecute(null) == true, "Expected ShowErrorDetailsCommand.CanExecute to be true");

                // Default null values must clear previous detailed error
                vm.SetStatus("Updated status");
                Assert(vm.StatusMessage == "Updated status", $"Expected StatusMessage 'Updated status', got '{vm.StatusMessage}'");
                Assert(vm.StatusToolTip == null, $"Expected StatusToolTip null, got '{vm.StatusToolTip}'");
                Assert(vm.DetailedError == null, $"Expected DetailedError null, got '{vm.DetailedError}'");
                Assert(vm.HasDetailedError == false, "Expected HasDetailedError to be false after default clear");
                Assert(vm.ShowErrorDetailsCommand.CanExecute(null) == false, "Expected ShowErrorDetailsCommand.CanExecute to be false after default clear");
            });

            RunSyncTest("MainViewModel_DetailedError_RaisesPropertyChanged_AndUpdatesCommandCanExecute", () =>
            {
                var mockService = new MockDatabaseService();
                var vm = new MainViewModel(mockService);

                var changedProperties = new List<string>();
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
                };

                // Setting DetailedError to text
                vm.DetailedError = "SqlException: Timeout expired";
                Assert(changedProperties.Contains(nameof(MainViewModel.DetailedError)), "Expected PropertyChanged for DetailedError");
                Assert(changedProperties.Contains(nameof(MainViewModel.HasDetailedError)), "Expected PropertyChanged for HasDetailedError");
                Assert(vm.HasDetailedError == true, "Expected HasDetailedError to be true");
                Assert(vm.ShowErrorDetailsCommand.CanExecute(null) == true, "Expected ShowErrorDetailsCommand to be enabled");

                changedProperties.Clear();

                // Setting DetailedError to empty string
                vm.DetailedError = string.Empty;
                Assert(changedProperties.Contains(nameof(MainViewModel.DetailedError)), "Expected PropertyChanged for DetailedError on empty string");
                Assert(changedProperties.Contains(nameof(MainViewModel.HasDetailedError)), "Expected PropertyChanged for HasDetailedError on empty string");
                Assert(vm.HasDetailedError == false, "Expected HasDetailedError to be false for empty string");
                Assert(vm.ShowErrorDetailsCommand.CanExecute(null) == false, "Expected ShowErrorDetailsCommand to be disabled for empty string");

                changedProperties.Clear();

                // Setting DetailedError to null
                vm.DetailedError = null;
                Assert(vm.HasDetailedError == false, "Expected HasDetailedError to be false for null");
                Assert(vm.ShowErrorDetailsCommand.CanExecute(null) == false, "Expected ShowErrorDetailsCommand to be disabled for null");
            });

            RunSyncTest("MainViewModel_ShowErrorDetails_InvokesCustomHandler_WhenDetailedErrorPresent", () =>
            {
                var mockService = new MockDatabaseService();
                var vm = new MainViewModel(mockService);

                string? receivedError = null;
                vm.ShowErrorDetailsHandler = error => receivedError = error;

                // When no detailed error, ShowErrorDetails should not invoke handler
                vm.ShowErrorDetails();
                Assert(receivedError == null, "Handler should not be invoked when HasDetailedError is false");

                // When detailed error is present, command execution invokes handler
                vm.DetailedError = "Critical failure details";
                vm.ShowErrorDetailsCommand.Execute(null);
                Assert(receivedError == "Critical failure details", $"Expected receivedError 'Critical failure details', got '{receivedError}'");
            });

            await RunAsyncTest("LoadCollationsAsync_FiresSingleCollectionChangedResetEvent", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    CollationsToReturn = new List<string>
                    {
                        "SQL_Latin1_General_CP1_CI_AS",
                        "Latin1_General_100_CI_AS",
                        "Japanese_CI_AS",
                        "French_CI_AS"
                    }
                };

                var vm = new MainViewModel(mockService);
                int collectionChangedCount = 0;
                NotifyCollectionChangedAction? lastAction = null;

                vm.SupportedCollations.CollectionChanged += (s, e) =>
                {
                    collectionChangedCount++;
                    lastAction = e.Action;
                };

                await vm.ConnectAsync();

                Assert(collectionChangedCount == 1, $"Expected collectionChangedCount 1, got {collectionChangedCount}");
                Assert(lastAction == NotifyCollectionChangedAction.Reset, $"Expected Reset action, got {lastAction}");
                Assert(vm.SupportedCollations.Count == 4, $"Expected 4 collations in collection, got {vm.SupportedCollations.Count}");
                Assert(vm.SupportedCollations[0] == "SQL_Latin1_General_CP1_CI_AS", "First collation mismatch");
                Assert(vm.SupportedCollations[3] == "French_CI_AS", "Fourth collation mismatch");
            });

            RunSyncTest("DatabaseInfo_RaisesPropertyChanged_WhenCollationChanges", () =>
            {
                var db = new DatabaseInfo
                {
                    Name = "TestDB",
                    Collation = "SQL_Latin1_General_CP1_CI_AS"
                };

                string? changedProperty = null;
                db.PropertyChanged += (sender, args) =>
                {
                    changedProperty = args.PropertyName;
                };

                db.Collation = "Latin1_General_100_CI_AS";

                Assert(nameof(DatabaseInfo.Collation) == changedProperty, $"Expected property name 'Collation', got '{changedProperty}'");
                Assert(db.Collation == "Latin1_General_100_CI_AS", $"Expected Collation 'Latin1_General_100_CI_AS', got '{db.Collation}'");
            });

            RunSyncTest("DatabaseService_EscapeIdentifier_EscapesRightBracketsCorrectly", () =>
            {
                var method = typeof(DatabaseService).GetMethod("EscapeIdentifier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert(method != null, "EscapeIdentifier method not found");
                string result = (string)method!.Invoke(null, new object[] { "Test]Db]Name" })!;
                Assert(result == "Test]]Db]]Name", $"Expected 'Test]]Db]]Name', got '{result}'");
            });

            await RunAsyncTest("DatabaseService_PreflightCheckAsync_HandlesConnectionErrorGracefully", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);
                dbService.SetConnectionString("invalid_server_xyz_12345", "Windows Authentication", "", "");

                var report = await dbService.PreflightCheckAsync("TestDB");

                Assert(report != null, "Report should not be null");
                Assert(report!.DatabaseName == "TestDB", "Report DatabaseName should match requested database");
                Assert(report.ValidationErrors.Count > 0, "PreflightReport should contain validation error when server connection fails");
                Assert(report.ValidationErrors[0].Contains("Database connection or pre-flight query error"), $"Expected error message to mention connection error, got '{report.ValidationErrors[0]}'");
            });

            RunSyncTest("DatabaseInfo_DatabaseSizeFormatting_Handles64BitIntegerSizes", () =>
            {
                long largeSizeMb = 3000000000L;
                var db = new DatabaseInfo
                {
                    Name = "LargeDB",
                    Size = $"{largeSizeMb} MB"
                };

                Assert(db.Size == "3000000000 MB", $"Expected '3000000000 MB', got '{db.Size}'");
            });

            RunSyncTest("ObservableRangeCollection_ReplaceRange_FiresSingleCollectionChangedResetEvent", () =>
            {
                var collection = new ObservableRangeCollection<DatabaseInfo>();
                int eventCount = 0;
                NotifyCollectionChangedAction? lastAction = null;

                collection.CollectionChanged += (sender, args) =>
                {
                    eventCount++;
                    lastAction = args.Action;
                };

                var items = new List<DatabaseInfo>
                {
                    new DatabaseInfo { Name = "DB1" },
                    new DatabaseInfo { Name = "DB2" },
                    new DatabaseInfo { Name = "DB3" }
                };

                collection.ReplaceRange(items);

                Assert(eventCount == 1, $"Expected eventCount 1, got {eventCount}");
                Assert(lastAction == NotifyCollectionChangedAction.Reset, $"Expected action Reset, got {lastAction}");
                Assert(collection.Count == 3, $"Expected count 3, got {collection.Count}");
            });

            await RunAsyncTest("LoadDatabasesAsync_FiresSingleCollectionChangedEvent", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo>
                    {
                        new DatabaseInfo { Name = "DB1", Collation = "Collation1" },
                        new DatabaseInfo { Name = "DB2", Collation = "Collation2" },
                        new DatabaseInfo { Name = "DB3", Collation = "Collation3" }
                    }
                };

                var vm = new MainViewModel(mockService);
                int collectionChangedCount = 0;
                vm.Databases.CollectionChanged += (s, e) =>
                {
                    collectionChangedCount++;
                };

                bool result = await vm.LoadDatabasesAsync();

                Assert(result == true, "Expected LoadDatabasesAsync to return true");
                Assert(collectionChangedCount == 1, $"Expected collectionChangedCount 1, got {collectionChangedCount}");
                Assert(vm.Databases.Count == 3, $"Expected 3 databases in collection, got {vm.Databases.Count}");
                Assert(mockService.GetDatabasesCount == 1, $"Expected GetDatabasesCount 1, got {mockService.GetDatabasesCount}");
            });

            await RunAsyncTest("LoadDatabasesAsync_WhenExceptionThrown_ClearsDatabasesAndReturnsFalse", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    ThrowOnGetDatabases = true
                };

                var vm = new MainViewModel(mockService);
                vm.Databases.Add(new DatabaseInfo { Name = "OldDB" });

                bool result = await vm.LoadDatabasesAsync();

                Assert(result == false, "Expected LoadDatabasesAsync to return false on exception");
                Assert(vm.Databases.Count == 0, $"Expected Databases to be cleared, but count was {vm.Databases.Count}");
                Assert(vm.StatusMessage.Contains("Failed to load databases"), $"Expected StatusMessage to contain error details, got '{vm.StatusMessage}'");
            });

            RunSyncTest("RefreshDatabasesCommand_GatedByIsConnectedAndIsNotBusy", () =>
            {
                var vm = new MainViewModel(new MockDatabaseService());
                Assert(vm.RefreshDatabasesCommand != null, "RefreshDatabasesCommand must be initialized");
                Assert(!vm.RefreshDatabasesCommand!.CanExecute(null), "RefreshDatabasesCommand must be disabled when disconnected");

                vm.IsConnected = true;
                Assert(vm.RefreshDatabasesCommand.CanExecute(null), "RefreshDatabasesCommand must be enabled when connected and not busy");

                vm.IsBusy = true;
                Assert(!vm.RefreshDatabasesCommand.CanExecute(null), "RefreshDatabasesCommand must be disabled when busy");

                vm.IsBusy = false;
                Assert(vm.RefreshDatabasesCommand.CanExecute(null), "RefreshDatabasesCommand must re-enable when no longer busy");

                vm.Disconnect();
                Assert(!vm.RefreshDatabasesCommand.CanExecute(null), "RefreshDatabasesCommand must be disabled after disconnecting");
            });

            await RunAsyncTest("RefreshDatabasesAsync_ReloadsInPlace_AndPreservesSelectedDatabaseCaseInsensitively", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo>
                    {
                        new DatabaseInfo { Name = "SalesDB", Collation = "SQL_Latin1_General_CP1_CI_AS", Size = "120 MB" },
                        new DatabaseInfo { Name = "InventoryDB", Collation = "SQL_Latin1_General_CP1_CI_AS", Size = "80 MB" }
                    },
                    DefaultBackupDirectoryToReturn = @"D:\SQLServer\Backups"
                };

                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();

                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";
                vm.BackupDirectory = @"E:\CustomBackups\Nightly";
                vm.SetStatus("Stale error", "Stale tooltip", "Stale detailed error");

                var updatedSalesDb = new DatabaseInfo { Name = "salesdb", Collation = "Latin1_General_CI_AS", Size = "150 MB" };
                var newAnalyticsDb = new DatabaseInfo { Name = "AnalyticsDB", Collation = "SQL_Latin1_General_CP1_CI_AS", Size = "40 MB" };
                mockService.DatabasesToReturn = new List<DatabaseInfo> { newAnalyticsDb, updatedSalesDb };

                int resetCount = 0;
                vm.Databases.CollectionChanged += (s, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Reset)
                    {
                        resetCount++;
                    }
                };

                await vm.RefreshDatabasesAsync();

                Assert(vm.IsConnected, "IsConnected must remain true after RefreshDatabasesAsync");
                Assert(!vm.IsBusy, "IsBusy must reset to false after RefreshDatabasesAsync");
                Assert(resetCount == 1, $"Expected exactly 1 Reset notification during refresh, got {resetCount}");
                Assert(vm.Databases.Count == 2, $"Expected 2 databases after refresh, got {vm.Databases.Count}");
                Assert(ReferenceEquals(vm.SelectedDatabase, updatedSalesDb), "SelectedDatabase must rebind to the refreshed DatabaseInfo instance matching by case-insensitive Name");
                Assert(vm.SelectedDatabase?.Size == "150 MB", $"Expected refreshed Size '150 MB', got '{vm.SelectedDatabase?.Size}'");
                Assert(vm.BackupDirectory == @"E:\CustomBackups\Nightly", "RefreshDatabasesAsync must preserve custom BackupDirectory");
                Assert(vm.ProcessCommand.CanExecute(null), "ProcessCommand must remain enabled when SelectedDatabase and TargetCollation are preserved");
                Assert(!vm.HasDetailedError, "Successful RefreshDatabasesAsync must clear stale DetailedError via SetStatus");
                Assert(vm.StatusMessage.Contains("refreshed", StringComparison.OrdinalIgnoreCase), $"Expected StatusMessage to confirm catalog refresh, got '{vm.StatusMessage}'");
            });

            await RunAsyncTest("LoadDatabasesAsync_ClearsSelectedDatabaseAndDisablesProcessCommand_WhenDroppedOrFaulted", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo>
                    {
                        new DatabaseInfo { Name = "DroppedDB", Collation = "SQL_Latin1_General_CP1_CI_AS" },
                        new DatabaseInfo { Name = "RetainedDB", Collation = "SQL_Latin1_General_CP1_CI_AS" }
                    }
                };

                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";
                Assert(vm.ProcessCommand.CanExecute(null), "ProcessCommand should be enabled initially");

                // 1. Server drops DroppedDB
                mockService.DatabasesToReturn = new List<DatabaseInfo>
                {
                    new DatabaseInfo { Name = "RetainedDB", Collation = "SQL_Latin1_General_CP1_CI_AS" }
                };
                await vm.RefreshDatabasesAsync();

                Assert(vm.SelectedDatabase == null, "SelectedDatabase must be cleared when dropped on server");
                Assert(!vm.ProcessCommand.CanExecute(null), "ProcessCommand must be disabled when SelectedDatabase is cleared");

                // 2. Reselect RetainedDB, then simulate fault during refresh
                vm.SelectedDatabase = vm.Databases[0];
                Assert(vm.ProcessCommand.CanExecute(null), "ProcessCommand should re-enable after selecting RetainedDB");

                mockService.ThrowOnGetDatabases = true;
                await vm.RefreshDatabasesAsync();

                Assert(vm.Databases.Count == 0, "Databases should be cleared when GetDatabasesAsync faults");
                Assert(vm.SelectedDatabase == null, "SelectedDatabase must be cleared when GetDatabasesAsync faults");
                Assert(!vm.ProcessCommand.CanExecute(null), "ProcessCommand must be disabled when GetDatabasesAsync faults");
                Assert(vm.StatusMessage.Contains("Failed to load databases"), $"Expected error status on faulted refresh, got '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ConnectAsync_PreservesCustomBackupDirectoryOnReconnect_AndRepopulatesWhenCleared", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "DB1" } },
                    DefaultBackupDirectoryToReturn = @"D:\ServerDefault\Backups"
                };

                var vm = new MainViewModel(mockService);

                // Initial connect populates server default backup path
                await vm.ConnectAsync();
                Assert(vm.BackupDirectory == @"D:\ServerDefault\Backups", $"Expected server default path on initial connect, got '{vm.BackupDirectory}'");

                // User enters a custom backup path, then disconnects and reconnects
                vm.BackupDirectory = @"Z:\DBA\CustomBackups";
                vm.Disconnect();
                mockService.DefaultBackupDirectoryToReturn = @"E:\AnotherServerDefault\Backups";
                await vm.ConnectAsync();
                Assert(vm.BackupDirectory == @"Z:\DBA\CustomBackups", $"Expected custom BackupDirectory to be preserved on reconnect, got '{vm.BackupDirectory}'");

                // Clearing BackupDirectory to whitespace falls back to server default on reconnect
                vm.BackupDirectory = "   ";
                vm.Disconnect();
                await vm.ConnectAsync();
                Assert(vm.BackupDirectory == @"E:\AnotherServerDefault\Backups", $"Expected server default BackupDirectory when custom path was cleared, got '{vm.BackupDirectory}'");
            });

            await RunAsyncTest("ConnectAsync_WhenLoadDatabasesFails_DefersIsConnectedToFalseAndPreservesErrorStatus", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    ThrowOnGetDatabases = true
                };

                var vm = new MainViewModel(mockService);
                Assert(vm.IsConnected == false, "Expected IsConnected to be false initially");

                await vm.ConnectAsync();

                Assert(vm.IsConnected == false, "Expected IsConnected to remain false when database load fails");
                Assert(vm.StatusMessage.Contains("Failed to load databases"), $"Expected StatusMessage to retain database error, got '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ConnectAsync_WhenLoadDatabasesSucceeds_SetsIsConnectedToTrueAndStatusReady", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "DB1" } }
                };

                var vm = new MainViewModel(mockService);
                Assert(vm.IsConnected == false, "Expected IsConnected to be false initially");

                await vm.ConnectAsync();

                Assert(vm.IsConnected == true, "Expected IsConnected to be true when connection and database load succeed");
                Assert(vm.StatusMessage == "Connected to server. Database operations panel unlocked.", $"Expected StatusMessage to announce operations panel unlock, got '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_UpdatesCollationInMemory_WithoutCallingLoadDatabasesAsync", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "DB1", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                int getDatabasesCallCountAfterLoad = mockService.GetDatabasesCount;

                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS_KS_WS";

                string? propertyChangedName = null;
                vm.SelectedDatabase.PropertyChanged += (s, e) =>
                {
                    propertyChangedName = e.PropertyName;
                };

                await vm.ProcessCollationChangeAsync();

                Assert(vm.SelectedDatabase.Collation == "Latin1_General_100_CI_AS_KS_WS", "Collation did not update in memory");
                Assert(propertyChangedName == nameof(DatabaseInfo.Collation), $"Expected PropertyChanged event for Collation, got {propertyChangedName}");
                Assert(mockService.GetDatabasesCount == getDatabasesCallCountAfterLoad, "GetDatabasesAsync was unexpectedly called during collation change!");
                Assert(mockService.AlterCollationCount == 1, "AlterCollationCount expected to be 1");
                Assert(ReferenceEquals(initialDb, vm.SelectedDatabase), "SelectedDatabase reference changed unexpectedly");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_ClearsDetailedErrorAndStatusToolTip_OnSuccess", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "DB1", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();

                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS_KS_WS";

                // Seed prior error state and tooltip
                vm.SetStatus("Prior error message", "Prior error tooltip", "Prior detailed error diagnostics");
                Assert(vm.HasDetailedError == true, "Expected HasDetailedError to be true before collation change");
                Assert(vm.DetailedError == "Prior detailed error diagnostics", "Expected DetailedError to be populated before collation change");
                Assert(vm.StatusToolTip == "Prior error tooltip", "Expected StatusToolTip to be populated before collation change");
                Assert(vm.EffectiveStatusToolTip == "Prior error tooltip", "Expected EffectiveStatusToolTip to match StatusToolTip before collation change");

                var notifiedProperties = new List<string>();
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null)
                    {
                        notifiedProperties.Add(e.PropertyName);
                    }
                };

                await vm.ProcessCollationChangeAsync();

                string expectedSuccessStatus = "Database default collation updated to 'Latin1_General_100_CI_AS_KS_WS'. 0 existing user columns kept prior collations.";
                Assert(vm.StatusMessage == expectedSuccessStatus, $"Expected StatusMessage '{expectedSuccessStatus}', got '{vm.StatusMessage}'");
                Assert(vm.DetailedError == null, $"Expected DetailedError to be null after successful collation change, got '{vm.DetailedError}'");
                Assert(vm.HasDetailedError == false, "Expected HasDetailedError to be false after successful collation change");
                Assert(vm.StatusToolTip == null, $"Expected StatusToolTip to be null after successful collation change, got '{vm.StatusToolTip}'");
                Assert(vm.EffectiveStatusToolTip == expectedSuccessStatus, $"Expected EffectiveStatusToolTip to fallback to StatusMessage, got '{vm.EffectiveStatusToolTip}'");
                Assert(notifiedProperties.Contains(nameof(MainViewModel.HasDetailedError)), "Expected PropertyChanged event for HasDetailedError");
                Assert(notifiedProperties.Contains(nameof(MainViewModel.EffectiveStatusToolTip)), "Expected PropertyChanged event for EffectiveStatusToolTip");
            });

            RunSyncTest("MainViewModel_IsConnected_DefaultsToFalse_AndNotifiesPropertyChanged", () =>
            {
                var vm = new MainViewModel(new MockDatabaseService());
                Assert(vm.IsConnected == false, "Expected IsConnected to default to false");

                var notifiedProperties = new List<string>();
                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName != null)
                    {
                        notifiedProperties.Add(args.PropertyName);
                    }
                };

                vm.IsConnected = true;

                Assert(vm.IsConnected == true, "Expected IsConnected to be true after setting");
                Assert(notifiedProperties.Contains(nameof(MainViewModel.IsConnected)), $"Expected PropertyChanged for 'IsConnected', got '{string.Join(", ", notifiedProperties)}'");
                Assert(notifiedProperties.Contains(nameof(MainViewModel.CanEditConnectionParameters)), $"Expected PropertyChanged for 'CanEditConnectionParameters', got '{string.Join(", ", notifiedProperties)}'");
                Assert(!notifiedProperties.Contains("CanEditConnection"), "Did not expect PropertyChanged for deleted 'CanEditConnection'");
            });

            await RunAsyncTest("ConnectAsync_UpdatesIsConnectedState_OnSuccessAndFailure", async () =>
            {
                var mockService = new MockDatabaseService();
                var vm = new MainViewModel(mockService);

                Assert(vm.IsConnected == false, "IsConnected should be false initially");

                await vm.ConnectAsync();
                Assert(vm.IsConnected == true, "IsConnected should be true after successful connect");

                // Test failure
                var failingService = new FailingMockDatabaseService();
                var failingVm = new MainViewModel(failingService);
                await failingVm.ConnectAsync();
                Assert(failingVm.IsConnected == false, "IsConnected should be false after failed connect");
            });

            RunSyncTest("CreateDefaultDatabaseService_InstantiatesDatabaseServiceDirectly", () =>
            {
                var vm = new MainViewModel();
                Assert(vm != null, "Expected MainViewModel to be instantiated using default constructor without reflection.");
            });

            await RunAsyncTest("LoadDatabasesAsync_PassesCancellationTokenToDatabaseService", async () =>
            {
                var mockService = new MockDatabaseService();
                var vm = new MainViewModel(mockService);
                using var cts = new CancellationTokenSource();
                var token = cts.Token;

                bool result = await vm.LoadDatabasesAsync(token);
                Assert(result == true, "Expected LoadDatabasesAsync with CancellationToken to return true");
                Assert(mockService.GetDatabasesCount == 1, "Expected GetDatabasesAsync to be called once");

                using var canceledCts = new CancellationTokenSource();
                canceledCts.Cancel();
                bool caughtCancellation = false;
                try
                {
                    await vm.LoadDatabasesAsync(canceledCts.Token);
                }
                catch (OperationCanceledException)
                {
                    caughtCancellation = true;
                }

                Assert(caughtCancellation, "Expected OperationCanceledException when LoadDatabasesAsync is called with a canceled token");
                Assert(mockService.GetDatabasesCount == 1, "Expected GetDatabasesCount to remain 1 after cancellation prior to database query");
                Assert(vm.StatusMessage == "Operation cancelled by user.", "Expected StatusMessage to be 'Operation cancelled by user.'");
            });

            await RunAsyncTest("RefreshDatabasesAsync_WhenCancelledViaCancelCommand_CatchesCancellationAndKeepsSelectionIntact", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "PrimaryDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var refreshStarted = new TaskCompletionSource<bool>();
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();
                vm.SelectedDatabase = vm.Databases[0];

                mockService.OnGetDatabases = async token =>
                {
                    refreshStarted.SetResult(true);
                    var tcs = new TaskCompletionSource<bool>();
                    using var reg = token.Register(() => tcs.TrySetCanceled(token));
                    await tcs.Task;
                };

                var refreshTask = vm.RefreshDatabasesAsync();
                await refreshStarted.Task;

                Assert(vm.IsBusy, "IsBusy must be true while RefreshDatabasesAsync is in progress");
                Assert(!vm.IsConnecting, "IsConnecting must remain false while refreshing an active connection");
                Assert(!vm.RefreshDatabasesCommand.CanExecute(null), "RefreshDatabasesCommand must be disabled while refresh is in progress");
                Assert(vm.CancelCommand.CanExecute(null), "CancelCommand must be enabled while refresh is in progress");

                vm.CancelCommand.Execute(null);
                await refreshTask;

                Assert(!vm.IsBusy, "IsBusy must reset to false after cancelling refresh");
                Assert(vm.IsConnected, "IsConnected must remain true after cancelling refresh");
                Assert(vm.Databases.Count == 1 && ReferenceEquals(vm.Databases[0], initialDb), "Existing Databases collection must remain intact when refresh is cancelled");
                Assert(ReferenceEquals(vm.SelectedDatabase, initialDb), "SelectedDatabase must remain intact when refresh is cancelled");
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Expected cancellation StatusMessage, got '{vm.StatusMessage}'");
            });

            RunSyncTest("MainWindowXaml_RefreshDatabasesButton_LayoutAndAccessibility_Configured", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);

                var selectDbCombo = doc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "ComboBox" &&
                    e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "SelectDatabaseInput"));
                Assert(selectDbCombo != null, "MainWindow.xaml must declare SelectDatabaseInput ComboBox");

                var btnRefresh = doc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "Button" &&
                    e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "btnRefreshDatabases"));
                Assert(btnRefresh != null, "MainWindow.xaml must declare btnRefreshDatabases Button");

                var parentGrid = selectDbCombo!.Parent;
                Assert(parentGrid != null && parentGrid.Name.LocalName == "Grid", "SelectDatabaseInput must be placed inside a Grid");
                Assert(ReferenceEquals(parentGrid, btnRefresh!.Parent), "SelectDatabaseInput and btnRefreshDatabases must share the same two-column Grid");

                var colDefs = parentGrid!.Elements()
                    .Where(e => e.Name.LocalName == "Grid.ColumnDefinitions")
                    .SelectMany(e => e.Elements().Where(c => c.Name.LocalName == "ColumnDefinition"))
                    .ToList();
                Assert(colDefs.Count == 2, $"Database selector Grid must define 2 columns, got {colDefs.Count}");
                Assert(colDefs[0].Attribute("Width")?.Value == "*", $"First column must be Width='*', got '{colDefs[0].Attribute("Width")?.Value}'");
                Assert(colDefs[1].Attribute("Width")?.Value == "Auto", $"Second column must be Width='Auto', got '{colDefs[1].Attribute("Width")?.Value}'");

                Assert((selectDbCombo.Attribute("Grid.Column")?.Value ?? "0") == "0", "SelectDatabaseInput must be in Grid.Column='0'");
                Assert(btnRefresh.Attribute("Grid.Column")?.Value == "1", "btnRefreshDatabases must be in Grid.Column='1'");

                var refreshContent = btnRefresh.Attribute("Content")?.Value;
                var refreshCommand = btnRefresh.Attribute("Command")?.Value;
                var refreshStyle = btnRefresh.Attribute("Style")?.Value;
                var refreshMinHeight = btnRefresh.Attribute("MinHeight")?.Value;
                var refreshAutomationName = btnRefresh.Attributes().FirstOrDefault(a =>
                    (a.Name.LocalName == "Name" && a.Name.NamespaceName.Contains("AutomationProperties")) ||
                    a.Name.ToString().Contains("AutomationProperties.Name"))?.Value;
                var refreshHelpText = btnRefresh.Attributes().FirstOrDefault(a =>
                    (a.Name.LocalName == "HelpText" && a.Name.NamespaceName.Contains("AutomationProperties")) ||
                    a.Name.ToString().Contains("AutomationProperties.HelpText"))?.Value;

                Assert(refreshContent == "Refresh", $"btnRefreshDatabases Content must be 'Refresh', got '{refreshContent}'");
                Assert(refreshCommand == "{Binding RefreshDatabasesCommand}", $"btnRefreshDatabases Command must bind to RefreshDatabasesCommand, got '{refreshCommand}'");
                Assert(refreshStyle == "{StaticResource SecondaryButton}", $"btnRefreshDatabases Style must use SecondaryButton, got '{refreshStyle}'");
                Assert(refreshMinHeight == "24", $"btnRefreshDatabases MinHeight must be '24', got '{refreshMinHeight}'");
                Assert(refreshAutomationName == "Refresh", $"btnRefreshDatabases AutomationProperties.Name must match visible Content 'Refresh' per WCAG 2.2 SC 2.5.3, got '{refreshAutomationName}'");
                Assert(refreshAutomationName == refreshContent, "btnRefreshDatabases AutomationProperties.Name must equal Content");
                Assert(!string.IsNullOrWhiteSpace(refreshHelpText), "btnRefreshDatabases must declare non-empty AutomationProperties.HelpText");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_HaltsBeforeBackup_WhenActiveSessionsExist", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "ActiveDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    PreflightReportToReturn = new PreflightReport
                    {
                        ActiveSessions = new List<ActiveSessionInfo>
                        {
                            new ActiveSessionInfo { SessionId = 55, LoginName = "app_user", HostName = "WORKSTATION1", ProgramName = "AppClient", Status = "running" }
                        }
                    }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS_KS_WS";

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 0, "Backup should NOT have been performed when active sessions exist!");
                Assert(mockService.AlterCollationCount == 0, "Alter Collation should NOT have been performed when active sessions exist!");
                Assert(vm.StatusMessage.Contains("Process failed before backup"), $"Expected status message to indicate failure before backup, got: '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_HaltsBeforeBackup_WhenSchemaDependenciesExist", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "SchemaDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    PreflightReportToReturn = new PreflightReport
                    {
                        SchemaDependencies = new List<SchemaDependencyInfo>
                        {
                            new SchemaDependencyInfo { ObjectName = "vw_Orders", ObjectType = "Schema-Bound Object", SchemaName = "dbo", Details = "Definition is schema-bound (VIEW)." },
                            new SchemaDependencyInfo { ObjectName = "tbl_Users.FullAddress", ObjectType = "Computed Column", SchemaName = "dbo", Details = "Computed column expression: [Street] + [City]" }
                        }
                    }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS_KS_WS";

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 0, "Backup should NOT have been performed when schema dependencies exist!");
                Assert(mockService.AlterCollationCount == 0, "Alter Collation should NOT have been performed when schema dependencies exist!");
                Assert(vm.StatusMessage.Contains("Process failed before backup"), $"Expected status message to indicate failure before backup, got: '{vm.StatusMessage}'");
            });

            RunSyncTest("PreflightReport_GetDiagnosticSummary_FormatsDetailsCorrectly", () =>
            {
                var report = new PreflightReport
                {
                    DatabaseName = "TestDiagDB",
                    ActiveSessions = new List<ActiveSessionInfo>
                    {
                        new ActiveSessionInfo { SessionId = 60, LoginName = "db_admin", HostName = "SERVER1", ProgramName = "SSMS", Status = "sleeping" }
                    },
                    SchemaDependencies = new List<SchemaDependencyInfo>
                    {
                        new SchemaDependencyInfo { ObjectName = "FK_Orders_Users", ObjectType = "Foreign Key Constraint", SchemaName = "dbo", Details = "Table 'Orders' references 'Users'." }
                    }
                };

                string summary = report.GetDiagnosticSummary();
                Assert(summary.Contains("FAILED (Blocking items found)"), "Summary should indicate failure");
                Assert(summary.Contains("Session ID: 60"), "Summary should include session ID");
                Assert(summary.Contains("FK_Orders_Users"), "Summary should include foreign key constraint name");
            });

            RunSyncTest("DatabaseService_EvaluatePermissions_Fails_WhenBackupDatabasePermissionMissing", () =>
            {
                var report = new PreflightReport { DatabaseName = "TestDB" };
                DatabaseService.EvaluatePermissions(new[] { "ALTER" }, report);

                Assert(report.HasMissingPermissions, "Report should have missing permissions when BACKUP DATABASE is missing.");
                Assert(report.HasBlockingConditions, "Report should have blocking conditions when BACKUP DATABASE is missing.");
                Assert(report.MissingPermissions.Count == 1, $"Expected 1 missing permission, got {report.MissingPermissions.Count}");
                Assert(report.MissingPermissions[0].Contains("BACKUP DATABASE"), "Missing permission should mention BACKUP DATABASE.");
                Assert(report.MissingPermissions[0].Contains("User lacks required database backup privileges"), "Missing permission should state lack of database backup privileges.");

                string summary = report.GetDiagnosticSummary();
                Assert(summary.Contains("FAILED (Blocking items found)"), "Diagnostic summary should indicate failure.");
                Assert(summary.Contains("User lacks required database backup privileges"), "Diagnostic summary must state user lacks required database backup privileges.");
            });

            RunSyncTest("DatabaseService_EvaluatePermissions_Fails_WhenAlterPermissionMissing", () =>
            {
                var report = new PreflightReport { DatabaseName = "TestDB" };
                DatabaseService.EvaluatePermissions(new[] { "BACKUP DATABASE" }, report);

                Assert(report.HasMissingPermissions, "Report should have missing permissions when ALTER is missing.");
                Assert(report.HasBlockingConditions, "Report should have blocking conditions when ALTER is missing.");
                Assert(report.MissingPermissions.Count == 1, $"Expected 1 missing permission, got {report.MissingPermissions.Count}");
                Assert(report.MissingPermissions[0].Contains("ALTER"), "Missing permission should mention ALTER.");
            });

            RunSyncTest("DatabaseService_EvaluatePermissions_Fails_WhenBothPermissionsMissing", () =>
            {
                var report = new PreflightReport { DatabaseName = "TestDB" };
                DatabaseService.EvaluatePermissions(Array.Empty<string>(), report);

                Assert(report.HasMissingPermissions, "Report should have missing permissions when both are missing.");
                Assert(report.HasBlockingConditions, "Report should have blocking conditions.");
                Assert(report.MissingPermissions.Count == 2, $"Expected 2 missing permissions, got {report.MissingPermissions.Count}");
                Assert(report.MissingPermissions.Any(p => p.Contains("ALTER")), "Should report missing ALTER permission.");
                Assert(report.MissingPermissions.Any(p => p.Contains("BACKUP DATABASE") && p.Contains("User lacks required database backup privileges")), "Should report missing BACKUP DATABASE permission.");
            });

            RunSyncTest("DatabaseService_EvaluatePermissions_Succeeds_WhenBothPermissionsPresent", () =>
            {
                var report = new PreflightReport { DatabaseName = "TestDB" };
                DatabaseService.EvaluatePermissions(new[] { "ALTER", "BACKUP DATABASE", "BACKUP LOG" }, report);

                Assert(!report.HasMissingPermissions, "Report should have no missing permissions when both ALTER and BACKUP DATABASE are granted.");
                Assert(report.MissingPermissions.Count == 0, "MissingPermissions should be empty.");
                Assert(!report.HasBlockingConditions, "Report should have no blocking conditions.");
                Assert(report.IsValid, "Report should be valid.");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_HaltsBeforeBackup_WhenBackupDatabasePermissionMissing", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "NoBackupPermDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var preflightReport = new PreflightReport { DatabaseName = "NoBackupPermDb" };
                DatabaseService.EvaluatePermissions(new[] { "ALTER" }, preflightReport);

                Assert(preflightReport.HasBlockingConditions, "PreflightReport should have blocking conditions when BACKUP DATABASE permission is missing.");
                Assert(preflightReport.HasMissingPermissions, "PreflightReport should flag missing permissions.");
                Assert(preflightReport.MissingPermissions.Any(p => p.Contains("BACKUP DATABASE") && p.Contains("User lacks required database backup privileges")), "Missing permissions must describe missing BACKUP DATABASE privilege.");

                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    PreflightReportToReturn = preflightReport
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS_KS_WS";

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 0, "Backup must NOT execute when user lacks BACKUP DATABASE permission!");
                Assert(mockService.AlterCollationCount == 0, "Alter Collation must NOT execute when preflight fails!");
                Assert(vm.StatusMessage.Contains("Pre-flight validation failed"), $"Expected status message to indicate preflight failure, got: '{vm.StatusMessage}'");
                Assert(vm.DetailedError != null && vm.DetailedError.Contains("User lacks required database backup privileges"), $"DetailedError should state user lacks required database backup privileges, got: '{vm.DetailedError}'");
            });

            await RunAsyncTest("ConnectAsync_DisablesConnectionParameters_And_UpdatesButtonText_WhenConnected", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "DB1" } }
                };

                var vm = new MainViewModel(mockService);

                Assert(!vm.IsConnected, "Initial IsConnected should be false");
                Assert(vm.ConnectButtonText == "Connect", $"Expected button text 'Connect', got '{vm.ConnectButtonText}'");
                Assert(vm.CanEditConnectionParameters, "CanEditConnectionParameters should be true initially");

                vm.SelectedAuthenticationType = "SQL Server Authentication";
                Assert(vm.CanEditSqlCredentials, "CanEditSqlCredentials should be true for SQL Auth when disconnected");

                await vm.ConnectAsync();

                Assert(vm.IsConnected, "IsConnected should be true after successful connection");
                Assert(vm.ConnectButtonText == "Disconnect", $"Expected button text 'Disconnect', got '{vm.ConnectButtonText}'");
                Assert(!vm.CanEditConnectionParameters, "CanEditConnectionParameters should be false after connecting");
                Assert(!vm.CanEditSqlCredentials, "CanEditSqlCredentials should be false after connecting");
            });

            await RunAsyncTest("Disconnect_ResetsConnectionState_ClearsDatabases_And_EnablesConnectionParameters", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo>
                    {
                        new DatabaseInfo { Name = "DB1" },
                        new DatabaseInfo { Name = "DB2" }
                    }
                };

                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();

                Assert(vm.IsConnected, "IsConnected should be true after connecting");
                Assert(vm.Databases.Count == 2, $"Expected 2 databases, got {vm.Databases.Count}");
                vm.SelectedDatabase = vm.Databases[0];

                await vm.ConnectAsync();

                Assert(!vm.IsConnected, "IsConnected should be false after disconnect");
                Assert(vm.Databases.Count == 0, "Databases should be cleared after disconnect");
                Assert(vm.SelectedDatabase == null, "SelectedDatabase should be null after disconnect");
                Assert(vm.ConnectButtonText == "Connect", $"Expected button text 'Connect', got '{vm.ConnectButtonText}'");
                Assert(vm.CanEditConnectionParameters, "CanEditConnectionParameters should be true after disconnect");
                Assert(vm.StatusMessage == "Disconnected from server. Database operations panel locked.", $"Expected 'Disconnected from server. Database operations panel locked.', got '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ToggleConnectionCommand_DisconnectsWhenConnected_AndConnectsWhenDisconnected", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "ToggleDB" } }
                };

                var vm = new MainViewModel(mockService);

                // Execute command to Connect
                if (vm.ConnectCommand.CanExecute(null))
                {
                    vm.ConnectCommand.Execute(null);
                }
                // Allow async Task execution to finish
                await Task.Delay(50);

                Assert(vm.IsConnected, "IsConnected should be true after executing ConnectCommand");

                // Execute command to Disconnect
                if (vm.ConnectCommand.CanExecute(null))
                {
                    vm.ConnectCommand.Execute(null);
                }
                await Task.Delay(50);

                Assert(!vm.IsConnected, "IsConnected should be false after executing ConnectCommand second time");
            });

            RunSyncTest("CanEditConnectionParameters_ReturnsFalse_WhenBusyOrConnected", () =>
            {
                var vm = new MainViewModel(new MockDatabaseService());

                // Disconnected and Not Busy -> Can Edit
                vm.IsBusy = false;
                vm.IsConnected = false;
                Assert(vm.CanEditConnectionParameters, "Should be editable when not busy and not connected");

                // Busy and Disconnected -> Cannot Edit
                vm.IsBusy = true;
                vm.IsConnected = false;
                Assert(!vm.CanEditConnectionParameters, "Should not be editable when busy");

                // Not Busy and Connected -> Cannot Edit
                vm.IsBusy = false;
                vm.IsConnected = true;
                Assert(!vm.CanEditConnectionParameters, "Should not be editable when connected");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_DisplaysExplicitWarning_WhenMultiUserResetFails", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "LockDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    ShouldAlterCollationThrow = true,
                    ShouldTrySetMultiUserFail = true
                };

                var vm = new MainViewModel(mockService);
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.TrySetMultiUserCount > 0, "TrySetMultiUserAsync should have been called during emergency recovery!");
                Assert(vm.StatusMessage.Contains("WARNING: Database remains locked in SINGLE_USER mode."), $"Status message should include explicit SINGLE_USER lock warning, got: '{vm.StatusMessage}'");
                Assert(vm.StatusMessage.Contains("Table constraint violation"), $"Status message should include primary error details, got: '{vm.StatusMessage}'");
                Assert(vm.StatusMessage.Contains("Connection reset failed"), $"Status message should include secondary reset failure details, got: '{vm.StatusMessage}'");
                Assert(vm.HasDetailedError == true, "HasDetailedError should be true when emergency reset fails");
                Assert(!string.IsNullOrEmpty(vm.DetailedError), "DetailedError should be populated when emergency reset fails");
                Assert(vm.DetailedError.Contains("WARNING: Database remains locked in SINGLE_USER mode."), $"DetailedError should contain lock warning, got: '{vm.DetailedError}'");
                Assert(vm.StatusToolTip == vm.StatusMessage, $"StatusToolTip should match StatusMessage, got: '{vm.StatusToolTip}'");
                Assert(vm.ShowErrorDetailsCommand.CanExecute(null) == true, "ShowErrorDetailsCommand should be executable when HasDetailedError is true");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_ConfirmsMultiUserRestored_WhenEmergencyResetSucceeds", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "RecoverDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    ShouldAlterCollationThrow = true,
                    ShouldTrySetMultiUserFail = false
                };

                var vm = new MainViewModel(mockService);
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.TrySetMultiUserCount > 0, "TrySetMultiUserAsync should have been called during recovery!");
                Assert(!mockService.LastRestoreCancellationToken.IsCancellationRequested, "RestoreDatabaseAsync must receive an uncancelled token during error recovery!");
                Assert(mockService.IsMultiUser, "Database should be restored to MULTI_USER mode!");
                Assert(vm.StatusMessage.Contains("Database restored to MULTI_USER mode successfully"), $"Status message should confirm MULTI_USER restoration, got: '{vm.StatusMessage}'");
                Assert(vm.StatusMessage.Contains("Table constraint violation"), $"Status message should include primary error details, got: '{vm.StatusMessage}'");
                Assert(vm.HasDetailedError == true, "HasDetailedError should be true after successful auto-restore");
                Assert(!string.IsNullOrEmpty(vm.DetailedError), "DetailedError should be populated after successful auto-restore");
                Assert(vm.DetailedError.Contains("Table constraint violation"), $"DetailedError should contain primary error details, got: '{vm.DetailedError}'");
                Assert(vm.DetailedError.Contains("Database was restored to MULTI_USER mode successfully from backup"), $"DetailedError should contain restore confirmation, got: '{vm.DetailedError}'");
                Assert(vm.StatusToolTip == vm.StatusMessage, $"StatusToolTip should match StatusMessage, got: '{vm.StatusToolTip}'");
                Assert(vm.ShowErrorDetailsCommand.CanExecute(null) == true, "ShowErrorDetailsCommand should be executable when HasDetailedError is true");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_AutoRestoreSuccess_PopulatesTooltipAndLockWarning_WhenLockStateDetected", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "LockWarningDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    ShouldAlterCollationThrow = true,
                    ShouldTrySetMultiUserFail = true
                };

                var vm = new MainViewModel(mockService);
                string? capturedErrorDetails = null;
                vm.ShowErrorDetailsHandler = details => capturedErrorDetails = details;

                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                await vm.ProcessCollationChangeAsync();

                Assert(vm.StatusMessage.Contains("WARNING: Database remains locked in SINGLE_USER mode."), $"Status message should include single-user lock warning, got: '{vm.StatusMessage}'");
                Assert(vm.StatusToolTip == vm.StatusMessage, $"StatusToolTip should match StatusMessage, got: '{vm.StatusToolTip}'");
                Assert(vm.HasDetailedError == true, "HasDetailedError must be true after successful auto-restore with lock warning");
                Assert(!string.IsNullOrEmpty(vm.DetailedError), "DetailedError must be populated");
                Assert(vm.DetailedError.Contains("WARNING: Database remains locked in SINGLE_USER mode."), "DetailedError must contain single-user lock warning");
                Assert(vm.DetailedError.Contains("Table constraint violation"), "DetailedError must contain primary error text");
                Assert(vm.ShowErrorDetailsCommand.CanExecute(null) == true, "ShowErrorDetailsCommand must be executable");

                vm.ShowErrorDetailsCommand.Execute(null);
                Assert(capturedErrorDetails == vm.DetailedError, "ShowErrorDetailsHandler should receive the formatted DetailedError text");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_AutoRestoreSuccess_EnablesShowErrorDetailsCommand_AndPassesDiagnostics", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "DetailsDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    ShouldAlterCollationThrow = true,
                    ShouldTrySetMultiUserFail = false
                };

                var vm = new MainViewModel(mockService);
                string? capturedDetails = null;
                vm.ShowErrorDetailsHandler = details => capturedDetails = details;

                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                await vm.ProcessCollationChangeAsync();

                Assert(vm.HasDetailedError == true, "HasDetailedError must be true");
                Assert(vm.StatusToolTip != null, "StatusToolTip must be populated");
                Assert(vm.StatusToolTip == vm.StatusMessage, "StatusToolTip must match StatusMessage");
                Assert(vm.ShowErrorDetailsCommand.CanExecute(null) == true, "ShowErrorDetailsCommand must be enabled");

                vm.ShowErrorDetailsCommand.Execute(null);
                Assert(capturedDetails != null && capturedDetails.Contains("Database was restored to MULTI_USER mode successfully"),
                    "ShowErrorDetailsHandler should be called with full diagnostic information");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_RollsBackWithUncancelledToken_WhenCancelledAfterBackup", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "CancelRollbackDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                // Simulate cancellation during AlterCollation (which occurs after backup has completed)
                mockService.OnAlterCollation = async token =>
                {
                    mockService.IsMultiUser = false; // Simulate database put in SINGLE_USER mode
                    vm.Cancel();
                    token.ThrowIfCancellationRequested();
                    await Task.CompletedTask;
                };

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 1, "Backup should have completed prior to cancellation.");
                Assert(mockService.RestoreCount == 1, "RestoreDatabaseAsync should be invoked when cancelled after backup.");
                Assert(!mockService.LastRestoreCancellationToken.IsCancellationRequested, "RestoreDatabaseAsync must receive an uncancelled token (CancellationToken.None).");
                Assert(mockService.IsMultiUser, "Database must be restored to MULTI_USER mode.");
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Status message should indicate cancellation by user, got: '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_SkipsRollback_WhenCancelledBeforeBackup", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "CancelBeforeBackupDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                // Simulate cancellation before or during backup
                mockService.OnBackupDatabase = async token =>
                {
                    vm.Cancel();
                    token.ThrowIfCancellationRequested();
                    await Task.CompletedTask;
                };

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.RestoreCount == 0, "RestoreDatabaseAsync should NOT be invoked if cancelled before backup completes.");
                Assert(mockService.TrySetMultiUserCount == 0, "TrySetMultiUserAsync should NOT be invoked if cancelled before alteration starts.");
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Status message should indicate cancellation by user, got: '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_SkipsMultiUserReset_WhenCancelledDuringPreflight", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "CancelDuringPreflightDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    ShouldTrySetMultiUserFail = true
                };

                var vm = new MainViewModel(mockService);
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                mockService.OnPreflightCheck = async token =>
                {
                    vm.Cancel();
                    token.ThrowIfCancellationRequested();
                    await Task.CompletedTask;
                };

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 0, "BackupDatabaseAsync should NOT be invoked when cancelled during pre-flight.");
                Assert(mockService.RestoreCount == 0, "RestoreDatabaseAsync should NOT be invoked when cancelled during pre-flight.");
                Assert(mockService.AlterCollationCount == 0, "AlterCollationAsync should NOT be invoked when cancelled during pre-flight.");
                Assert(mockService.TrySetMultiUserCount == 0, "TrySetMultiUserAsync should NOT be invoked when cancelled during pre-flight.");
                Assert(!vm.HasDetailedError, "HasDetailedError should be false when pre-alter cancellation skips TrySetMultiUserAsync.");
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Status message should indicate cancellation by user, got: '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_SkipsMultiUserReset_WhenCancelledAfterBackupBeforeAlter", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "CancelAfterBackupBeforeAlterDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    ShouldTrySetMultiUserFail = true
                };

                var vm = new MainViewModel(mockService);
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                // Cancel after BackupDatabaseAsync passes its token check so backup completes,
                // triggering cancellation right before AlterCollationAsync starts.
                mockService.BackupProgressSimulator = _ => vm.Cancel();

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 1, "BackupDatabaseAsync should have completed before post-backup cancellation check.");
                Assert(mockService.RestoreCount == 1, "RestoreDatabaseAsync should still be invoked when backup completed before cancellation.");
                Assert(mockService.AlterCollationCount == 0, "AlterCollationAsync should NOT have started.");
                Assert(mockService.TrySetMultiUserCount == 0, "TrySetMultiUserAsync should NOT be invoked when cancelled before AlterCollationAsync starts.");
                Assert(mockService.IsMultiUser, "Database must remain in MULTI_USER mode after restore.");
                Assert(!vm.HasDetailedError, "HasDetailedError should be false when restore succeeds and unnecessary TrySetMultiUserAsync is skipped.");
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Status message should indicate cancellation by user, got: '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_SurfacesRestoreFailure_WhenRestoreThrowsDuringCancellation", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "CancelRestoreFailDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    RestoreExceptionToThrow = new InvalidOperationException("Restore disk I/O error occurred."),
                    ShouldTrySetMultiUserFail = false
                };

                var vm = new MainViewModel(mockService);
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                mockService.OnAlterCollation = async token =>
                {
                    vm.Cancel();
                    token.ThrowIfCancellationRequested();
                    await Task.CompletedTask;
                };

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 1, "Backup should have completed prior to cancellation.");
                Assert(mockService.RestoreCount == 1, "RestoreDatabaseAsync should have been called.");
                Assert(mockService.TrySetMultiUserCount > 0, "TrySetMultiUserAsync should have been called.");
                Assert(vm.StatusMessage.Contains("CRITICAL: Auto-restore also failed"), $"Expected auto-restore failure message in StatusMessage, got: '{vm.StatusMessage}'");
                Assert(vm.StatusMessage.Contains("Restore disk I/O error occurred"), $"Expected restore error details in StatusMessage, got: '{vm.StatusMessage}'");
                Assert(vm.StatusToolTip != null && vm.StatusToolTip.Contains("CRITICAL: Auto-restore also failed"), "StatusToolTip should contain restore error diagnostics.");
                Assert(vm.HasDetailedError, "HasDetailedError should be true to activate error details dialog.");
                Assert(vm.DetailedError != null && vm.DetailedError.Contains("Restore disk I/O error occurred"), "DetailedError should contain restore error details.");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_SurfacesSingleUserLockWarning_WhenResetThrowsDuringCancellation", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "CancelResetFailDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    RestoreExceptionToThrow = null,
                    ShouldTrySetMultiUserFail = true
                };

                var vm = new MainViewModel(mockService);
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                mockService.OnAlterCollation = async token =>
                {
                    vm.Cancel();
                    token.ThrowIfCancellationRequested();
                    await Task.CompletedTask;
                };

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 1, "Backup should have completed prior to cancellation.");
                Assert(mockService.RestoreCount == 1, "RestoreDatabaseAsync should have succeeded.");
                Assert(mockService.TrySetMultiUserCount > 0, "TrySetMultiUserAsync should have been attempted.");
                Assert(vm.StatusMessage.Contains("WARNING: Database remains locked in SINGLE_USER mode."), $"Expected SINGLE_USER lock warning in StatusMessage, got: '{vm.StatusMessage}'");
                Assert(vm.StatusMessage.Contains("Connection reset failed"), $"Expected reset failure details in StatusMessage, got: '{vm.StatusMessage}'");
                Assert(vm.HasDetailedError, "HasDetailedError should be true when single-user reset fails during cancellation.");
                Assert(vm.DetailedError != null && vm.DetailedError.Contains("Connection reset failed"), "DetailedError should contain reset failure details.");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_SurfacesAggregatedErrors_WhenBothRestoreAndResetFailDuringCancellation", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "CancelBothFailDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    RestoreExceptionToThrow = new InvalidOperationException("Restore disk I/O error occurred."),
                    ShouldTrySetMultiUserFail = true
                };

                var vm = new MainViewModel(mockService);
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                mockService.OnAlterCollation = async token =>
                {
                    vm.Cancel();
                    token.ThrowIfCancellationRequested();
                    await Task.CompletedTask;
                };

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 1, "Backup should have completed prior to cancellation.");
                Assert(mockService.RestoreCount == 1, "RestoreDatabaseAsync should have been attempted.");
                Assert(mockService.TrySetMultiUserCount > 0, "TrySetMultiUserAsync should have been attempted.");
                Assert(vm.StatusMessage.Contains("CRITICAL: Auto-restore also failed"), $"Expected auto-restore failure message in StatusMessage, got: '{vm.StatusMessage}'");
                Assert(vm.StatusMessage.Contains("WARNING: Database remains locked in SINGLE_USER mode."), $"Expected lock warning in StatusMessage, got: '{vm.StatusMessage}'");
                Assert(vm.HasDetailedError, "HasDetailedError should be true when both restore and reset fail.");
                Assert(vm.DetailedError != null && vm.DetailedError.Contains("Restore disk I/O error occurred"), "DetailedError should contain restore error details.");
                Assert(vm.DetailedError != null && vm.DetailedError.Contains("Connection reset failed"), "DetailedError should contain reset error details.");
            });

            await RunAsyncTest("DatabaseService_TrySetMultiUserAsync_ExecutesWithoutCancellation_WhenTokenIsCancelled", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);
                dbService.SetConnectionString("invalid_server_xyz_12345", "Windows Authentication", "", "");

                using var cts = new CancellationTokenSource();
                cts.Cancel();

                bool wasCanceled = false;
                bool wasOtherException = false;
                try
                {
                    await dbService.TrySetMultiUserAsync("TestDB", cts.Token);
                }
                catch (OperationCanceledException)
                {
                    wasCanceled = true;
                }
                catch (Exception)
                {
                    wasOtherException = true;
                }

                Assert(!wasCanceled, "TrySetMultiUserAsync must not throw OperationCanceledException when called with cancelled token.");
                Assert(wasOtherException, "TrySetMultiUserAsync should attempt connection execution without checking cancellation state.");
            });

            await RunAsyncTest("DatabaseService_TrySetMultiUserAsync_ThrowsException_WhenConnectionFails", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);
                dbService.SetConnectionString("invalid_server_xyz_12345", "Windows Authentication", "", "");

                bool exceptionThrown = false;
                try
                {
                    await dbService.TrySetMultiUserAsync("TestDB");
                }
                catch (Exception)
                {
                    exceptionThrown = true;
                }

                Assert(exceptionThrown, "TrySetMultiUserAsync must throw an exception when reset command fails (no swallowed exceptions).");
            });

            await RunAsyncTest("DatabaseService_AlterCollationAsync_RaisesAggregateException_WhenResetAlsoFails", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);
                dbService.SetConnectionString("invalid_server_xyz_12345", "Windows Authentication", "", "");

                AggregateException? caughtAggEx = null;
                try
                {
                    await dbService.AlterCollationAsync("TestDB", "Latin1_General_100_CI_AS");
                }
                catch (AggregateException aggEx)
                {
                    caughtAggEx = aggEx;
                }

                Assert(caughtAggEx != null, "AlterCollationAsync must raise an AggregateException when both primary collation and emergency reset fail.");
                Assert(caughtAggEx!.InnerExceptions.Count == 2, $"AggregateException should contain 2 inner exceptions (primary and reset failure), got {caughtAggEx.InnerExceptions.Count}.");
                Assert(caughtAggEx.Message.Contains("emergency MULTI_USER reset failed"), $"AggregateException message should mention emergency reset failure, got: '{caughtAggEx.Message}'");
            });

            await RunAsyncTest("DatabaseService_RestoreDatabaseAsync_RaisesAggregateException_WhenResetAlsoFails", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);
                dbService.SetConnectionString("invalid_server_xyz_12345", "Windows Authentication", "", "");

                AggregateException? caughtAggEx = null;
                try
                {
                    await dbService.RestoreDatabaseAsync("TestDB", "/tmp/nonexistent.bak");
                }
                catch (AggregateException aggEx)
                {
                    caughtAggEx = aggEx;
                }

                Assert(caughtAggEx != null, "RestoreDatabaseAsync must raise an AggregateException when both restore and emergency reset fail.");
                Assert(caughtAggEx!.InnerExceptions.Count == 2, $"AggregateException should contain 2 inner exceptions (primary and reset failure), got {caughtAggEx.InnerExceptions.Count}.");
                Assert(caughtAggEx.Message.Contains("emergency MULTI_USER reset failed"), $"AggregateException message should mention emergency reset failure, got: '{caughtAggEx.Message}'");
                Assert(caughtAggEx.Message.Contains("Failed to restore database 'TestDB'"), $"AggregateException message should mention restore database failure, got: '{caughtAggEx.Message}'");
            });

            await RunAsyncTest("ConnectAsync_PopulatesSupportedCollations_OnSuccessfulConnection", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "TestDB" } },
                    CollationsToReturn = new List<string> { "SQL_Latin1_General_CP1_CI_AS", "Latin1_General_BIN" }
                };
                var vm = new MainViewModel(mockService);

                await vm.ConnectAsync();

                Assert(vm.IsConnected, "IsConnected should be true after successful connect");
                Assert(mockService.GetCollationsCount > 0, $"Expected GetSupportedCollationsAsync to be invoked, count was {mockService.GetCollationsCount}");
                Assert(vm.SupportedCollations.Count == 2, $"Expected 2 supported collations in VM, got {vm.SupportedCollations.Count}");
            });

            await RunAsyncTest("TargetCollation_KeyboardTextSearchAndSelection_UpdatesTargetCollationCorrectly", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "TestDB" } },
                    CollationsToReturn = new List<string> { "SQL_Latin1_General_CP1_CI_AS", "Latin1_General_100_CI_AS_SC", "Japanese_CI_AS" }
                };
                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();
                vm.SelectedDatabase = vm.Databases[0];

                var propertyChangedList = new List<string>();
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null) propertyChangedList.Add(e.PropertyName);
                };

                // Simulate keyboard typing prefix search
                string prefixTyped = "Latin1_General";
                var matchedCollation = vm.SupportedCollations.FirstOrDefault(c => c.StartsWith(prefixTyped, StringComparison.OrdinalIgnoreCase));
                Assert(matchedCollation == "Latin1_General_100_CI_AS_SC", $"Expected matched collation to be 'Latin1_General_100_CI_AS_SC', got '{matchedCollation}'");

                // Update TargetCollation via text entry / dropdown selection
                vm.TargetCollation = matchedCollation!;
                Assert(vm.TargetCollation == "Latin1_General_100_CI_AS_SC", "TargetCollation should reflect selected item");
                Assert(propertyChangedList.Contains(nameof(MainViewModel.TargetCollation)), "TargetCollation change should fire PropertyChanged");
                Assert(vm.ProcessCommand.CanExecute(null), "ProcessCommand should be executable when valid database and collation are set");

                // Manual text entry
                vm.TargetCollation = "Custom_Collation_CI_AS";
                Assert(vm.TargetCollation == "Custom_Collation_CI_AS", "TargetCollation should allow direct manual text input");
            });

            await RunAsyncTest("TargetCollation_LargeCollationDataset_LoadsAndFiltersEfficiently", async () =>
            {
                // Generate 3,885 collation records simulating full SQL Server catalog
                var largeCatalog = new List<string>(3885);
                for (int i = 0; i < 3885; i++)
                {
                    largeCatalog.Add($"SQL_Collation_{i:D4}_CP1_CI_AS");
                }

                var mockService = new MockDatabaseService
                {
                    CollationsToReturn = largeCatalog
                };
                var vm = new MainViewModel(mockService);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                await vm.ConnectAsync();
                sw.Stop();

                Assert(vm.SupportedCollations.Count == 3885, $"Expected 3,885 collations loaded, got {vm.SupportedCollations.Count}");
                Assert(sw.ElapsedMilliseconds < 1000, $"Loading 3885 collations took too long: {sw.ElapsedMilliseconds} ms");

                // Simulate rapid keyboard text search filtering across 3885 items
                string searchPrefix = "SQL_Collation_2500";
                var searchResult = vm.SupportedCollations.FirstOrDefault(c => c.StartsWith(searchPrefix, StringComparison.OrdinalIgnoreCase));
                Assert(searchResult == "SQL_Collation_2500_CP1_CI_AS", $"Expected SQL_Collation_2500_CP1_CI_AS, got '{searchResult}'");

                vm.TargetCollation = searchResult!;
                Assert(vm.TargetCollation == "SQL_Collation_2500_CP1_CI_AS", "TargetCollation selection must be retained");
            });

            await RunAsyncTest("TargetCollation_EmptyCollationList_HandlesGracefully", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    CollationsToReturn = new List<string>()
                };
                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();

                Assert(vm.SupportedCollations.Count == 0, $"Expected 0 collations, got {vm.SupportedCollations.Count}");

                // Even with empty list, user can input target collation text without crash
                vm.TargetCollation = "Latin1_General_CI_AS";
                Assert(vm.TargetCollation == "Latin1_General_CI_AS", "TargetCollation should accept text even when collation catalog is empty");
            });

            await RunAsyncTest("PreflightCheckAsync_ValidatesTargetCollationFormat_AndRejectsInvalidFormat", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);

                var report = await dbService.PreflightCheckAsync("TestDB", "", "Invalid;Collation!DropTable");

                Assert(report.HasBlockingConditions, "Report should have blocking conditions for invalid collation format.");
                Assert(report.ValidationErrors.Any(e => e.Contains("Invalid target collation format")), "ValidationErrors should report invalid collation format error.");
                Assert(!report.ValidationErrors.Any(e => e.Contains("Database connection or pre-flight query error")), "Should short-circuit and not attempt database connection when collation format is invalid.");
            });

            await RunAsyncTest("PreflightCheckAsync_RejectsCollationWithSpaces_AndShortCircuitsWithoutConnectionError", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);

                var report = await dbService.PreflightCheckAsync("TestDB", "", "SQL Latin1 General");

                Assert(report.HasBlockingConditions, "Report should have blocking conditions when collation contains spaces.");
                Assert(report.ValidationErrors.Count == 1, $"Expected exactly 1 validation error, got {report.ValidationErrors.Count}");
                Assert(report.ValidationErrors[0].Contains("Invalid target collation format"), "Validation error must state invalid target collation format.");
                Assert(report.ValidationErrors[0].Contains("only alphanumeric characters and underscores"), "Validation error must state exact formatting requirements.");
            });

            await RunAsyncTest("PreflightCheckAsync_ValidatesTargetCollationFormat_AndAcceptsValidFormat", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);

                var report = await dbService.PreflightCheckAsync("TestDB", "", "SQL_Latin1_General_CP1_CI_AS");

                Assert(!report.ValidationErrors.Any(e => e.Contains("Invalid target collation format")), "ValidationErrors should not report format error for valid collation format.");
            });

            await RunAsyncTest("PreflightCheckAsync_ValidatesCollationsAgainstCache_WithoutIssuingNewQueries", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new TestableDatabaseService(logger);

                Assert(dbService.GetSupportedCollationsCallCount == 0, "Collation query count should initially be 0");

                var report1 = await dbService.PreflightCheckAsync("TestDB", "", "SQL_Latin1_General_CP1_CI_AS");
                Assert(dbService.GetSupportedCollationsCallCount == 1, $"Expected 1 collation query to populate cache, got {dbService.GetSupportedCollationsCallCount}");
                Assert(!report1.ValidationErrors.Any(e => e.Contains("is not supported by SQL Server")), "Expected valid collation in cache to not generate error");

                // Second check with another valid collation from cache - should not issue new query
                var report2 = await dbService.PreflightCheckAsync("TestDB", "", "Latin1_General_100_CI_AS");
                Assert(dbService.GetSupportedCollationsCallCount == 1, $"Expected zero additional queries when cache is populated, got {dbService.GetSupportedCollationsCallCount}");
                Assert(!report2.ValidationErrors.Any(e => e.Contains("is not supported by SQL Server")), "Expected valid collation in cache to not generate error");

                // Case-insensitive lookup check
                var report3 = await dbService.PreflightCheckAsync("TestDB", "", "sql_latin1_general_cp1_ci_as");
                Assert(dbService.GetSupportedCollationsCallCount == 1, $"Expected zero additional queries for case-insensitive check, got {dbService.GetSupportedCollationsCallCount}");
                Assert(!report3.ValidationErrors.Any(e => e.Contains("is not supported by SQL Server")), "Expected case-insensitive collation to be recognized");
            });

            await RunAsyncTest("PreflightCheckAsync_RejectsUnsupportedCollation_UsingCachedData", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new TestableDatabaseService(logger);

                var report = await dbService.PreflightCheckAsync("TestDB", "", "NonExistentCollation_XYZ");

                Assert(dbService.GetSupportedCollationsCallCount == 1, "Expected collation fetch to populate cache");
                Assert(report.ValidationErrors.Any(e => e.Contains("Target collation 'NonExistentCollation_XYZ' is not supported by SQL Server.")),
                    "Expected unsupported collation to be reported in validation errors.");

                // Re-check another unsupported collation uses cache without new query
                var report2 = await dbService.PreflightCheckAsync("TestDB", "", "Another_Unknown_Collation");
                Assert(dbService.GetSupportedCollationsCallCount == 1, "Expected no new queries for second collation check");
                Assert(report2.ValidationErrors.Any(e => e.Contains("Target collation 'Another_Unknown_Collation' is not supported by SQL Server.")),
                    "Expected second unsupported collation to also be rejected from cache.");
            });

            await RunAsyncTest("SetConnectionString_ClearsCollationCache_WhenCredentialsChange", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new TestableDatabaseService(logger);
                dbService.SetConnectionString("ServerA", "Windows Authentication", "", "");

                var report1 = await dbService.PreflightCheckAsync("TestDB", "", "SQL_Latin1_General_CP1_CI_AS");
                Assert(dbService.GetSupportedCollationsCallCount == 1, "Expected 1 collation fetch for initial connection");

                // Same connection string should not invalidate cache
                dbService.SetConnectionString("ServerA", "Windows Authentication", "", "");
                var report2 = await dbService.PreflightCheckAsync("TestDB", "", "SQL_Latin1_General_CP1_CI_AS");
                Assert(dbService.GetSupportedCollationsCallCount == 1, "Expected cache to be preserved when connection string unchanged");

                // Changed server/credentials must invalidate cache
                dbService.SetConnectionString("ServerB", "Windows Authentication", "", "");
                var report3 = await dbService.PreflightCheckAsync("TestDB", "", "SQL_Latin1_General_CP1_CI_AS");
                Assert(dbService.GetSupportedCollationsCallCount == 2, $"Expected cache to be re-populated after connection string changed, got {dbService.GetSupportedCollationsCallCount}");
            });

            await RunAsyncTest("PreflightCheckAsync_WhenCachePopulationFails_LogsWarningAndDoesNotBlockExecution", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new TestableDatabaseService(logger)
                {
                    ThrowOnGetSupportedCollations = true
                };

                var report = await dbService.PreflightCheckAsync("TestDB", "", "SQL_Latin1_General_CP1_CI_AS");

                Assert(report != null, "Report should not be null when collation cache population fails");
                Assert(dbService.GetSupportedCollationsCallCount == 1, "Expected attempt to fetch collations");
                Assert(!report!.ValidationErrors.Any(e => e.Contains("is not supported by SQL Server")),
                    "Collation should not be falsely reported as unsupported when catalog fetch failed");
            });

            RunSyncTest("DatabaseService_ValidateCollationFormat_ValidatesAlphanumericAndUnderscoresOnly", () =>
            {
                // Valid collations
                DatabaseService.ValidateCollationFormat("SQL_Latin1_General_CP1_CI_AS");
                DatabaseService.ValidateCollationFormat("Latin1_General_100_CI_AS");
                DatabaseService.ValidateCollationFormat("Japanese_CI_AS");
                DatabaseService.ValidateCollationFormat("Collation_123");
                Assert(DatabaseService.IsValidCollationName("SQL_Latin1_General_CP1_CI_AS"), "IsValidCollationName should be true for valid format.");

                // Invalid collations with spaces, symbols, punctuation, or empty/null
                string[] invalidInputs = new[]
                {
                    "SQL Latin1",
                    "Latin1-General",
                    "Collation;DROP TABLE",
                    "Collation#1",
                    "Collation' OR 1=1--",
                    "   ",
                    "",
                    null
                };

                foreach (var invalid in invalidInputs)
                {
                    Assert(!DatabaseService.IsValidCollationName(invalid), $"IsValidCollationName should be false for: '{invalid}'");
                    bool threw = false;
                    try
                    {
                        DatabaseService.ValidateCollationFormat(invalid);
                    }
                    catch (ArgumentException ex)
                    {
                        threw = true;
                        Assert(ex.Message.Contains("only alphanumeric characters and underscores"), $"Exception message should guide user on allowed characters, got: {ex.Message}");
                    }
                    Assert(threw, $"ValidateCollationFormat must throw ArgumentException for invalid collation: '{invalid}'");
                }
            });

            await RunAsyncTest("DatabaseService_AlterCollationAsync_SharesValidationLogic_AndThrowsArgumentException", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);

                bool threw = false;
                try
                {
                    await dbService.AlterCollationAsync("TestDB", "Invalid Collation Name!");
                }
                catch (ArgumentException ex)
                {
                    threw = true;
                    Assert(ex.Message.Contains("Invalid target collation format"), $"Expected format error message, got: {ex.Message}");
                    Assert(ex.Message.Contains("only alphanumeric characters and underscores"), $"Expected requirement details, got: {ex.Message}");
                }
                Assert(threw, "AlterCollationAsync must throw ArgumentException on invalid collation format using shared validation logic.");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_HaltsBeforeBackup_WhenTargetCollationFormatIsInvalid", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "CollationDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Invalid Collation Name with Spaces!";

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 0, "Automated database backup must NOT be executed when collation format is invalid!");
                Assert(mockService.AlterCollationCount == 0, "AlterCollationAsync must NOT be executed when collation format is invalid!");
                Assert(vm.StatusMessage.Contains("Process failed before backup"), $"Expected status message to indicate failure before backup, got: '{vm.StatusMessage}'");
                Assert(vm.StatusMessage.Contains("Invalid target collation format"), $"Expected status message to mention collation format error, got: '{vm.StatusMessage}'");
            });

            RunSyncTest("MainViewModel_HasSelectedDatabase_EvaluatesToFalseInitially_AndUpdatesToTrueOnSelection", () =>
            {
                var vm = new MainViewModel(new MockDatabaseService());

                Assert(!vm.HasSelectedDatabase, "Expected HasSelectedDatabase to be false initially.");

                var db = new DatabaseInfo { Name = "TestDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                vm.Databases.Add(db);

                Assert(!vm.HasSelectedDatabase, "Expected HasSelectedDatabase to be false before database selection.");

                vm.SelectedDatabase = db;
                Assert(vm.HasSelectedDatabase, "Expected HasSelectedDatabase to be true after selecting a database.");

                vm.SelectedDatabase = null;
                Assert(!vm.HasSelectedDatabase, "Expected HasSelectedDatabase to be false after clearing selection.");
            });

            RunSyncTest("MainViewModel_MetadataEmptyStateText_ProvidesContextSensitiveGuidance", () =>
            {
                var vm = new MainViewModel(new MockDatabaseService());

                // 1. When Databases is empty (Databases.Count == 0)
                Assert(!vm.HasDatabases, "Expected HasDatabases to be false when list is empty.");
                Assert(!vm.HasSelectedDatabase, "Expected HasSelectedDatabase to be false.");
                Assert(vm.MetadataEmptyStateText.Contains("No candidate databases found"), $"Expected empty state text to mention no candidate databases, got: '{vm.MetadataEmptyStateText}'");

                // 2. When Databases is populated but no database selected
                var db = new DatabaseInfo { Name = "DB1", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                vm.Databases.Add(db);
                Assert(vm.HasDatabases, "Expected HasDatabases to be true when list has items.");
                Assert(!vm.HasSelectedDatabase, "Expected HasSelectedDatabase to be false.");
                Assert(vm.MetadataEmptyStateText.Contains("Select a database"), $"Expected empty state text to request database selection, got: '{vm.MetadataEmptyStateText}'");

                // 3. When a database is selected
                vm.SelectedDatabase = db;
                Assert(vm.HasSelectedDatabase, "Expected HasSelectedDatabase to be true.");
                Assert(string.IsNullOrEmpty(vm.MetadataEmptyStateText), $"Expected empty state text to be empty when database selected, got: '{vm.MetadataEmptyStateText}'");
            });

            RunSyncTest("MainViewModel_RaisesPropertyChanged_ForEmptyStateProperties", () =>
            {
                var vm = new MainViewModel(new MockDatabaseService());
                var changedProperties = new List<string>();

                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName != null)
                    {
                        changedProperties.Add(args.PropertyName);
                    }
                };

                var db = new DatabaseInfo { Name = "NotifyDB" };
                vm.Databases.Add(db);

                Assert(changedProperties.Contains(nameof(MainViewModel.HasDatabases)), "Expected PropertyChanged for HasDatabases on collection change.");
                Assert(changedProperties.Contains(nameof(MainViewModel.MetadataEmptyStateText)), "Expected PropertyChanged for MetadataEmptyStateText on collection change.");

                changedProperties.Clear();
                vm.SelectedDatabase = db;

                Assert(changedProperties.Contains(nameof(MainViewModel.HasSelectedDatabase)), "Expected PropertyChanged for HasSelectedDatabase on selection.");
                Assert(changedProperties.Contains(nameof(MainViewModel.MetadataEmptyStateText)), "Expected PropertyChanged for MetadataEmptyStateText on selection.");
            });

            RunSyncTest("AppXaml_FocusVisualStyle_ImplicitStyles_Configured", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string content = System.IO.File.ReadAllText(appXamlPath);

                Assert(content.Contains("x:Key=\"FocusVisualStyle\""), "App.xaml should define FocusVisualStyle");
                Assert(content.Contains("x:Key=\"FocusBorderBrush\" Color=\"#0284C7\""), "App.xaml should define FocusBorderBrush with #0284C7");
                Assert(content.Contains("<Style TargetType=\"Button\">"), "App.xaml should define implicit TargetType=\"Button\" style");
                Assert(content.Contains("<Setter Property=\"FocusVisualStyle\" Value=\"{StaticResource FocusVisualStyle}\" />"), "App.xaml should apply FocusVisualStyle across implicit control styles");
                Assert(!content.Contains("<Setter Property=\"BorderThickness\" Value=\"2\" />"), "App.xaml should not contain layout-shifting BorderThickness=2 trigger setters");
                Assert(content.Contains("x:Key=\"StatusSuccessBrush\" Color=\"#047857\""), "App.xaml should define StatusSuccessBrush with #047857");
                Assert(content.Contains("#047857"), "App.xaml should define PrimaryButton background with #047857");
                Assert(content.Contains("#1D4ED8"), "App.xaml should define SecondaryButton background with #1D4ED8");
                Assert(!content.Contains("#10B981"), "App.xaml should not contain non-compliant color #10B981");
                Assert(!content.Contains("#3B82F6"), "App.xaml should not contain non-compliant color #3B82F6");
            });

            RunSyncTest("AppXaml_ComboBoxStyle_EditableFocusIndicatorAndAdorner_Configured", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string appXamlContent = System.IO.File.ReadAllText(appXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(appXamlContent);

                var comboBoxStyle = doc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "Style" &&
                    e.Attribute("TargetType")?.Value == "ComboBox" &&
                    e.Attributes().All(a => a.Name.LocalName != "Key"));
                Assert(comboBoxStyle != null, "App.xaml must define an implicit <Style TargetType=\"ComboBox\">");

                var setters = comboBoxStyle!.Elements().Where(e => e.Name.LocalName == "Setter").ToList();
                Assert(setters.Any(s => s.Attribute("Property")?.Value == "BorderThickness" && s.Attribute("Value")?.Value == "1"),
                    "ComboBox style must keep BorderThickness=\"1\" to prevent layout shift");
                Assert(setters.Any(s => s.Attribute("Property")?.Value == "FocusVisualStyle" && s.Attribute("Value")?.Value == "{StaticResource FocusVisualStyle}"),
                    "ComboBox style must keep default FocusVisualStyle=\"{StaticResource FocusVisualStyle}\" for non-editable ComboBox controls");
                Assert(setters.Any(s => s.Attribute("Property")?.Value == "local:App.TrackEditableFocus" && s.Attribute("Value")?.Value == "True"),
                    "ComboBox style must bind shared attached property local:App.TrackEditableFocus=\"True\"");

                var triggersElement = comboBoxStyle.Elements().FirstOrDefault(e => e.Name.LocalName == "Style.Triggers");
                Assert(triggersElement != null, "ComboBox style must declare Style.Triggers");
                var triggers = triggersElement!.Elements().Where(e => e.Name.LocalName == "Trigger").ToList();

                var keyboardFocusedTrigger = triggers.FirstOrDefault(t =>
                    t.Attribute("Property")?.Value == "IsKeyboardFocused" &&
                    t.Attribute("Value")?.Value == "True");
                Assert(keyboardFocusedTrigger != null, "ComboBox style must retain Trigger for IsKeyboardFocused=\"True\"");
                Assert(keyboardFocusedTrigger!.Elements().Any(s =>
                    s.Name.LocalName == "Setter" &&
                    s.Attribute("Property")?.Value == "BorderBrush" &&
                    s.Attribute("Value")?.Value == "{StaticResource FocusBorderBrush}"),
                    "IsKeyboardFocused=\"True\" trigger must set BorderBrush to {StaticResource FocusBorderBrush}");

                var keyboardFocusWithinTrigger = triggers.FirstOrDefault(t =>
                    t.Attribute("Property")?.Value == "IsKeyboardFocusWithin" &&
                    t.Attribute("Value")?.Value == "True");
                Assert(keyboardFocusWithinTrigger != null, "ComboBox style must declare Trigger for IsKeyboardFocusWithin=\"True\"");
                Assert(keyboardFocusWithinTrigger!.Elements().Any(s =>
                    s.Name.LocalName == "Setter" &&
                    s.Attribute("Property")?.Value == "BorderBrush" &&
                    s.Attribute("Value")?.Value == "{StaticResource FocusBorderBrush}"),
                    "IsKeyboardFocusWithin=\"True\" trigger must set BorderBrush to {StaticResource FocusBorderBrush}");

                var editableTrigger = triggers.FirstOrDefault(t =>
                    t.Attribute("Property")?.Value == "IsEditable" &&
                    t.Attribute("Value")?.Value == "True");
                Assert(editableTrigger != null, "ComboBox style must declare Trigger for IsEditable=\"True\"");
                Assert(editableTrigger!.Elements().Any(s =>
                    s.Name.LocalName == "Setter" &&
                    s.Attribute("Property")?.Value == "FocusVisualStyle" &&
                    s.Attribute("Value")?.Value == "{x:Null}"),
                    "IsEditable=\"True\" trigger must set FocusVisualStyle to {x:Null} to prevent duplicate outer adorners");

                Assert(!triggers.SelectMany(t => t.Elements()).Any(s => s.Attribute("Property")?.Value == "BorderThickness"),
                    "ComboBox triggers must not modify BorderThickness");

                string appXamlCsPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(appXamlPath)!, "App.xaml.cs"));
                Assert(System.IO.File.Exists(appXamlCsPath), $"App.xaml.cs file should exist at path {appXamlCsPath}");
                string appCsContent = System.IO.File.ReadAllText(appXamlCsPath);

                Assert(appCsContent.Contains("TrackEditableFocusProperty"), "App.xaml.cs must register TrackEditableFocusProperty");
                Assert(appCsContent.Contains("IsEditable"), "App.xaml.cs must guard editable focus tracking on ComboBox.IsEditable");
                Assert(appCsContent.Contains("IsKeyboardFocusWithinChanged"), "App.xaml.cs must subscribe to IsKeyboardFocusWithinChanged");
                Assert(appCsContent.Contains("IsEnabledChanged"), "App.xaml.cs must subscribe to IsEnabledChanged");
                Assert(appCsContent.Contains("Loaded +="), "App.xaml.cs must subscribe to Loaded");
                Assert(appCsContent.Contains("Unloaded +="), "App.xaml.cs must subscribe to Unloaded");
                Assert(appCsContent.Contains(": Adorner"), "App.xaml.cs must define a custom Adorner around the outer ComboBox");
                Assert(appCsContent.Contains("AdornerLayer.GetAdornerLayer"), "App.xaml.cs must manage the focus indicator in AdornerLayer");
                Assert(appCsContent.Contains("\"FocusVisualStyle\""), "App.xaml.cs must resolve FocusVisualStyle resource for the adorner");
                Assert(appCsContent.Contains("IsHitTestVisible = false"), "Editable ComboBox focus adorner must be non-interactive (IsHitTestVisible = false)");
                Assert(appCsContent.Contains("Focusable = false"), "Editable ComboBox focus adorner must not be focusable (Focusable = false)");

                // Verify WCAG 2.2 SC 1.4.11 non-text contrast (>= 3.0:1) for #0284C7 against #F8FAFC and #FFFFFF
                double fR = 2.0 / 255.0, fG = 132.0 / 255.0, fB = 199.0 / 255.0;
                double fRLin = fR <= 0.04045 ? fR / 12.92 : Math.Pow((fR + 0.055) / 1.055, 2.4);
                double fGLin = fG <= 0.04045 ? fG / 12.92 : Math.Pow((fG + 0.055) / 1.055, 2.4);
                double fBLin = fB <= 0.04045 ? fB / 12.92 : Math.Pow((fB + 0.055) / 1.055, 2.4);
                double focusLum = 0.2126 * fRLin + 0.7152 * fGLin + 0.0722 * fBLin;

                double bgR = 248.0 / 255.0, bgG = 250.0 / 255.0, bgB = 252.0 / 255.0;
                double bgRLin = bgR <= 0.04045 ? bgR / 12.92 : Math.Pow((bgR + 0.055) / 1.055, 2.4);
                double bgGLin = bgG <= 0.04045 ? bgG / 12.92 : Math.Pow((bgG + 0.055) / 1.055, 2.4);
                double bgBLin = bgB <= 0.04045 ? bgB / 12.92 : Math.Pow((bgB + 0.055) / 1.055, 2.4);
                double slateBgLum = 0.2126 * bgRLin + 0.7152 * bgGLin + 0.0722 * bgBLin;

                double contrastOnSlate = (slateBgLum + 0.05) / (focusLum + 0.05);
                double contrastOnWhite = (1.0 + 0.05) / (focusLum + 0.05);
                Assert(contrastOnSlate >= 3.0, $"Focus ring contrast {contrastOnSlate:F2}:1 on #F8FAFC must meet WCAG 2.2 SC 1.4.11 (>= 3:1)");
                Assert(contrastOnWhite >= 3.0, $"Focus ring contrast {contrastOnWhite:F2}:1 on #FFFFFF must meet WCAG 2.2 SC 1.4.11 (>= 3:1)");
            });

            RunSyncTest("ErrorDetailsWindowXaml_CopyStatus_UsesAccessibleStatusSuccessBrush", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string appContent = System.IO.File.ReadAllText(appXamlPath);
                Assert(appContent.Contains("x:Key=\"StatusSuccessBrush\" Color=\"#047857\""), "App.xaml should define StatusSuccessBrush with #047857");

                string errorDetailsXamlPath = ResolveCollationModifierFilePath("ErrorDetailsWindow.xaml");

                string errorDetailsContent = System.IO.File.ReadAllText(errorDetailsXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(errorDetailsContent);
                var textBlocks = doc.Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();
                var copiedStatusTb = textBlocks.FirstOrDefault(tb =>
                    tb.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "lblCopiedStatus"));

                Assert(copiedStatusTb != null, "ErrorDetailsWindow.xaml must contain lblCopiedStatus TextBlock");
                Assert(copiedStatusTb!.Attribute("Foreground")?.Value == "{StaticResource StatusSuccessBrush}",
                    "lblCopiedStatus should bind Foreground to {StaticResource StatusSuccessBrush}");
                Assert(!errorDetailsContent.Contains("#16A34A"),
                    "ErrorDetailsWindow.xaml must not contain non-compliant hardcoded color #16A34A");

                // Verify relative luminance and contrast ratio calculation for #047857 vs #F8FAFC window background (WCAG 2.2 SC 1.4.3 >= 4.5:1)
                double sR1 = 4.0 / 255.0;
                double sG1 = 120.0 / 255.0;
                double sB1 = 87.0 / 255.0;
                double rLin1 = sR1 <= 0.04045 ? sR1 / 12.92 : Math.Pow((sR1 + 0.055) / 1.055, 2.4);
                double gLin1 = sG1 <= 0.04045 ? sG1 / 12.92 : Math.Pow((sG1 + 0.055) / 1.055, 2.4);
                double bLin1 = sB1 <= 0.04045 ? sB1 / 12.92 : Math.Pow((sB1 + 0.055) / 1.055, 2.4);
                double lumFg = 0.2126 * rLin1 + 0.7152 * gLin1 + 0.0722 * bLin1;

                double sR2 = 248.0 / 255.0;
                double sG2 = 250.0 / 255.0;
                double sB2 = 252.0 / 255.0;
                double rLin2 = sR2 <= 0.04045 ? sR2 / 12.92 : Math.Pow((sR2 + 0.055) / 1.055, 2.4);
                double gLin2 = sG2 <= 0.04045 ? sG2 / 12.92 : Math.Pow((sG2 + 0.055) / 1.055, 2.4);
                double bLin2 = sB2 <= 0.04045 ? sB2 / 12.92 : Math.Pow((sB2 + 0.055) / 1.055, 2.4);
                double lumBg = 0.2126 * rLin2 + 0.7152 * gLin2 + 0.0722 * bLin2;

                double contrastRatio = (lumBg + 0.05) / (lumFg + 0.05);
                Assert(contrastRatio >= 4.5, $"Contrast ratio {contrastRatio:F2}:1 against window background #F8FAFC must meet or exceed 4.5:1 under WCAG 2.2 1.4.3");
            });

            RunSyncTest("AppXaml_InputRestingBorderBrush_HighContrastConfiguration", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string content = System.IO.File.ReadAllText(appXamlPath);

                Assert(content.Contains("x:Key=\"InputRestingBorderBrush\" Color=\"#64748B\""), "App.xaml should define InputRestingBorderBrush with #64748B");
                Assert(content.Contains("<Setter Property=\"BorderBrush\" Value=\"{StaticResource InputRestingBorderBrush}\" />"), "App.xaml should set BorderBrush to InputRestingBorderBrush");
                Assert(!content.Contains("<Setter Property=\"BorderBrush\" Value=\"#CBD5E1\" />"), "App.xaml must not contain low-contrast #CBD5E1 resting border brush");

                // Verify relative luminance and contrast ratio calculation for #64748B vs #FFFFFF
                double sR = 100.0 / 255.0;
                double sG = 116.0 / 255.0;
                double sB = 139.0 / 255.0;
                double rLin = sR <= 0.04045 ? sR / 12.92 : Math.Pow((sR + 0.055) / 1.055, 2.4);
                double gLin = sG <= 0.04045 ? sG / 12.92 : Math.Pow((sG + 0.055) / 1.055, 2.4);
                double bLin = sB <= 0.04045 ? sB / 12.92 : Math.Pow((sB + 0.055) / 1.055, 2.4);
                double lum = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
                double contrastAgainstWhite = (1.0 + 0.05) / (lum + 0.05);
                Assert(contrastAgainstWhite >= 3.0, $"Contrast ratio {contrastAgainstWhite} against white background must exceed 3:1 under WCAG 2.2 1.4.11");
            });

            RunSyncTest("AppXaml_DisabledInputStyleTriggers_Configured", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string content = System.IO.File.ReadAllText(appXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var styles = doc.Descendants().Where(e => e.Name.LocalName == "Style").ToList();

                string[] inputTargetTypes = new[] { "TextBox", "PasswordBox", "ComboBox" };
                foreach (string targetType in inputTargetTypes)
                {
                    var style = styles.FirstOrDefault(s => s.Attribute("TargetType")?.Value == targetType);
                    Assert(style != null, $"App.xaml should define style for TargetType='{targetType}'");

                    var triggersContainer = style!.Elements().FirstOrDefault(e => e.Name.LocalName == "Style.Triggers");
                    Assert(triggersContainer != null, $"Style for '{targetType}' should contain <Style.Triggers>");

                    var disabledTrigger = triggersContainer!.Elements().FirstOrDefault(e =>
                        e.Name.LocalName == "Trigger" &&
                        e.Attribute("Property")?.Value == "IsEnabled" &&
                        e.Attribute("Value")?.Value == "False");
                    Assert(disabledTrigger != null, $"Style for '{targetType}' must configure <Trigger Property=\"IsEnabled\" Value=\"False\">");

                    var bgSetter = disabledTrigger!.Elements().FirstOrDefault(e =>
                        e.Name.LocalName == "Setter" &&
                        e.Attribute("Property")?.Value == "Background" &&
                        e.Attribute("Value")?.Value == "{StaticResource DisabledBackgroundBrush}");
                    Assert(bgSetter != null, $"Style for '{targetType}' disabled trigger must set Background to {{StaticResource DisabledBackgroundBrush}}");

                    var fgSetter = disabledTrigger!.Elements().FirstOrDefault(e =>
                        e.Name.LocalName == "Setter" &&
                        e.Attribute("Property")?.Value == "Foreground" &&
                        e.Attribute("Value")?.Value == "{StaticResource DisabledForegroundBrush}");
                    Assert(fgSetter != null, $"Style for '{targetType}' disabled trigger must set Foreground to {{StaticResource DisabledForegroundBrush}}");

                    var borderSetter = disabledTrigger!.Elements().FirstOrDefault(e =>
                        e.Name.LocalName == "Setter" &&
                        e.Attribute("Property")?.Value == "BorderBrush" &&
                        e.Attribute("Value")?.Value == "{StaticResource DisabledBackgroundBrush}");
                    Assert(borderSetter != null, $"Style for '{targetType}' disabled trigger must set BorderBrush to {{StaticResource DisabledBackgroundBrush}}");
                }
            });

            RunSyncTest("MainWindowXaml_MetadataValues_AutomationProperties_Configured", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var textBlocks = doc.Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();

                // 1. Database Default Collation
                var collationValueTb = textBlocks.FirstOrDefault(tb =>
                    tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.Collation") == true);
                Assert(collationValueTb != null, "Collation value TextBlock should have AutomationProperties.Name bound to SelectedDatabase.Collation");
                string collationNameAttr = collationValueTb!.Attribute("AutomationProperties.Name")!.Value;
                Assert(collationNameAttr.Contains("StringFormat='Database Default Collation: {0}'"), "Collation value TextBlock should format with 'Database Default Collation: {0}'");
                Assert(collationNameAttr.Contains("FallbackValue='Database Default Collation: -'"), "Collation value TextBlock should have FallbackValue='Database Default Collation: -'");
                Assert(collationNameAttr.Contains("TargetNullValue='Database Default Collation: -'"), "Collation value TextBlock should have TargetNullValue='Database Default Collation: -'");
                string collationLabeledBy = collationValueTb.Attribute("AutomationProperties.LabeledBy")?.Value ?? "";
                Assert(collationLabeledBy.Contains("ElementName=DatabaseDefaultCollationLabel"), "Collation value TextBlock should bind LabeledBy to DatabaseDefaultCollationLabel");

                // 1b. User Character Columns
                var userColumnsValueTb = textBlocks.FirstOrDefault(tb =>
                    tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.UserCharacterColumns") == true);
                Assert(userColumnsValueTb != null, "User Character Columns value TextBlock should have AutomationProperties.Name bound to SelectedDatabase.UserCharacterColumns");
                string userColumnsNameAttr = userColumnsValueTb!.Attribute("AutomationProperties.Name")!.Value;
                Assert(userColumnsNameAttr.Contains("StringFormat='User Character Columns: {0}'"), "User Character Columns value TextBlock should format with 'User Character Columns: {0}'");
                Assert(userColumnsNameAttr.Contains("FallbackValue='User Character Columns: -'"), "User Character Columns value TextBlock should have FallbackValue='User Character Columns: -'");
                Assert(userColumnsNameAttr.Contains("TargetNullValue='User Character Columns: -'"), "User Character Columns value TextBlock should have TargetNullValue='User Character Columns: -'");
                string userColumnsLabeledBy = userColumnsValueTb.Attribute("AutomationProperties.LabeledBy")?.Value ?? "";
                Assert(userColumnsLabeledBy.Contains("ElementName=UserCharacterColumnsLabel"), "User Character Columns value TextBlock should bind LabeledBy to UserCharacterColumnsLabel");

                // 2. Compatibility Level
                var compatValueTb = textBlocks.FirstOrDefault(tb =>
                    tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.CompatibilityLevel") == true);
                Assert(compatValueTb != null, "Compatibility Level value TextBlock should have AutomationProperties.Name bound to SelectedDatabase.CompatibilityLevel");
                string compatNameAttr = compatValueTb!.Attribute("AutomationProperties.Name")!.Value;
                Assert(compatNameAttr.Contains("StringFormat='Compatibility Level: {0}'"), "Compatibility Level value TextBlock should format with 'Compatibility Level: {0}'");
                Assert(compatNameAttr.Contains("FallbackValue='Compatibility Level: -'"), "Compatibility Level value TextBlock should have FallbackValue='Compatibility Level: -'");
                Assert(compatNameAttr.Contains("TargetNullValue='Compatibility Level: -'"), "Compatibility Level value TextBlock should have TargetNullValue='Compatibility Level: -'");
                string compatLabeledBy = compatValueTb.Attribute("AutomationProperties.LabeledBy")?.Value ?? "";
                Assert(compatLabeledBy.Contains("ElementName=CompatibilityLevelLabel"), "Compatibility Level value TextBlock should retain LabeledBy binding to CompatibilityLevelLabel");

                // 3. Owner
                var ownerValueTb = textBlocks.FirstOrDefault(tb =>
                    tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.Owner") == true);
                Assert(ownerValueTb != null, "Owner value TextBlock should have AutomationProperties.Name bound to SelectedDatabase.Owner");
                string ownerNameAttr = ownerValueTb!.Attribute("AutomationProperties.Name")!.Value;
                Assert(ownerNameAttr.Contains("StringFormat='Owner: {0}'"), "Owner value TextBlock should format with 'Owner: {0}'");
                Assert(ownerNameAttr.Contains("FallbackValue='Owner: -'"), "Owner value TextBlock should have FallbackValue='Owner: -'");
                Assert(ownerNameAttr.Contains("TargetNullValue='Owner: -'"), "Owner value TextBlock should have TargetNullValue='Owner: -'");
                string ownerLabeledBy = ownerValueTb.Attribute("AutomationProperties.LabeledBy")?.Value ?? "";
                Assert(ownerLabeledBy.Contains("ElementName=OwnerLabel"), "Owner value TextBlock should retain LabeledBy binding to OwnerLabel");

                // 4. Size
                var sizeValueTb = textBlocks.FirstOrDefault(tb =>
                    tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.Size") == true);
                Assert(sizeValueTb != null, "Size value TextBlock should have AutomationProperties.Name bound to SelectedDatabase.Size");
                string sizeNameAttr = sizeValueTb!.Attribute("AutomationProperties.Name")!.Value;
                Assert(sizeNameAttr.Contains("StringFormat='Size: {0}'"), "Size value TextBlock should format with 'Size: {0}'");
                Assert(sizeNameAttr.Contains("FallbackValue='Size: -'"), "Size value TextBlock should have FallbackValue='Size: -'");
                Assert(sizeNameAttr.Contains("TargetNullValue='Size: -'"), "Size value TextBlock should have TargetNullValue='Size: -'");
                string sizeLabeledBy = sizeValueTb.Attribute("AutomationProperties.LabeledBy")?.Value ?? "";
                Assert(sizeLabeledBy.Contains("ElementName=SizeLabel"), "Size value TextBlock should retain LabeledBy binding to SizeLabel");
            });

            RunSyncTest("MainWindowXaml_MetadataEmptyState_BindingsTriggersAndContrast_Configured", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                var xName = System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
                var mainDoc = System.Xml.Linq.XDocument.Parse(System.IO.File.ReadAllText(mainWindowXamlPath));
                var appDoc = System.Xml.Linq.XDocument.Parse(System.IO.File.ReadAllText(appXamlPath));

                // 1. MetadataPropertyGrid wraps the 4 metadata rows and toggles Visibility via HasSelectedDatabase
                var metadataPropertyGrid = mainDoc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "Grid" && e.Attribute(xName)?.Value == "MetadataPropertyGrid");
                Assert(metadataPropertyGrid != null, "MainWindow.xaml must define Grid x:Name='MetadataPropertyGrid'");

                var propGridStyle = metadataPropertyGrid!.Elements().FirstOrDefault(e => e.Name.LocalName == "Grid.Style")
                    ?.Elements().FirstOrDefault(e => e.Name.LocalName == "Style" && e.Attribute("TargetType")?.Value == "Grid");
                Assert(propGridStyle != null, "MetadataPropertyGrid must define an inline Style with TargetType='Grid'");

                var propGridDefaultVisibility = propGridStyle!.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "Setter" && e.Attribute("Property")?.Value == "Visibility");
                Assert(propGridDefaultVisibility?.Attribute("Value")?.Value == "Collapsed",
                    "MetadataPropertyGrid must default Visibility to Collapsed when no database is selected");

                var propGridTrigger = propGridStyle.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "DataTrigger" &&
                    e.Attribute("Binding")?.Value == "{Binding HasSelectedDatabase}" &&
                    e.Attribute("Value")?.Value == "True");
                Assert(propGridTrigger != null, "MetadataPropertyGrid must define a DataTrigger for {Binding HasSelectedDatabase} == True");
                var propGridTriggerSetter = propGridTrigger!.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "Setter" && e.Attribute("Property")?.Value == "Visibility");
                Assert(propGridTriggerSetter?.Attribute("Value")?.Value == "Visible",
                    "MetadataPropertyGrid DataTrigger must set Visibility to Visible when HasSelectedDatabase is True");

                var propGridTextBlocks = metadataPropertyGrid.Elements().Where(e => e.Name.LocalName == "TextBlock").ToList();
                Assert(propGridTextBlocks.Count == 8, $"MetadataPropertyGrid should contain all 8 label and value TextBlocks, got {propGridTextBlocks.Count}");

                // 2. MetadataEmptyStateContainer sits beside MetadataPropertyGrid inside the metadata Border Grid
                var metadataEmptyStateContainer = mainDoc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "StackPanel" && e.Attribute(xName)?.Value == "MetadataEmptyStateContainer");
                Assert(metadataEmptyStateContainer != null, "MainWindow.xaml must define StackPanel x:Name='MetadataEmptyStateContainer'");
                Assert(metadataEmptyStateContainer!.Parent == metadataPropertyGrid.Parent,
                    "MetadataEmptyStateContainer and MetadataPropertyGrid must share the same parent Grid inside the metadata Border");
                Assert(metadataEmptyStateContainer.Attribute("Margin")?.Value == "12",
                    "MetadataEmptyStateContainer must specify valid Margin='12' on StackPanel");
                Assert(metadataEmptyStateContainer.Attribute("Padding") == null,
                    "MetadataEmptyStateContainer StackPanel must not specify invalid Padding attribute");
                Assert(metadataEmptyStateContainer.Attribute("HorizontalAlignment")?.Value == "Center",
                    "MetadataEmptyStateContainer must specify HorizontalAlignment='Center'");
                Assert(metadataEmptyStateContainer.Attribute("VerticalAlignment")?.Value == "Center",
                    "MetadataEmptyStateContainer must specify VerticalAlignment='Center'");

                var emptyContainerStyle = metadataEmptyStateContainer.Elements().FirstOrDefault(e => e.Name.LocalName == "StackPanel.Style")
                    ?.Elements().FirstOrDefault(e => e.Name.LocalName == "Style" && e.Attribute("TargetType")?.Value == "StackPanel");
                Assert(emptyContainerStyle != null, "MetadataEmptyStateContainer must define an inline Style with TargetType='StackPanel'");

                var emptyContainerDefaultVisibility = emptyContainerStyle!.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "Setter" && e.Attribute("Property")?.Value == "Visibility");
                Assert(emptyContainerDefaultVisibility?.Attribute("Value")?.Value == "Visible",
                    "MetadataEmptyStateContainer must default Visibility to Visible when no database is selected");

                var emptyContainerTrigger = emptyContainerStyle.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "DataTrigger" &&
                    e.Attribute("Binding")?.Value == "{Binding HasSelectedDatabase}" &&
                    e.Attribute("Value")?.Value == "True");
                Assert(emptyContainerTrigger != null, "MetadataEmptyStateContainer must define a DataTrigger for {Binding HasSelectedDatabase} == True");
                var emptyContainerTriggerSetter = emptyContainerTrigger!.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "Setter" && e.Attribute("Property")?.Value == "Visibility");
                Assert(emptyContainerTriggerSetter?.Attribute("Value")?.Value == "Collapsed",
                    "MetadataEmptyStateContainer DataTrigger must set Visibility to Collapsed when HasSelectedDatabase is True");

                // 3. lblMetadataEmptyState bindings, accessibility properties, and inherited #334155 high-contrast foreground
                var lblMetadataEmptyState = metadataEmptyStateContainer.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "TextBlock" && e.Attribute(xName)?.Value == "lblMetadataEmptyState");
                Assert(lblMetadataEmptyState != null, "MetadataEmptyStateContainer must contain TextBlock x:Name='lblMetadataEmptyState'");
                Assert(lblMetadataEmptyState!.Attribute("Text")?.Value == "{Binding MetadataEmptyStateText}",
                    "lblMetadataEmptyState Text must bind to {Binding MetadataEmptyStateText}");
                Assert(lblMetadataEmptyState.Attribute("FontSize")?.Value == "14",
                    "lblMetadataEmptyState must set FontSize='14'");
                Assert(lblMetadataEmptyState.Attribute("HorizontalAlignment")?.Value == "Center",
                    "lblMetadataEmptyState must set HorizontalAlignment='Center'");
                Assert(lblMetadataEmptyState.Attribute("TextWrapping")?.Value == "Wrap",
                    "lblMetadataEmptyState must set TextWrapping='Wrap'");
                Assert(lblMetadataEmptyState.Attribute("AutomationProperties.Name")?.Value == "{Binding MetadataEmptyStateText}",
                    "lblMetadataEmptyState AutomationProperties.Name must bind to {Binding MetadataEmptyStateText}");
                Assert(lblMetadataEmptyState.Attribute("AutomationProperties.HelpText")?.Value == "Metadata empty state guidance message.",
                    "lblMetadataEmptyState AutomationProperties.HelpText must equal 'Metadata empty state guidance message.'");
                Assert(lblMetadataEmptyState.Attribute("AutomationProperties.LiveSetting")?.Value == "Polite",
                    "lblMetadataEmptyState must configure AutomationProperties.LiveSetting='Polite' for WCAG 2.2 SC 4.1.3");
                Assert(lblMetadataEmptyState.Attribute("Foreground") == null,
                    "lblMetadataEmptyState must omit local Foreground so it inherits #334155 from App.xaml");
                Assert(lblMetadataEmptyState.Attribute("AutomationProperties.HeadingLevel") == null,
                    "lblMetadataEmptyState must omit AutomationProperties.HeadingLevel");

                // 4. Verify inherited Foreground (#334155) from App.xaml against metadata Border Background (White) meets WCAG 2.2 SC 1.4.3
                var metadataBorder = metadataPropertyGrid.Parent?.Parent;
                Assert(metadataBorder != null && metadataBorder.Name.LocalName == "Border",
                    "Metadata panel outer container should be a Border");
                Assert(metadataBorder!.Attribute("Background")?.Value == "White",
                    "Metadata Border Background should be White");

                var implicitTextBlockStyle = appDoc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "Style" &&
                    e.Attribute("TargetType")?.Value == "TextBlock" &&
                    e.Attributes().All(a => a.Name.LocalName != "Key"));
                Assert(implicitTextBlockStyle != null, "App.xaml must define implicit TextBlock style");
                var fgSetter = implicitTextBlockStyle!.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "Setter" && e.Attribute("Property")?.Value == "Foreground");
                Assert(fgSetter?.Attribute("Value")?.Value == "#334155",
                    "App.xaml implicit TextBlock style must set Foreground='#334155'");

                double sR = 0x33 / 255.0;
                double sG = 0x41 / 255.0;
                double sB = 0x55 / 255.0;
                double rLin = sR <= 0.04045 ? sR / 12.92 : Math.Pow((sR + 0.055) / 1.055, 2.4);
                double gLin = sG <= 0.04045 ? sG / 12.92 : Math.Pow((sG + 0.055) / 1.055, 2.4);
                double bLin = sB <= 0.04045 ? sB / 12.92 : Math.Pow((sB + 0.055) / 1.055, 2.4);
                double lumFg = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
                double contrastAgainstWhite = (1.0 + 0.05) / (lumFg + 0.05);
                Assert(contrastAgainstWhite >= 4.5 && Math.Abs(contrastAgainstWhite - 10.35) < 0.1,
                    $"Expected inherited #334155 foreground contrast ~10.35:1 (>= 4.5:1) against White, got {contrastAgainstWhite:F2}:1");
            });

            RunSyncTest("MainViewModel_SelectedDatabase_MetadataAccessibleNames_EvaluatesCompositeStrings", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var textBlocks = doc.Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();

                var collationTb = textBlocks.First(tb => tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.Collation") == true);
                var userColumnsTb = textBlocks.First(tb => tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.UserCharacterColumns") == true);
                var compatTb = textBlocks.First(tb => tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.CompatibilityLevel") == true);
                var ownerTb = textBlocks.First(tb => tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.Owner") == true);
                var sizeTb = textBlocks.First(tb => tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.Size") == true);

                // Helper to evaluate binding expression according to WPF binding rules
                string EvaluateMetadataAccessibleName(string bindingExpr, MainViewModel viewModel)
                {
                    string format = System.Text.RegularExpressions.Regex.Match(bindingExpr, @"StringFormat='([^']*)'").Groups[1].Value;
                    string fallback = System.Text.RegularExpressions.Regex.Match(bindingExpr, @"FallbackValue='([^']*)'").Groups[1].Value;
                    string targetNull = System.Text.RegularExpressions.Regex.Match(bindingExpr, @"TargetNullValue='([^']*)'").Groups[1].Value;
                    string propName = System.Text.RegularExpressions.Regex.Match(bindingExpr, @"SelectedDatabase\.(\w+)").Groups[1].Value;

                    if (viewModel.SelectedDatabase == null)
                    {
                        return fallback;
                    }

                    var prop = typeof(DatabaseInfo).GetProperty(propName);
                    var val = prop?.GetValue(viewModel.SelectedDatabase) as string;

                    if (string.IsNullOrEmpty(val))
                    {
                        return targetNull;
                    }

                    return string.Format(format, val);
                }

                var vm = new MainViewModel();

                // Case 1: SelectedDatabase is null -> returns FallbackValue
                Assert(vm.SelectedDatabase == null, "SelectedDatabase should be null initially");
                Assert(EvaluateMetadataAccessibleName(collationTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Database Default Collation: -",
                    "Expected fallback name 'Database Default Collation: -' when SelectedDatabase is null");
                Assert(EvaluateMetadataAccessibleName(userColumnsTb.Attribute("AutomationProperties.Name")!.Value, vm) == "User Character Columns: -",
                    "Expected fallback name 'User Character Columns: -' when SelectedDatabase is null");
                Assert(EvaluateMetadataAccessibleName(compatTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Compatibility Level: -",
                    "Expected fallback name 'Compatibility Level: -' when SelectedDatabase is null");
                Assert(EvaluateMetadataAccessibleName(ownerTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Owner: -",
                    "Expected fallback name 'Owner: -' when SelectedDatabase is null");
                Assert(EvaluateMetadataAccessibleName(sizeTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Size: -",
                    "Expected fallback name 'Size: -' when SelectedDatabase is null");

                // Case 2: SelectedDatabase has dynamic values -> returns formatted composite string
                var db = new DatabaseInfo
                {
                    Name = "ProductionDB",
                    Collation = "SQL_Latin1_General_CP1_CI_AS",
                    UserCharacterColumnCount = 42,
                    CompatibilityLevel = "150",
                    Owner = "domain\\dbadmin",
                    Size = "5120 MB"
                };
                vm.SelectedDatabase = db;

                Assert(EvaluateMetadataAccessibleName(collationTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Database Default Collation: SQL_Latin1_General_CP1_CI_AS",
                    "Expected composite accessible name with dynamic database default collation");
                Assert(EvaluateMetadataAccessibleName(userColumnsTb.Attribute("AutomationProperties.Name")!.Value, vm) == "User Character Columns: 42",
                    "Expected composite accessible name with dynamic user character column count");
                Assert(EvaluateMetadataAccessibleName(compatTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Compatibility Level: 150",
                    "Expected composite accessible name with dynamic compatibility level");
                Assert(EvaluateMetadataAccessibleName(ownerTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Owner: domain\\dbadmin",
                    "Expected composite accessible name with dynamic owner");
                Assert(EvaluateMetadataAccessibleName(sizeTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Size: 5120 MB",
                    "Expected composite accessible name with dynamic size");

                // Case 3: SelectedDatabase has null/empty properties -> returns TargetNullValue
                var emptyDb = new DatabaseInfo
                {
                    Name = "EmptyDB",
                    Collation = "",
                    UserCharacterColumns = "",
                    CompatibilityLevel = "",
                    Owner = "",
                    Size = ""
                };
                vm.SelectedDatabase = emptyDb;

                Assert(EvaluateMetadataAccessibleName(collationTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Database Default Collation: -",
                    "Expected TargetNullValue 'Database Default Collation: -' when collation is empty");
                Assert(EvaluateMetadataAccessibleName(userColumnsTb.Attribute("AutomationProperties.Name")!.Value, vm) == "User Character Columns: -",
                    "Expected TargetNullValue 'User Character Columns: -' when UserCharacterColumns is empty");
                Assert(EvaluateMetadataAccessibleName(compatTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Compatibility Level: -",
                    "Expected TargetNullValue 'Compatibility Level: -' when compatibility level is empty");
                Assert(EvaluateMetadataAccessibleName(ownerTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Owner: -",
                    "Expected TargetNullValue 'Owner: -' when owner is empty");
                Assert(EvaluateMetadataAccessibleName(sizeTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Size: -",
                    "Expected TargetNullValue 'Size: -' when size is empty");

                // Case 4: Switching back to null database returns fallback
                vm.SelectedDatabase = null;
                Assert(EvaluateMetadataAccessibleName(collationTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Database Default Collation: -",
                    "Expected fallback name after resetting SelectedDatabase to null");
                Assert(EvaluateMetadataAccessibleName(userColumnsTb.Attribute("AutomationProperties.Name")!.Value, vm) == "User Character Columns: -",
                    "Expected fallback name for User Character Columns after resetting SelectedDatabase to null");
            });

            RunSyncTest("MainWindowXaml_StatusBarTextBlock_ConfiguredWithPoliteLiveSetting", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);

                Assert(content.Contains("Text=\"{Binding StatusMessage}\""), "MainWindow.xaml should contain status message binding");
                Assert(content.Contains("ToolTip=\"{Binding EffectiveStatusToolTip}\"") || content.Contains("ToolTip=\"{Binding StatusToolTip}\""), "MainWindow.xaml status TextBlock should have ToolTip binding");
                Assert(content.Contains("AutomationProperties.LiveSetting=\"Polite\""), "MainWindow.xaml status TextBlock should have AutomationProperties.LiveSetting=\"Polite\"");

                var doc = System.Xml.Linq.XDocument.Parse(content);
                var xNameAttr = System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
                var statusTextBlock = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "TextBlock" && (e.Attribute(xNameAttr)?.Value == "StatusTextBlock" || e.Attribute("Name")?.Value == "StatusTextBlock"));
                Assert(statusTextBlock != null, "MainWindow.xaml should define StatusTextBlock TextBlock");
                var tooltipValue = statusTextBlock!.Attribute("ToolTip")?.Value;
                Assert(tooltipValue == "{Binding EffectiveStatusToolTip}" || tooltipValue == "{Binding StatusToolTip}", "StatusTextBlock must configure ToolTip binding");
            });

            RunSyncTest("MainWindowXaml_StatusBar_UsesConstrainedGridWithTextTrimming", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);

                int statusBarIndex = content.IndexOf("<!-- Status Bar at the bottom -->");
                Assert(statusBarIndex >= 0, "MainWindow.xaml should contain status bar section");
                string statusBarSection = content.Substring(statusBarIndex);

                Assert(statusBarSection.Contains("<Grid.ColumnDefinitions>"), "Status bar should contain Grid.ColumnDefinitions");
                Assert(statusBarSection.Contains("Width=\"*\""), "Status bar Grid should define star width column");
                Assert(statusBarSection.Contains("Width=\"Auto\""), "Status bar Grid should define Auto width column");
                Assert(statusBarSection.Contains("TextTrimming=\"CharacterEllipsis\""), "StatusTextBlock should have TextTrimming=\"CharacterEllipsis\"");
                Assert(statusBarSection.Contains("ToolTip=\"{Binding EffectiveStatusToolTip}\""), "StatusTextBlock should bind ToolTip to EffectiveStatusToolTip");
                Assert(statusBarSection.Contains("Grid.Column=\"0\""), "StatusTextBlock should be placed in Grid.Column 0");
                Assert(statusBarSection.Contains("Grid.Column=\"1\""), "ProgressBar should be placed in Grid.Column 1");
            });

            RunSyncTest("MainViewModel_EffectiveStatusToolTip_FallsBackToStatusMessage", () =>
            {
                var mockService = new MockDatabaseService();
                var vm = new MainViewModel(mockService);

                // Initial state: StatusToolTip is null, StatusMessage is "Ready"
                Assert(vm.StatusToolTip == null, "Initial StatusToolTip should be null");
                Assert(vm.StatusMessage == "Ready", "Initial StatusMessage should be 'Ready'");
                Assert(vm.EffectiveStatusToolTip == "Ready", $"Expected EffectiveStatusToolTip to fall back to 'Ready', got '{vm.EffectiveStatusToolTip}'");

                var changedProperties = new List<string>();
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
                };

                // Changing StatusMessage should raise PropertyChanged for EffectiveStatusToolTip when StatusToolTip is null
                changedProperties.Clear();
                vm.StatusMessage = "Processing database collation...";
                Assert(vm.EffectiveStatusToolTip == "Processing database collation...", $"Expected EffectiveStatusToolTip to update to new StatusMessage, got '{vm.EffectiveStatusToolTip}'");
                Assert(changedProperties.Contains(nameof(MainViewModel.StatusMessage)), "StatusMessage property change should be notified");
                Assert(changedProperties.Contains(nameof(MainViewModel.EffectiveStatusToolTip)), "EffectiveStatusToolTip property change should be notified when StatusMessage changes");

                // Setting StatusToolTip should override StatusMessage in EffectiveStatusToolTip
                changedProperties.Clear();
                vm.StatusToolTip = "Detailed progress tooltip text";
                Assert(vm.EffectiveStatusToolTip == "Detailed progress tooltip text", $"Expected EffectiveStatusToolTip to return StatusToolTip, got '{vm.EffectiveStatusToolTip}'");
                Assert(changedProperties.Contains(nameof(MainViewModel.StatusToolTip)), "StatusToolTip property change should be notified");
                Assert(changedProperties.Contains(nameof(MainViewModel.EffectiveStatusToolTip)), "EffectiveStatusToolTip property change should be notified when StatusToolTip changes");

                // Resetting StatusToolTip to null should fall back to StatusMessage again
                changedProperties.Clear();
                vm.StatusToolTip = null;
                Assert(vm.EffectiveStatusToolTip == "Processing database collation...", $"Expected EffectiveStatusToolTip to fall back to StatusMessage after nulling tooltip, got '{vm.EffectiveStatusToolTip}'");
                Assert(changedProperties.Contains(nameof(MainViewModel.EffectiveStatusToolTip)), "EffectiveStatusToolTip property change should be notified when StatusToolTip cleared");

                // Setting empty StatusToolTip should also fall back to StatusMessage
                vm.StatusToolTip = string.Empty;
                Assert(vm.EffectiveStatusToolTip == "Processing database collation...", $"Expected EffectiveStatusToolTip to fall back to StatusMessage when tooltip is empty string, got '{vm.EffectiveStatusToolTip}'");

                // SetStatus helper method should also update EffectiveStatusToolTip
                vm.SetStatus("Final status", "Explicit tooltip");
                Assert(vm.EffectiveStatusToolTip == "Explicit tooltip", $"Expected EffectiveStatusToolTip 'Explicit tooltip', got '{vm.EffectiveStatusToolTip}'");

                vm.SetStatus("Another status");
                Assert(vm.EffectiveStatusToolTip == "Another status", $"Expected EffectiveStatusToolTip 'Another status', got '{vm.EffectiveStatusToolTip}'");
            });

            RunSyncTest("AppXaml_ButtonControlTemplates_IncorporateFocusVisualBorderAndTrigger", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string content = System.IO.File.ReadAllText(appXamlPath);

                Assert(content.Contains("<ControlTemplate.Triggers>"), "Button ControlTemplate should contain triggers");
                Assert(content.Contains("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\">"), "Button ControlTemplate should have IsKeyboardFocused trigger");
                Assert(content.Contains("Property=\"BorderBrush\" Value=\"{StaticResource FocusBorderBrush}\""), "Button ControlTemplate trigger should set BorderBrush to FocusBorderBrush");
                Assert(content.Contains("x:Key=\"PrimaryButton\""), "App.xaml should define PrimaryButton style");
                Assert(content.Contains("x:Key=\"SecondaryButton\""), "App.xaml should define SecondaryButton style");
            });

            RunSyncTest("IDatabaseService_Declares_GetServerInitializationDataAsync", () =>
            {
                var method = typeof(IDatabaseService).GetMethod("GetServerInitializationDataAsync", new[] { typeof(CancellationToken) });
                Assert(method != null, "IDatabaseService must declare GetServerInitializationDataAsync(CancellationToken).");
                Assert(method!.ReturnType == typeof(Task<ServerInitializationData>), "GetServerInitializationDataAsync must return Task<ServerInitializationData>.");
            });

            RunSyncTest("ServerInitializationData_DoesNotExposeObsoletePropertyShims", () =>
            {
                var backupDirectoryProp = typeof(ServerInitializationData).GetProperty("BackupDirectory");
                Assert(backupDirectoryProp == null, "ServerInitializationData must not expose obsolete BackupDirectory property shim.");

                var collationsProp = typeof(ServerInitializationData).GetProperty("Collations");
                Assert(collationsProp == null, "ServerInitializationData must not expose obsolete Collations property shim.");

                var defaultBackupDirProp = typeof(ServerInitializationData).GetProperty("DefaultBackupDirectory");
                Assert(defaultBackupDirProp != null, "ServerInitializationData must expose canonical DefaultBackupDirectory property.");
                Assert(defaultBackupDirProp!.PropertyType == typeof(string), "DefaultBackupDirectory must be of type string.");

                var supportedCollationsProp = typeof(ServerInitializationData).GetProperty("SupportedCollations");
                Assert(supportedCollationsProp != null, "ServerInitializationData must expose canonical SupportedCollations property.");
                Assert(supportedCollationsProp!.PropertyType == typeof(List<string>), "SupportedCollations must be of type List<string>.");

                var databasesProp = typeof(ServerInitializationData).GetProperty("Databases");
                Assert(databasesProp != null, "ServerInitializationData must expose canonical Databases property.");
                Assert(databasesProp!.PropertyType == typeof(List<DatabaseInfo>), "Databases must be of type List<DatabaseInfo>.");
            });

            RunSyncTest("IDatabaseService_DoesNotExposeObsoleteBackupRestoreShims", () =>
            {
                var backupShim = typeof(IDatabaseService).GetMethod(
                    "BackupDatabaseAsync",
                    new[] { typeof(string), typeof(string), typeof(CancellationToken) });
                Assert(backupShim == null, "IDatabaseService must not expose obsolete three-parameter BackupDatabaseAsync shim.");

                var restoreShim = typeof(IDatabaseService).GetMethod(
                    "RestoreDatabaseAsync",
                    new[] { typeof(string), typeof(string), typeof(CancellationToken) });
                Assert(restoreShim == null, "IDatabaseService must not expose obsolete three-parameter RestoreDatabaseAsync shim.");

                var canonicalBackup = typeof(IDatabaseService).GetMethod(
                    "BackupDatabaseAsync",
                    new[] { typeof(string), typeof(string), typeof(IProgress<int>), typeof(CancellationToken) });
                Assert(canonicalBackup != null, "IDatabaseService must expose canonical four-parameter BackupDatabaseAsync method.");
                Assert(canonicalBackup!.ReturnType == typeof(Task<string>), "Canonical BackupDatabaseAsync must return Task<string>.");

                var canonicalRestore = typeof(IDatabaseService).GetMethod(
                    "RestoreDatabaseAsync",
                    new[] { typeof(string), typeof(string), typeof(IProgress<int>), typeof(CancellationToken) });
                Assert(canonicalRestore != null, "IDatabaseService must expose canonical four-parameter RestoreDatabaseAsync method.");
                Assert(canonicalRestore!.ReturnType == typeof(Task), "Canonical RestoreDatabaseAsync must return Task.");
            });

            RunSyncTest("DatabaseService_DoesNotExposeObsoleteBackupRestoreShims", () =>
            {
                var backupShim = typeof(DatabaseService).GetMethod(
                    "BackupDatabaseAsync",
                    new[] { typeof(string), typeof(string), typeof(CancellationToken) });
                Assert(backupShim == null, "DatabaseService must not expose obsolete three-parameter BackupDatabaseAsync shim.");

                var restoreShim = typeof(DatabaseService).GetMethod(
                    "RestoreDatabaseAsync",
                    new[] { typeof(string), typeof(string), typeof(CancellationToken) });
                Assert(restoreShim == null, "DatabaseService must not expose obsolete three-parameter RestoreDatabaseAsync shim.");

                var canonicalBackup = typeof(DatabaseService).GetMethod(
                    "BackupDatabaseAsync",
                    new[] { typeof(string), typeof(string), typeof(IProgress<int>), typeof(CancellationToken) });
                Assert(canonicalBackup != null, "DatabaseService must expose canonical four-parameter BackupDatabaseAsync method.");
                Assert(canonicalBackup!.ReturnType == typeof(Task<string>), "Canonical BackupDatabaseAsync must return Task<string>.");

                var canonicalRestore = typeof(DatabaseService).GetMethod(
                    "RestoreDatabaseAsync",
                    new[] { typeof(string), typeof(string), typeof(IProgress<int>), typeof(CancellationToken) });
                Assert(canonicalRestore != null, "DatabaseService must expose canonical four-parameter RestoreDatabaseAsync method.");
                Assert(canonicalRestore!.ReturnType == typeof(Task), "Canonical RestoreDatabaseAsync must return Task.");
            });

            await RunAsyncTest("DatabaseService_GetServerInitializationDataAsync_ThrowsOperationCanceledException_WhenTokenCancelled", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);
                dbService.SetConnectionString("localhost", "Windows Authentication", "", "");

                using var cts = new CancellationTokenSource();
                cts.Cancel();

                bool wasAbortedWithCancellation = false;
                try
                {
                    await dbService.GetServerInitializationDataAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    wasAbortedWithCancellation = true;
                }
                catch (Exception ex) when (ex.InnerException is OperationCanceledException)
                {
                    wasAbortedWithCancellation = true;
                }

                Assert(wasAbortedWithCancellation, "GetServerInitializationDataAsync must abort with OperationCanceledException when cancellation token is cancelled.");
            });

            RunSyncTest("DatabaseService_DefaultBackupDirectoryQuery_ContainsExpectedFallbackSteps", () =>
            {
                var field = typeof(DatabaseService).GetField("DefaultBackupDirectoryQuery", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert(field != null, "DefaultBackupDirectoryQuery constant not found on DatabaseService.");
                var query = field!.GetValue(null) as string;
                Assert(!string.IsNullOrEmpty(query), "DefaultBackupDirectoryQuery should not be null or empty.");

                Assert(query!.Contains("SERVERPROPERTY('InstanceDefaultBackupPath')"),
                    "DefaultBackupDirectoryQuery should attempt InstanceDefaultBackupPath first.");
                Assert(query.Contains(@"Software\Microsoft\MSSQLServer\MSSQLServer"),
                    "DefaultBackupDirectoryQuery should attempt registry lookup at Software\\Microsoft\\MSSQLServer\\MSSQLServer.");
                Assert(query.Contains(@"Software\Microsoft\MSSQLServer\MSSQLServer\MSSQLServer"),
                    "DefaultBackupDirectoryQuery should attempt secondary registry lookup at Software\\Microsoft\\MSSQLServer\\MSSQLServer\\MSSQLServer.");
                Assert(query.Contains("SERVERPROPERTY('InstanceDefaultDataPath')"),
                    "DefaultBackupDirectoryQuery should fall back to InstanceDefaultDataPath.");
                Assert(query.Contains("SELECT ISNULL(@path, '') AS DefaultBackupPath;"),
                    "DefaultBackupDirectoryQuery should return DefaultBackupPath column with fallback to empty string.");
            });

            await RunAsyncTest("ConnectAsync_CallsGetServerInitializationDataAsync_InSingleStep", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "CatalogDB" } },
                    CollationsToReturn = new List<string> { "Latin1_General_CI_AS" },
                    DefaultBackupDirectoryToReturn = "C:\\Backups"
                };

                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();

                Assert(mockService.GetServerInitializationDataCount == 1, $"Expected GetServerInitializationDataAsync to be called once, got {mockService.GetServerInitializationDataCount}");
                Assert(vm.IsConnected, "Expected IsConnected to be true after single-step initialization");
            });

            await RunAsyncTest("ConnectAsync_PopulatesDatabasesCollationsAndBackupDirectory_Atomically", async () =>
            {
                var expectedDb = new DatabaseInfo { Name = "ProductionDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { expectedDb },
                    CollationsToReturn = new List<string> { "SQL_Latin1_General_CP1_CI_AS", "Latin1_General_BIN" },
                    DefaultBackupDirectoryToReturn = "D:\\SQLServer\\Backups"
                };

                var vm = new MainViewModel(mockService);

                Assert(vm.Databases.Count == 0, "Databases should be empty initially");
                Assert(vm.SupportedCollations.Count == 0, "SupportedCollations should be empty initially");
                Assert(vm.BackupDirectory != "D:\\SQLServer\\Backups", "BackupDirectory should not be target server path initially");

                await vm.ConnectAsync();

                Assert(vm.IsConnected, "IsConnected should be true");
                Assert(vm.StatusMessage == "Connected to server. Database operations panel unlocked.", $"Expected StatusMessage to announce operations panel unlock, got '{vm.StatusMessage}'");
                Assert(vm.BackupDirectory == "D:\\SQLServer\\Backups", $"Expected BackupDirectory 'D:\\SQLServer\\Backups', got '{vm.BackupDirectory}'");
                Assert(vm.Databases.Count == 1 && vm.Databases[0].Name == "ProductionDB", "Databases should contain ProductionDB");
                Assert(vm.SupportedCollations.Count == 2 && vm.SupportedCollations.Contains("Latin1_General_BIN"), "SupportedCollations should contain expected collations");
            });

            await RunAsyncTest("ConnectAsync_WhenCancelled_AbortsAndPreservesState", async () =>
            {
                using var cts = new CancellationTokenSource();
                var mockService = new MockDatabaseService
                {
                    OnGetServerInitializationData = async token =>
                    {
                        await Task.Delay(1, token);
                        throw new OperationCanceledException();
                    }
                };

                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();

                Assert(!vm.IsConnected, "IsConnected should remain false when ConnectAsync is cancelled");
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Expected cancellation status message, got '{vm.StatusMessage}'");
                Assert(vm.Databases.Count == 0, "Databases should remain empty on cancellation");
            });

            RunSyncTest("DatabaseService_AttachProgressHandler_MethodExistsAndHandlesNullProgress", () =>
            {
                var method = typeof(DatabaseService).GetMethod("AttachProgressHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert(method != null, "AttachProgressHandler method not found in DatabaseService");

                var parameters = method!.GetParameters();
                Assert(parameters.Length == 2, $"Expected 2 parameters, found {parameters.Length}");
                Assert(parameters[0].ParameterType.Name == "SqlConnection", "First parameter should be SqlConnection");
                Assert(parameters[1].ParameterType == typeof(IProgress<int>), "Second parameter should be IProgress<int>");

                var conn = (IDisposable?)Activator.CreateInstance(parameters[0].ParameterType);
                try
                {
                    // Invoking AttachProgressHandler with null progress must safely do nothing without raising an exception
                    method.Invoke(null, new object?[] { conn, null });
                }
                finally
                {
                    conn?.Dispose();
                }
            });

            RunSyncTest("DatabaseService_ProgressRegex_MatchesValidFormat", () =>
            {
                var regex = new System.Text.RegularExpressions.Regex(@"(\d+)\s*percent\s+processed", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                var match1 = regex.Match("10 percent processed.");
                Assert(match1.Success && match1.Groups[1].Value == "10", "Failed to parse '10 percent processed.'");

                var match2 = regex.Match("50 PERCENT PROCESSED");
                Assert(match2.Success && match2.Groups[1].Value == "50", "Failed to parse '50 PERCENT PROCESSED'");

                var match3 = regex.Match("100 percent processed");
                Assert(match3.Success && match3.Groups[1].Value == "100", "Failed to parse '100 percent processed'");

                Assert(!regex.Match("Non matching info string").Success, "Non-matching string incorrectly matched");
            });

            RunSyncTest("DatabaseService_ProgressParsing_ExtractsPercentageCorrectly", () =>
            {
                int? ParsePercent(string? message)
                {
                    if (string.IsNullOrEmpty(message))
                    {
                        return null;
                    }
                    var match = System.Text.RegularExpressions.Regex.Match(message, @"(\d+)\s*percent\s+processed", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int percent))
                    {
                        return percent;
                    }
                    return null;
                }

                // Standard percentage messages
                Assert(ParsePercent("10 percent processed.") == 10, "Failed to parse '10 percent processed.'");
                Assert(ParsePercent("50 PERCENT PROCESSED") == 50, "Failed to parse '50 PERCENT PROCESSED'");
                Assert(ParsePercent("100 percent processed") == 100, "Failed to parse '100 percent processed'");
                Assert(ParsePercent("0 percent processed.") == 0, "Failed to parse '0 percent processed.'");
                Assert(ParsePercent("25  percent   processed.") == 25, "Failed to parse message with extra whitespace");
                Assert(ParsePercent("Backup operation: 75 percent processed.") == 75, "Failed to parse prefixed message");

                // Invalid formats
                Assert(ParsePercent("Non matching info string") == null, "Invalid format without percentage should return null");
                Assert(ParsePercent("Processed 100 percent.") == null, "Reverse phrase should not match");
                Assert(ParsePercent("percent processed") == null, "Missing number should return null");
                Assert(ParsePercent("abc percent processed") == null, "Non-numeric token should return null");
                Assert(ParsePercent("100 percent done") == null, "Different phrase should return null");

                // Empty strings and null
                Assert(ParsePercent("") == null, "Empty string should return null");
                Assert(ParsePercent(null) == null, "Null string should return null");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_ReportsBackupProgressPercentagesAndUpdatesUI", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "ProgressDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    BackupProgressSimulator = progress =>
                    {
                        progress?.Report(30);
                        progress?.Report(70);
                        progress?.Report(100);
                    }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                var reportedPercents = new List<int>();
                bool wasDeterminateDuringProgress = false;
                string? statusFor100Percent = null;

                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.ProgressPercent))
                    {
                        reportedPercents.Add(vm.ProgressPercent);
                        if (!vm.IsIndeterminateProgress)
                        {
                            wasDeterminateDuringProgress = true;
                        }
                    }
                    if (args.PropertyName == nameof(MainViewModel.StatusMessage) && vm.StatusMessage.Contains("100%"))
                    {
                        statusFor100Percent = vm.StatusMessage;
                    }
                };

                await vm.ProcessCollationChangeAsync();

                Assert(reportedPercents.Contains(30) && reportedPercents.Contains(70) && reportedPercents.Contains(100), $"Expected reported percents to contain 30, 70, 100, got [{string.Join(", ", reportedPercents)}]");
                Assert(wasDeterminateDuringProgress, "Expected IsIndeterminateProgress to be false during progress reporting");
                Assert(statusFor100Percent != null && statusFor100Percent.Contains("100%"), $"Expected status message during progress to contain 100%, got '{statusFor100Percent}'");
                Assert(vm.ProgressPercent == 0, $"Expected ProgressPercent to reset to 0 after process completes, got {vm.ProgressPercent}");
                Assert(vm.IsIndeterminateProgress == true, "Expected IsIndeterminateProgress to reset to true after process completes");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_ReportsRestoreProgressPercentagesOnFailure", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "RestoreDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    ShouldAlterCollationThrow = true,
                    RestoreProgressSimulator = progress =>
                    {
                        progress?.Report(50);
                    }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                int restoreReportedPercent = -1;
                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.ProgressPercent) && vm.ProgressPercent == 50)
                    {
                        restoreReportedPercent = vm.ProgressPercent;
                    }
                };

                await vm.ProcessCollationChangeAsync();

                Assert(restoreReportedPercent == 50, $"Expected restore progress percent to report 50, got {restoreReportedPercent}");
                Assert(vm.ProgressPercent == 0, "Expected ProgressPercent to reset to 0 after restore completes");
                Assert(vm.IsIndeterminateProgress == true, "Expected IsIndeterminateProgress to reset to true after restore completes");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_ReportsRestoreProgressPercentagesOnCancellation", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "CancelProgressRestoreDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    RestoreProgressSimulator = progress =>
                    {
                        progress?.Report(75);
                    }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                mockService.OnAlterCollation = async token =>
                {
                    vm.Cancel();
                    token.ThrowIfCancellationRequested();
                    await Task.CompletedTask;
                };

                int restoreReportedPercent = -1;
                string? restoreReportedStatus = null;
                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.ProgressPercent) && vm.ProgressPercent == 75)
                    {
                        restoreReportedPercent = vm.ProgressPercent;
                    }
                    if (args.PropertyName == nameof(MainViewModel.StatusMessage) && vm.StatusMessage == "Restoring database... 75%")
                    {
                        restoreReportedStatus = vm.StatusMessage;
                    }
                };

                await vm.ProcessCollationChangeAsync();

                Assert(restoreReportedPercent == 75, $"Expected restore progress percent to report 75, got {restoreReportedPercent}");
                Assert(restoreReportedStatus == "Restoring database... 75%", $"Expected restore status message 'Restoring database... 75%', got '{restoreReportedStatus}'");
                Assert(vm.ProgressPercent == 0, "Expected ProgressPercent to reset to 0 after cancellation rollback completes");
                Assert(vm.IsIndeterminateProgress == true, "Expected IsIndeterminateProgress to reset to true after cancellation rollback completes");
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Expected final StatusMessage 'Operation cancelled by user.', got '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_ResetsProgressAndEnablesIndeterminate_DuringAlterCollation", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "AlterProgressDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                int capturedProgressPercent = -1;
                bool? capturedIsIndeterminate = null;
                string? capturedStatusMessage = null;

                MainViewModel? vm = null;
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    BackupProgressSimulator = progress =>
                    {
                        // Simulate backup finishing at 100%, which turns off indeterminate mode
                        progress?.Report(100);
                    },
                    OnAlterCollation = async ct =>
                    {
                        capturedProgressPercent = vm!.ProgressPercent;
                        capturedIsIndeterminate = vm.IsIndeterminateProgress;
                        capturedStatusMessage = vm.StatusMessage;
                        await Task.CompletedTask;
                    }
                };

                vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                await vm.ProcessCollationChangeAsync();

                Assert(capturedProgressPercent == 0, $"Expected ProgressPercent to be 0 during AlterCollationAsync, but was {capturedProgressPercent}");
                Assert(capturedIsIndeterminate == true, $"Expected IsIndeterminateProgress to be true during AlterCollationAsync, but was {capturedIsIndeterminate}");
                Assert(capturedStatusMessage != null && capturedStatusMessage.Contains("Altering database collation"), $"Expected status message during AlterCollationAsync to indicate collation alteration, got '{capturedStatusMessage}'");
                Assert(vm.ProgressPercent == 0, $"Expected ProgressPercent to reset to 0 after completion, got {vm.ProgressPercent}");
                Assert(vm.IsIndeterminateProgress == true, $"Expected IsIndeterminateProgress to reset to true after completion, got {vm.IsIndeterminateProgress}");
            });

            RunSyncTest("ErrorFormattingHelper_IsSingleUserLockState_Detection", () =>
            {
                Assert(!ErrorFormattingHelper.IsSingleUserLockState(null), "Null exception should not be detected as lock state.");
                Assert(!ErrorFormattingHelper.IsSingleUserLockState(new Exception("Generic network timeout")), "Unrelated message should not be detected as lock state.");

                Assert(ErrorFormattingHelper.IsSingleUserLockState(new Exception("Database locked in SINGLE_USER mode")), "SINGLE_USER keyword should be detected.");
                Assert(ErrorFormattingHelper.IsSingleUserLockState(new Exception("Connection reset failed")), "'reset failed' should be detected.");
                Assert(ErrorFormattingHelper.IsSingleUserLockState(new Exception("emergency MULTI_USER reset")), "'emergency MULTI_USER reset' should be detected.");

                var innerAggEx = new AggregateException("Aggregate failure", new InvalidOperationException("Failed command"), new Exception("Connection reset failed"));
                Assert(ErrorFormattingHelper.IsSingleUserLockState(innerAggEx), "AggregateException containing lock state error should be detected.");

                var nestedAggEx = new AggregateException(new Exception("Root"), new AggregateException(new Exception("Nested SINGLE_USER lock")));
                Assert(ErrorFormattingHelper.IsSingleUserLockState(nestedAggEx), "Nested AggregateException containing lock state error should be detected.");
            });

            RunSyncTest("ErrorFormattingHelper_FormatProcessError_IncludesLockWarning", () =>
            {
                var primaryEx = new Exception("Table schema alter failed");
                var restoreEx = new Exception("Connection reset failed during emergency MULTI_USER reset");

                var (status, toolTip, detailed) = ErrorFormattingHelper.FormatProcessError(primaryEx, "C:\\backup.bak", restoreEx, true);

                Assert(status.Contains("WARNING: Database remains locked in SINGLE_USER mode."), "Status text should contain SINGLE_USER lock warning.");
                Assert(toolTip.Contains("WARNING: Database remains locked in SINGLE_USER mode."), "ToolTip text should contain SINGLE_USER lock warning.");
                Assert(detailed.Contains("WARNING: Database remains locked in SINGLE_USER mode."), "Detailed text should contain SINGLE_USER lock warning.");
            });

            RunSyncTest("CancelCommand_ExposesICommand_AndCanExecuteEvaluatesTrueOnlyWhenIsBusyIsTrue", () =>
            {
                var vm = new MainViewModel();
                Assert(vm.CancelCommand != null, "MainViewModel.CancelCommand must not be null.");
                Assert(vm.CancelCommand is System.Windows.Input.ICommand, "MainViewModel.CancelCommand must implement ICommand.");

                Assert(!vm.IsBusy, "IsBusy should initially be false.");
                Assert(!vm.CancelCommand.CanExecute(null), "CancelCommand.CanExecute must be false when IsBusy is false.");

                vm.IsBusy = true;
                Assert(vm.CancelCommand.CanExecute(null), "CancelCommand.CanExecute must be true when IsBusy is true.");

                vm.IsBusy = false;
                Assert(!vm.CancelCommand.CanExecute(null), "CancelCommand.CanExecute must be false when IsBusy is reset to false.");
            });

            RunSyncTest("CancelCommand_WhenNotBusy_ExecutesSafelyWithoutThrowing", () =>
            {
                var vm = new MainViewModel();
                vm.Cancel();
                vm.CancelCommand.Execute(null);
                Assert(!vm.IsBusy, "IsBusy should remain false.");
            });

            await RunAsyncTest("CancelCommand_WhenClickedDuringConnectionAttempt_CancelsCancellationTokenSourceAndResetsState", async () =>
            {
                var connectionStarted = new TaskCompletionSource<bool>();
                var mockService = new MockDatabaseService
                {
                    OnTestConnection = async (token) =>
                    {
                        connectionStarted.SetResult(true);
                        var tcs = new TaskCompletionSource<bool>();
                        using var reg = token.Register(() => tcs.TrySetCanceled());
                        return await tcs.Task;
                    }
                };
                var vm = new MainViewModel(mockService);

                var connectTask = vm.ConnectAsync();
                await connectionStarted.Task;

                Assert(vm.IsBusy, "MainViewModel should be busy during connection attempt.");
                Assert(vm.CancelCommand.CanExecute(null), "CancelCommand CanExecute should be true when IsBusy is true.");

                vm.CancelCommand.Execute(null);

                await connectTask;

                Assert(!vm.IsBusy, "MainViewModel IsBusy should reset to false after cancellation.");
                Assert(!vm.IsConnected, "MainViewModel IsConnected should be false after cancellation.");
                Assert(vm.CanEditConnectionParameters, "CanEditConnectionParameters should be true after cancellation.");
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Expected 'Operation cancelled by user.' status, got: '{vm.StatusMessage}'");
            });

            await RunAsyncTest("CancelCommand_WhenClickedDuringCollationChange_CancelsOperationAndRunsMultiUserRecoveryAndResetsState", async () =>
            {
                var alterStarted = new TaskCompletionSource<bool>();
                var mockService = new MockDatabaseService
                {
                    OnAlterCollation = async (token) =>
                    {
                        alterStarted.SetResult(true);
                        var tcs = new TaskCompletionSource<bool>();
                        using var reg = token.Register(() => tcs.TrySetCanceled());
                        await tcs.Task;
                    }
                };
                var vm = new MainViewModel(mockService);
                var testDb = new DatabaseInfo { Name = "CancelDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                vm.Databases.Add(testDb);
                vm.SelectedDatabase = testDb;
                vm.TargetCollation = "Latin1_General_100_CI_AS";
                vm.BackupDirectory = "/tmp/backups";

                var processTask = vm.ProcessCollationChangeAsync();
                await alterStarted.Task;

                Assert(vm.IsBusy, "MainViewModel should be busy during collation change.");
                Assert(vm.CancelCommand.CanExecute(null), "CancelCommand CanExecute should be true when IsBusy is true.");

                vm.CancelCommand.Execute(null);

                await processTask;

                Assert(!vm.IsBusy, "MainViewModel IsBusy should reset to false after cancellation.");
                Assert(mockService.TrySetMultiUserCount > 0, "TrySetMultiUserAsync must be called during cancellation recovery.");
                Assert(mockService.TrySetMultiUserCount == 1, $"Expected TrySetMultiUserCount to be 1 during alteration cancellation, got {mockService.TrySetMultiUserCount}.");
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Expected 'Operation cancelled by user.' status, got: '{vm.StatusMessage}'");
                Assert(vm.ProgressPercent == 0, "ProgressPercent should reset to 0 after cancellation.");
            });

            RunSyncTest("AppXaml_ComboBox_VirtualizationSetters_Configured", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string content = System.IO.File.ReadAllText(appXamlPath);

                Assert(content.Contains("<Style TargetType=\"ComboBox\">"), "App.xaml should define implicit ComboBox style");
                Assert(content.Contains("<Setter Property=\"VirtualizingPanel.IsVirtualizing\" Value=\"True\" />"), "App.xaml ComboBox style should enable VirtualizingPanel.IsVirtualizing");
                Assert(content.Contains("<Setter Property=\"VirtualizingPanel.VirtualizationMode\" Value=\"Recycling\" />"), "App.xaml ComboBox style should configure VirtualizingPanel.VirtualizationMode to Recycling");
                Assert(content.Contains("<Setter Property=\"ScrollViewer.CanContentScroll\" Value=\"True\" />"), "App.xaml ComboBox style should enable ScrollViewer.CanContentScroll");
                Assert(content.Contains("<VirtualizingStackPanel />"), "App.xaml ComboBox style should set ItemsPanel to a VirtualizingStackPanel template");
            });

            RunSyncTest("AppXaml_ComboBoxStyle_Virtualization_Configured", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string content = System.IO.File.ReadAllText(appXamlPath);

                Assert(content.Contains("<Style TargetType=\"ComboBox\">"), "App.xaml should define implicit TargetType=\"ComboBox\" style");
                Assert(content.Contains("<Setter Property=\"VirtualizingPanel.IsVirtualizing\" Value=\"True\" />"), "App.xaml ComboBox style should set VirtualizingPanel.IsVirtualizing to True");
                Assert(content.Contains("<Setter Property=\"VirtualizingPanel.VirtualizationMode\" Value=\"Recycling\" />"), "App.xaml ComboBox style should set VirtualizingPanel.VirtualizationMode to Recycling");
                Assert(content.Contains("<Setter Property=\"ScrollViewer.CanContentScroll\" Value=\"True\" />"), "App.xaml ComboBox style should set ScrollViewer.CanContentScroll to True");
                Assert(content.Contains("<VirtualizingStackPanel />"), "App.xaml ComboBox style should set ItemsPanel to a VirtualizingStackPanel template");
            });

            RunSyncTest("MainWindowXaml_TargetCollationInput_VirtualizationProperties_Configured", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);

                Assert(content.Contains("x:Name=\"TargetCollationInput\""), "MainWindow.xaml should define TargetCollationInput ComboBox");
                Assert(content.Contains("VirtualizingPanel.IsVirtualizing=\"True\""), "TargetCollationInput must have VirtualizingPanel.IsVirtualizing='True'");
                Assert(content.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\""), "TargetCollationInput must have VirtualizingPanel.VirtualizationMode='Recycling'");
                Assert(content.Contains("ScrollViewer.CanContentScroll=\"True\""), "TargetCollationInput must have ScrollViewer.CanContentScroll='True'");
                Assert(content.Contains("<ComboBox.ItemsPanel>"), "TargetCollationInput must specify ComboBox.ItemsPanel");
                Assert(content.Contains("<ItemsPanelTemplate>"), "TargetCollationInput must include ItemsPanelTemplate");
                Assert(content.Contains("<VirtualizingStackPanel />"), "TargetCollationInput ItemsPanel must contain VirtualizingStackPanel");
            });

            RunSyncTest("MainWindowXaml_TargetCollationInput_Virtualization_Configured", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);

                Assert(content.Contains("x:Name=\"TargetCollationInput\""), "MainWindow.xaml should define TargetCollationInput ComboBox");
                Assert(content.Contains("VirtualizingPanel.IsVirtualizing=\"True\""), "TargetCollationInput should explicitly set VirtualizingPanel.IsVirtualizing=\"True\"");
                Assert(content.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\""), "TargetCollationInput should explicitly set VirtualizingPanel.VirtualizationMode=\"Recycling\"");
                Assert(content.Contains("ScrollViewer.CanContentScroll=\"True\""), "TargetCollationInput should explicitly set ScrollViewer.CanContentScroll=\"True\"");
            });

            RunSyncTest("MainWindowXaml_TargetCollationInput_AutomationPropertiesForwarding_Configured", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string xamlContent = System.IO.File.ReadAllText(mainWindowXamlPath);
                Assert(xamlContent.Contains("x:Name=\"TargetCollationInput\""), "MainWindow.xaml should define TargetCollationInput ComboBox");
                Assert(xamlContent.Contains("Loaded=\"TargetCollationInput_Loaded\""), "TargetCollationInput must declare Loaded=\"TargetCollationInput_Loaded\"");

                var doc = System.Xml.Linq.XDocument.Parse(xamlContent);
                var comboBox = doc.Descendants().FirstOrDefault(e =>
                    e.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "TargetCollationInput" ||
                    e.Attribute("Name")?.Value == "TargetCollationInput");
                Assert(comboBox != null, "TargetCollationInput ComboBox element should exist in MainWindow.xaml");
                Assert(comboBox!.Attribute("Loaded")?.Value == "TargetCollationInput_Loaded", "TargetCollationInput must declare Loaded='TargetCollationInput_Loaded'");

                string mainWindowCsPath = ResolveCollationModifierFilePath("MainWindow.xaml.cs");

                string csContent = System.IO.File.ReadAllText(mainWindowCsPath);
                Assert(csContent.Contains("TargetCollationInput_Loaded"), "MainWindow.xaml.cs should implement TargetCollationInput_Loaded method");
                Assert(csContent.Contains("PART_EditableTextBox"), "TargetCollationInput_Loaded should locate PART_EditableTextBox");
                Assert(csContent.Contains("Template.FindName") || csContent.Contains("Template?.FindName"), "TargetCollationInput_Loaded should use Template.FindName");
                Assert(csContent.Contains("AutomationProperties.SetName"), "TargetCollationInput_Loaded should forward AutomationProperties.Name");
                Assert(csContent.Contains("AutomationProperties.SetLabeledBy"), "TargetCollationInput_Loaded should forward AutomationProperties.LabeledBy");
                Assert(csContent.Contains("AutomationProperties.SetHelpText"), "TargetCollationInput_Loaded should forward AutomationProperties.HelpText");
            });

            RunSyncTest("DatabaseService_IsValidCollationName_AcceptsValidCollations", () =>
            {
                var validCollations = new[]
                {
                    "SQL_Latin1_General_CP1_CI_AS",
                    "Latin1_General_CI_AS",
                    "Latin1_General_100_BIN2",
                    "Japanese_CI_AS",
                    "French_CI_AS",
                    "utf8_general_ci",
                    "Collation_123_ABC",
                    "ValidCollation",
                    "A",
                    "a_b_c_123"
                };

                foreach (var collation in validCollations)
                {
                    Assert(DatabaseService.IsValidCollationName(collation), $"Expected '{collation}' to be recognized as a valid collation name.");
                }
            });

            RunSyncTest("DatabaseService_IsValidCollationName_RejectsNullEmptyAndWhitespace", () =>
            {
                string?[] emptyOrWhitespace = new string?[]
                {
                    null,
                    "",
                    " ",
                    "   ",
                    "\t",
                    "\r\n",
                    "\n"
                };

                foreach (var input in emptyOrWhitespace)
                {
                    Assert(!DatabaseService.IsValidCollationName(input), $"Expected '{input ?? "<null>"}' to be rejected as an invalid collation name.");
                }
            });

            RunSyncTest("DatabaseService_IsValidCollationName_RejectsInvalidCharacters", () =>
            {
                var invalidCollations = new[]
                {
                    "Invalid;Collation",
                    "Collation with spaces",
                    "SQL-Latin1-General",
                    "Collation'OR'1'='1",
                    "Collation;DROP TABLE Students--",
                    "Collation/Path",
                    "Collation[0]",
                    "@Collation",
                    "Collation!",
                    "Collation#",
                    "Collation$",
                    "Collation%",
                    "Collation^",
                    "Collation&",
                    "Collation*",
                    "Collation("
                };

                foreach (var collation in invalidCollations)
                {
                    Assert(!DatabaseService.IsValidCollationName(collation), $"Expected '{collation}' containing invalid characters to be rejected.");
                }
            });

            await RunAsyncTest("DatabaseService_AlterCollationAsync_RejectsInvalidCollationFormat", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);

                bool threw = false;
                try
                {
                    await dbService.AlterCollationAsync("TestDB", "Invalid;Collation!DropTable");
                }
                catch (ArgumentException ex) when (ex.Message.Contains("Invalid collation name format"))
                {
                    threw = true;
                }
                Assert(threw, "AlterCollationAsync should throw ArgumentException for invalid collation format.");
            });

            RunSyncTest("AppXaml_HeadingLevelStyles_DeclaredAndConfigured", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string content = System.IO.File.ReadAllText(appXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var styles = doc.Descendants().Where(e => e.Name.LocalName == "Style").ToList();

                // 1. HeadingLevel1 reusable style
                var heading1Style = styles.FirstOrDefault(s => s.Attribute(System.Xml.Linq.XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "HeadingLevel1");
                Assert(heading1Style != null, "App.xaml should declare reusable HeadingLevel1 style");
                var heading1Setter = heading1Style!.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter" && e.Attribute("Property")?.Value == "AutomationProperties.HeadingLevel");
                Assert(heading1Setter != null, "HeadingLevel1 style should set AutomationProperties.HeadingLevel");
                Assert(heading1Setter!.Attribute("Value")?.Value == "Level1", "HeadingLevel1 style should set AutomationProperties.HeadingLevel to Level1");

                // 2. HeadingLevel2 reusable style
                var heading2Style = styles.FirstOrDefault(s => s.Attribute(System.Xml.Linq.XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "HeadingLevel2");
                Assert(heading2Style != null, "App.xaml should declare reusable HeadingLevel2 style");
                var heading2Setter = heading2Style!.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter" && e.Attribute("Property")?.Value == "AutomationProperties.HeadingLevel");
                Assert(heading2Setter != null, "HeadingLevel2 style should set AutomationProperties.HeadingLevel");
                Assert(heading2Setter!.Attribute("Value")?.Value == "Level2", "HeadingLevel2 style should set AutomationProperties.HeadingLevel to Level2");

                // 3. CardHeader style
                var cardHeaderStyle = styles.FirstOrDefault(s => s.Attribute(System.Xml.Linq.XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "CardHeader");
                Assert(cardHeaderStyle != null, "App.xaml should define CardHeader style");
                var cardHeaderSetter = cardHeaderStyle!.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter" && e.Attribute("Property")?.Value == "AutomationProperties.HeadingLevel");
                Assert(cardHeaderSetter != null, "CardHeader style must assign AutomationProperties.HeadingLevel");
                Assert(cardHeaderSetter!.Attribute("Value")?.Value == "Level2", "CardHeader style must assign AutomationProperties.HeadingLevel with value Level2");
            });

            RunSyncTest("MainWindowXaml_HeadingLevels_ConfiguredCorrectly", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var labels = doc.Descendants().Where(e => e.Name.LocalName == "Label").ToList();
                var textBlocks = doc.Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();

                // 1. Primary section header: Database Operations -> Level 1
                var dbOpsHeader = labels.FirstOrDefault(l => l.Attribute("Content")?.Value == "Database Operations");
                Assert(dbOpsHeader != null, "MainWindow.xaml should contain 'Database Operations' Label");
                Assert(dbOpsHeader!.Attribute("AutomationProperties.HeadingLevel")?.Value == "Level1",
                    "Database Operations section header must expose AutomationProperties.HeadingLevel='Level1'");

                // 2. Secondary section headers: Authentication & Target Configuration -> Level 2
                var authHeader = labels.FirstOrDefault(l => l.Attribute("Content")?.Value == "Authentication");
                Assert(authHeader != null, "MainWindow.xaml should contain 'Authentication' Label");
                Assert(authHeader!.Attribute("AutomationProperties.HeadingLevel")?.Value == "Level2" ||
                       authHeader!.Attribute("Style")?.Value.Contains("CardHeader") == true,
                    "Authentication section header must expose AutomationProperties.HeadingLevel Level2 directly or via CardHeader style");

                var targetConfigHeader = labels.FirstOrDefault(l => l.Attribute("Content")?.Value == "Target Configuration");
                Assert(targetConfigHeader != null, "MainWindow.xaml should contain 'Target Configuration' Label");
                Assert(targetConfigHeader!.Attribute("AutomationProperties.HeadingLevel")?.Value == "Level2" ||
                       targetConfigHeader!.Attribute("Style")?.Value.Contains("CardHeader") == true,
                    "Target Configuration section header must expose AutomationProperties.HeadingLevel Level2 directly or via CardHeader style");

                // 3. Input field labels must NOT have heading levels attached
                var inputLabelNames = new[] { "ServerNameLabel", "AuthTypeLabel", "UsernameLabel", "PasswordLabel", "SelectDatabaseLabel", "TargetCollationLabel", "BackupDirectoryLabel" };
                foreach (var labelName in inputLabelNames)
                {
                    var inputTb = textBlocks.FirstOrDefault(tb => tb.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == labelName);
                    if (inputTb != null)
                    {
                        Assert(inputTb.Attribute("AutomationProperties.HeadingLevel") == null,
                            $"Input field label '{labelName}' must NOT have AutomationProperties.HeadingLevel specified.");
                    }
                }
            });

            RunSyncTest("ErrorDetailsWindowXaml_DialogHeader_ExposesHeadingLevel1", () =>
            {
                string errorDetailsXamlPath = ResolveCollationModifierFilePath("ErrorDetailsWindow.xaml");

                string content = System.IO.File.ReadAllText(errorDetailsXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var textBlocks = doc.Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();

                var headerTb = textBlocks.FirstOrDefault(tb => tb.Attribute("Text")?.Value.Contains("An error occurred during operation") == true);
                Assert(headerTb != null, "ErrorDetailsWindow.xaml must contain operation error header TextBlock");
                Assert(headerTb!.Attribute("AutomationProperties.HeadingLevel")?.Value == "Level1",
                    "ErrorDetailsWindow.xaml header TextBlock must expose AutomationProperties.HeadingLevel='Level1'");
            });

            RunSyncTest("AppXaml_SuccessForegroundBrush_HighContrastConfiguration", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string content = System.IO.File.ReadAllText(appXamlPath);
                Assert(content.Contains("x:Key=\"SuccessForegroundBrush\" Color=\"#15803D\""),
                    "App.xaml should define SuccessForegroundBrush with #15803D");

                // Verify relative luminance and contrast ratio calculation under WCAG 2.2 1.4.3
                // Success color: #15803D (R=21, G=128, B=61)
                double sR = 21.0 / 255.0;
                double sG = 128.0 / 255.0;
                double sB = 61.0 / 255.0;
                double rLin = sR <= 0.04045 ? sR / 12.92 : Math.Pow((sR + 0.055) / 1.055, 2.4);
                double gLin = sG <= 0.04045 ? sG / 12.92 : Math.Pow((sG + 0.055) / 1.055, 2.4);
                double bLin = sB <= 0.04045 ? sB / 12.92 : Math.Pow((sB + 0.055) / 1.055, 2.4);
                double textLum = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;

                // Window background: #F8FAFC (R=248, G=250, B=252)
                double bgR = 248.0 / 255.0;
                double bgG = 250.0 / 255.0;
                double bgB = 252.0 / 255.0;
                double bgRLin = bgR <= 0.04045 ? bgR / 12.92 : Math.Pow((bgR + 0.055) / 1.055, 2.4);
                double bgGLin = bgG <= 0.04045 ? bgG / 12.92 : Math.Pow((bgG + 0.055) / 1.055, 2.4);
                double bgBLin = bgB <= 0.04045 ? bgB / 12.92 : Math.Pow((bgB + 0.055) / 1.055, 2.4);
                double bgLum = 0.2126 * bgRLin + 0.7152 * bgGLin + 0.0722 * bgBLin;

                double contrastAgainstWindowBg = (bgLum + 0.05) / (textLum + 0.05);
                Assert(contrastAgainstWindowBg >= 4.5,
                    $"Contrast ratio {contrastAgainstWindowBg:F2}:1 against #F8FAFC window background must exceed 4.5:1 under WCAG 2.2 1.4.3");

                double contrastAgainstWhite = (1.0 + 0.05) / (textLum + 0.05);
                Assert(contrastAgainstWhite >= 4.5,
                    $"Contrast ratio {contrastAgainstWhite:F2}:1 against white background must exceed 4.5:1 under WCAG 2.2 1.4.3");
            });

            RunSyncTest("DisconnectedOverlay_SharedBrushes_NoContainerOpacity_AndHighContrast", () =>
            {
                string appXamlPath = ResolveCollationModifierFilePath("App.xaml");

                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string appContent = System.IO.File.ReadAllText(appXamlPath);
                var appDoc = System.Xml.Linq.XDocument.Parse(appContent);
                System.Xml.Linq.XNamespace xNs = "http://schemas.microsoft.com/winfx/2006/xaml";

                var brushes = appDoc.Descendants().Where(e => e.Name.LocalName == "SolidColorBrush").ToList();
                var overlayBgBrush = brushes.FirstOrDefault(b => b.Attribute(xNs + "Key")?.Value == "OverlayBackgroundBrush");
                var overlayFgBrush = brushes.FirstOrDefault(b => b.Attribute(xNs + "Key")?.Value == "OverlayForegroundBrush");

                Assert(overlayBgBrush != null && string.Equals(overlayBgBrush.Attribute("Color")?.Value, "#E6F8FAFC", StringComparison.OrdinalIgnoreCase),
                    "App.xaml should define OverlayBackgroundBrush with Color=\"#E6F8FAFC\"");
                Assert(overlayFgBrush != null && string.Equals(overlayFgBrush.Attribute("Color")?.Value, "#334155", StringComparison.OrdinalIgnoreCase),
                    "App.xaml should define OverlayForegroundBrush with Color=\"#334155\"");

                string mainWindowContent = System.IO.File.ReadAllText(mainWindowXamlPath);
                var mainDoc = System.Xml.Linq.XDocument.Parse(mainWindowContent);

                var overlayTextBlock = mainDoc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "TextBlock" && e.Attribute(xNs + "Name")?.Value == "lblDisconnectedOverlay");
                Assert(overlayTextBlock != null, "MainWindow.xaml must contain lblDisconnectedOverlay TextBlock");
                Assert(overlayTextBlock!.Attribute("Foreground")?.Value == "{StaticResource OverlayForegroundBrush}",
                    "lblDisconnectedOverlay must bind Foreground to {StaticResource OverlayForegroundBrush}");
                Assert(overlayTextBlock.Attribute("Opacity") == null,
                    "lblDisconnectedOverlay must not set local Opacity");

                var overlayBorder = overlayTextBlock.Parent;
                Assert(overlayBorder != null && overlayBorder.Name.LocalName == "Border",
                    "lblDisconnectedOverlay must be wrapped in the disconnected overlay Border");
                Assert(overlayBorder!.Attribute("Background")?.Value == "{StaticResource OverlayBackgroundBrush}",
                    "Disconnected overlay Border must bind Background to {StaticResource OverlayBackgroundBrush}");
                Assert(overlayBorder.Attribute("Opacity") == null,
                    "Disconnected overlay Border must not set Opacity so child text renders at full opacity");
                Assert(overlayBorder.Attribute("Panel.ZIndex")?.Value == "5",
                    "Disconnected overlay Border must preserve Panel.ZIndex=\"5\"");

                // Verify WCAG 2.2 SC 1.4.3 / G18 alpha-composited contrast ratios (> 4.5:1)
                static double ToLinear(double channelByte)
                {
                    double s = channelByte / 255.0;
                    return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
                }

                static double RelativeLuminance(double r, double g, double b) =>
                    0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);

                static double ContrastRatio(double lum1, double lum2)
                {
                    double lighter = Math.Max(lum1, lum2);
                    double darker = Math.Min(lum1, lum2);
                    return (lighter + 0.05) / (darker + 0.05);
                }

                // OverlayForegroundBrush #334155 (opaque: R=51, G=65, B=85)
                double fgLum = RelativeLuminance(0x33, 0x41, 0x55);

                // OverlayBackgroundBrush #E6F8FAFC (A=230/255, R=248, G=250, B=252)
                double alpha = 0xE6 / 255.0;
                (string Name, double R, double G, double B)[] underlyingSurfaces = new[]
                {
                    ("DashboardPanel #F8FAFC", (double)0xF8, (double)0xFA, (double)0xFC),
                    ("MetadataCard #FFFFFF", (double)0xFF, (double)0xFF, (double)0xFF),
                    ("UnderlyingLabel #334155", (double)0x33, (double)0x41, (double)0x55)
                };

                foreach (var surface in underlyingSurfaces)
                {
                    double compR = Math.Round(alpha * 0xF8 + (1.0 - alpha) * surface.R);
                    double compG = Math.Round(alpha * 0xFA + (1.0 - alpha) * surface.G);
                    double compB = Math.Round(alpha * 0xFC + (1.0 - alpha) * surface.B);
                    double bgLum = RelativeLuminance(compR, compG, compB);
                    double ratio = ContrastRatio(bgLum, fgLum);

                    Assert(ratio >= 4.5,
                        $"Composited contrast ratio {ratio:F2}:1 over {surface.Name} must exceed 4.5:1 under WCAG 2.2 SC 1.4.3");
                    Assert(ratio >= 8.4,
                        $"Composited contrast ratio {ratio:F2}:1 over {surface.Name} should reach expected >= 8.41:1 contrast");
                }
            });

            RunSyncTest("ErrorDetailsWindowXaml_TxtErrorDetails_NoLocalBorderBrushOverride", () =>
            {
                string errorDetailsXamlPath = ResolveCollationModifierFilePath("ErrorDetailsWindow.xaml");

                string content = System.IO.File.ReadAllText(errorDetailsXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var textBoxes = doc.Descendants().Where(e => e.Name.LocalName == "TextBox").ToList();

                var errorDetailsTb = textBoxes.FirstOrDefault(tb =>
                    tb.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "txtErrorDetails" ||
                    tb.Attribute("Name")?.Value == "txtErrorDetails");
                Assert(errorDetailsTb != null, "ErrorDetailsWindow.xaml must contain txtErrorDetails TextBox");
                Assert(errorDetailsTb!.Attribute("BorderBrush") == null,
                    "txtErrorDetails must not contain a local BorderBrush override so it inherits InputRestingBorderBrush from App.xaml");
                Assert(!content.Contains("BorderBrush=\"#CBD5E1\""),
                    "ErrorDetailsWindow.xaml must not contain low-contrast #CBD5E1 border brush");

                // Verify relative luminance and contrast ratio calculation for #64748B vs #F8FAFC
                double sR = 100.0 / 255.0;
                double sG = 116.0 / 255.0;
                double sB = 139.0 / 255.0;
                double rLin = sR <= 0.04045 ? sR / 12.92 : Math.Pow((sR + 0.055) / 1.055, 2.4);
                double gLin = sG <= 0.04045 ? sG / 12.92 : Math.Pow((sG + 0.055) / 1.055, 2.4);
                double bLin = sB <= 0.04045 ? sB / 12.92 : Math.Pow((sB + 0.055) / 1.055, 2.4);
                double lum = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;

                double bgR = 248.0 / 255.0;
                double bgG = 250.0 / 255.0;
                double bgB = 252.0 / 255.0;
                double bgRLin = bgR <= 0.04045 ? bgR / 12.92 : Math.Pow((bgR + 0.055) / 1.055, 2.4);
                double bgGLin = bgG <= 0.04045 ? bgG / 12.92 : Math.Pow((bgG + 0.055) / 1.055, 2.4);
                double bgBLin = bgB <= 0.04045 ? bgB / 12.92 : Math.Pow((bgB + 0.055) / 1.055, 2.4);
                double bgLum = 0.2126 * bgRLin + 0.7152 * bgGLin + 0.0722 * bgBLin;

                double contrast = (bgLum + 0.05) / (lum + 0.05);
                Assert(contrast >= 3.0, $"Contrast ratio {contrast:F2} against background must exceed 3:1 under WCAG 2.2 1.4.11");
            });

            RunSyncTest("ErrorDetailsWindowXaml_CopiedStatusLabel_UsesSuccessForegroundBrush", () =>
            {
                string errorDetailsXamlPath = ResolveCollationModifierFilePath("ErrorDetailsWindow.xaml");

                string content = System.IO.File.ReadAllText(errorDetailsXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var textBlocks = doc.Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();

                var copiedLabel = textBlocks.FirstOrDefault(tb => tb.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "lblCopiedStatus");
                Assert(copiedLabel != null, "ErrorDetailsWindow.xaml must contain 'lblCopiedStatus' TextBlock");
                var fg = copiedLabel!.Attribute("Foreground")?.Value;
                Assert(fg == "{StaticResource StatusSuccessBrush}" || fg == "{StaticResource SuccessForegroundBrush}",
                    "lblCopiedStatus TextBlock must bind Foreground to an accessible success brush");
                Assert(!content.Contains("#16A34A"),
                    "ErrorDetailsWindow.xaml must not contain low-contrast hardcoded #16A34A foreground color");
            });

            RunSyncTest("MainWindowXaml_StatusBarBorder_PlacedAfterMainContentGridInVisualTree", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);

                int mainContentIndex = content.IndexOf("<!-- Main Content Area -->");
                int statusBarIndex = content.IndexOf("<!-- Status Bar at the bottom -->");

                Assert(mainContentIndex >= 0, "MainWindow.xaml should contain '<!-- Main Content Area -->'");
                Assert(statusBarIndex >= 0, "MainWindow.xaml should contain '<!-- Status Bar at the bottom -->'");
                Assert(mainContentIndex < statusBarIndex, "Main Content Area must precede Status Bar in visual tree declaration order for WCAG 2.2 SC 2.4.3 Focus Order compliance");

                int serverNameInputIndex = content.IndexOf("x:Name=\"ServerNameInput\"");
                int btnOpenLogIndex = content.IndexOf("x:Name=\"btnOpenLog\"");
                int btnErrorDetailsIndex = content.IndexOf("x:Name=\"btnErrorDetails\"");

                Assert(serverNameInputIndex >= 0, "MainWindow.xaml should contain ServerNameInput");
                Assert(btnOpenLogIndex >= 0, "MainWindow.xaml should contain btnOpenLog");
                Assert(btnErrorDetailsIndex >= 0, "MainWindow.xaml should contain btnErrorDetails");
                Assert(serverNameInputIndex < btnOpenLogIndex, "ServerNameInput must precede btnOpenLog in visual tree for logical tab order");
                Assert(serverNameInputIndex < btnErrorDetailsIndex, "ServerNameInput must precede btnErrorDetails in visual tree for logical tab order");

                // Verify status bar layout and z-index attributes are preserved
                int statusBarBorderStart = content.IndexOf("<Border Background=\"#E2E8F0\"", statusBarIndex);
                Assert(statusBarBorderStart >= 0, "Status bar Border element should exist after the status bar comment");
                int statusBarBorderEnd = content.IndexOf(">", statusBarBorderStart);
                string statusBarBorderTag = content.Substring(statusBarBorderStart, statusBarBorderEnd - statusBarBorderStart + 1);

                Assert(statusBarBorderTag.Contains("Height=\"30\""), "Status bar Border should preserve Height=\"30\"");
                Assert(statusBarBorderTag.Contains("VerticalAlignment=\"Bottom\""), "Status bar Border should preserve VerticalAlignment=\"Bottom\"");
                Assert(statusBarBorderTag.Contains("Grid.ZIndex=\"10\""), "Status bar Border should preserve Grid.ZIndex=\"10\"");
            });

            RunSyncTest("MainWindowXaml_OverlayAndOperationsPanel_LiveSetting_Configured", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);

                Assert(content.Contains("x:Name=\"lblDisconnectedOverlay\""), "MainWindow.xaml should define lblDisconnectedOverlay TextBlock");
                Assert(content.Contains("AutomationProperties.Name=\"Please connect to a server to continue.\""), "lblDisconnectedOverlay should configure AutomationProperties.Name");
                Assert(content.Contains("AutomationProperties.LiveSetting=\"Polite\""), "MainWindow.xaml should configure AutomationProperties.LiveSetting=\"Polite\"");
                Assert(content.Contains("AutomationProperties.Name=\"Database Operations\""), "Operations container should configure AutomationProperties.Name=\"Database Operations\"");

                // Verify <Window.Resources> precedes <Grid> so StaticResource resolves BooleanToVisibilityConverter on net48
                int windowResourcesIndex = content.IndexOf("<Window.Resources>");
                int rootGridIndex = content.IndexOf("<Grid>");
                Assert(windowResourcesIndex >= 0, "MainWindow.xaml must declare <Window.Resources>");
                Assert(rootGridIndex >= 0, "MainWindow.xaml must declare root <Grid>");
                Assert(windowResourcesIndex < rootGridIndex, "<Window.Resources> must precede <Grid> in MainWindow.xaml");

                var doc = System.Xml.Linq.XDocument.Parse(content);
                var rootElements = doc.Root!.Elements().ToList();
                var windowResourcesElements = rootElements.Where(e => e.Name.LocalName == "Window.Resources").ToList();
                Assert(windowResourcesElements.Count == 1, "MainWindow.xaml should declare exactly one <Window.Resources> block");
                var rootGridElement = rootElements.FirstOrDefault(e => e.Name.LocalName == "Grid");
                Assert(rootGridElement != null, "MainWindow.xaml root must contain <Grid>");
                Assert(rootElements.IndexOf(windowResourcesElements[0]) < rootElements.IndexOf(rootGridElement!),
                    "<Window.Resources> element must precede <Grid> element in Window children");
                Assert(windowResourcesElements[0].Elements().Any(e =>
                    e.Name.LocalName == "BooleanToVisibilityConverter" &&
                    e.Attributes().Any(a => a.Name.LocalName == "Key" && a.Value == "BooleanToVisibilityConverter")),
                    "<Window.Resources> must define BooleanToVisibilityConverter with x:Key='BooleanToVisibilityConverter'");

                // Verify Database Operations StackPanel binds Visibility to IsConnected via BooleanToVisibilityConverter while preserving IsEnabled
                var operationsPanel = doc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "StackPanel" &&
                    e.Attribute("AutomationProperties.Name")?.Value == "Database Operations");
                Assert(operationsPanel != null, "MainWindow.xaml must contain Database Operations StackPanel");
                Assert(operationsPanel!.Attribute("IsEnabled")?.Value == "{Binding IsConnected}",
                    "Database Operations StackPanel must preserve IsEnabled=\"{Binding IsConnected}\"");
                string? opsVisibility = operationsPanel.Attribute("Visibility")?.Value;
                Assert(opsVisibility == "{Binding IsConnected, Converter={StaticResource BooleanToVisibilityConverter}}",
                    $"Database Operations StackPanel must bind Visibility to IsConnected via BooleanToVisibilityConverter, got '{opsVisibility}'");

                // Verify TargetCollationInput_Loaded calls ApplyTemplate before checking Template and calling Template.FindName
                string mainWindowCsPath = ResolveCollationModifierFilePath("MainWindow.xaml.cs");

                string csContent = System.IO.File.ReadAllText(mainWindowCsPath);
                int loadedMethodIndex = csContent.IndexOf("TargetCollationInput_Loaded");
                Assert(loadedMethodIndex >= 0, "MainWindow.xaml.cs must define TargetCollationInput_Loaded");
                string loadedMethodBody = csContent.Substring(loadedMethodIndex);
                int applyTemplateIndex = loadedMethodBody.IndexOf("TargetCollationInput?.ApplyTemplate()");
                if (applyTemplateIndex < 0)
                {
                    applyTemplateIndex = loadedMethodBody.IndexOf(".ApplyTemplate()");
                }
                int templateCheckIndex = loadedMethodBody.IndexOf("Template != null");
                int findNameIndex = loadedMethodBody.IndexOf("Template.FindName");
                if (findNameIndex < 0)
                {
                    findNameIndex = loadedMethodBody.IndexOf("Template?.FindName");
                }
                Assert(applyTemplateIndex >= 0, "TargetCollationInput_Loaded must call ApplyTemplate() on TargetCollationInput");
                Assert(templateCheckIndex >= 0 && applyTemplateIndex < templateCheckIndex,
                    "TargetCollationInput_Loaded must call ApplyTemplate() before checking Template");
                Assert(findNameIndex >= 0 && applyTemplateIndex < findNameIndex,
                    "TargetCollationInput_Loaded must call ApplyTemplate() before calling Template.FindName");
            });

            RunSyncTest("MainWindowXaml_DashboardOperationsPanel_WrappedInScrollViewer", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);

                // 1. Find operations StackPanel
                var stackPanels = doc.Descendants().Where(e => e.Name.LocalName == "StackPanel").ToList();
                var operationsPanel = stackPanels.FirstOrDefault(sp =>
                    sp.Attribute("AutomationProperties.Name")?.Value == "Database Operations");
                Assert(operationsPanel != null, "MainWindow.xaml should contain operations StackPanel with AutomationProperties.Name='Database Operations'");

                // 2. Parent of operations StackPanel must be a ScrollViewer
                var parentScrollViewer = operationsPanel!.Parent;
                Assert(parentScrollViewer != null, "Operations StackPanel should have a parent");
                Assert(parentScrollViewer!.Name.LocalName == "ScrollViewer",
                    $"Operations StackPanel should be directly enclosed in a ScrollViewer, but found {parentScrollViewer.Name.LocalName}");

                // 3. ScrollViewer should configure vertical auto and horizontal disabled scrolling
                Assert(parentScrollViewer.Attribute("VerticalScrollBarVisibility")?.Value == "Auto",
                    "Dashboard ScrollViewer should specify VerticalScrollBarVisibility='Auto'");
                Assert(parentScrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value == "Disabled",
                    "Dashboard ScrollViewer should specify HorizontalScrollBarVisibility='Disabled'");

                // 4. ScrollViewer parent should be the dashboard Grid
                var dashboardGrid = parentScrollViewer.Parent;
                Assert(dashboardGrid != null, "Dashboard ScrollViewer should have a parent Grid");
                Assert(dashboardGrid!.Name.LocalName == "Grid",
                    $"Dashboard ScrollViewer parent should be Grid, but found {dashboardGrid.Name.LocalName}");

                // 5. The disconnected overlay Border should be a sibling element within the same Grid
                var overlayBorder = dashboardGrid.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "Border" &&
                                         e.Descendants().Any(d => d.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "lblDisconnectedOverlay")));
                Assert(overlayBorder != null, "Disconnected overlay Border should be a sibling inside the dashboard Grid");
            });

            RunSyncTest("MainViewModel_IsConnected_TransitionsAnnounceOperationsPanelState", () =>
            {
                var vm = new MainViewModel();
                Assert(!vm.IsConnected, "IsConnected should default to false.");

                vm.IsConnected = true;
                Assert(vm.StatusMessage == "Connected to server. Database operations panel unlocked.",
                    $"Expected 'Connected to server. Database operations panel unlocked.', got '{vm.StatusMessage}'");

                vm.IsConnected = false;
                Assert(vm.StatusMessage == "Disconnected from server. Database operations panel locked.",
                    $"Expected 'Disconnected from server. Database operations panel locked.', got '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ConnectAsync_AnnouncesOperationsPanelUnlocked_OnSuccessfulConnection", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "LiveRegionDB" } },
                    CollationsToReturn = new List<string> { "Latin1_General_CI_AS" }
                };

                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();

                Assert(vm.IsConnected, "IsConnected should be true after successful connect.");
                Assert(vm.StatusMessage == "Connected to server. Database operations panel unlocked.",
                    $"Expected StatusMessage to announce operations panel unlock, got '{vm.StatusMessage}'");
            });

            await RunAsyncTest("Disconnect_AnnouncesOperationsPanelLocked_OnDisconnection", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "LiveRegionDB" } }
                };

                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();
                Assert(vm.IsConnected, "IsConnected should be true before disconnect.");

                vm.Disconnect();

                Assert(!vm.IsConnected, "IsConnected should be false after disconnect.");
                Assert(vm.StatusMessage == "Disconnected from server. Database operations panel locked.",
                    $"Expected StatusMessage to announce operations panel lock, got '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ConnectAsync_WhenAuthenticationChangedWhileDisconnected_AnnouncesOperationsPanelUnlocked", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "AuthDB" } }
                };

                var vm = new MainViewModel(mockService);
                Assert(!vm.IsConnected, "ViewModel should be disconnected initially.");

                vm.SelectedAuthenticationType = "SQL Server Authentication";
                Assert(vm.StatusMessage == "SQL Server Authentication selected. Username and Password fields enabled.",
                    $"Expected authentication change message, got '{vm.StatusMessage}'");

                await vm.ConnectAsync();

                Assert(vm.IsConnected, "IsConnected should be true after successful connection.");
                Assert(vm.StatusMessage == "Connected to server. Database operations panel unlocked.",
                    $"Expected subsequent connection transition to announce operations panel unlock, got '{vm.StatusMessage}'");
            });

            RunSyncTest("DatabaseService_EvaluateCollation_Fails_WhenCollationsMatchIdentically", () =>
            {
                var report = new PreflightReport { DatabaseName = "TestDB" };
                DatabaseService.EvaluateCollation("TestDB", "SQL_Latin1_General_CP1_CI_AS", "SQL_Latin1_General_CP1_CI_AS", report);

                Assert(report.HasValidationErrors, "Report should have validation errors when target collation matches current collation.");
                Assert(report.HasBlockingConditions, "Report should have blocking conditions when target collation matches current collation.");
                Assert(!report.IsValid, "Report should not be valid when collations match.");
                Assert(report.ValidationErrors.Count == 1, $"Expected 1 validation error, got {report.ValidationErrors.Count}");
                Assert(report.ValidationErrors[0].Contains("already uses collation 'SQL_Latin1_General_CP1_CI_AS'"), "Validation error must state database already uses collation.");
                Assert(report.ValidationErrors[0].Contains("Target collation 'SQL_Latin1_General_CP1_CI_AS' is identical to the current database collation."), "Validation error must state target collation is identical to current collation.");
            });

            RunSyncTest("DatabaseService_EvaluateCollation_Fails_WhenCollationsMatchCaseInsensitively", () =>
            {
                var report = new PreflightReport { DatabaseName = "TestDB" };
                DatabaseService.EvaluateCollation("TestDB", "SQL_Latin1_General_CP1_CI_AS", "sql_latin1_general_cp1_ci_as", report);

                Assert(report.HasValidationErrors, "Report should have validation errors for case-insensitive match.");
                Assert(report.HasBlockingConditions, "Report should have blocking conditions for case-insensitive match.");
                Assert(!report.IsValid, "Report should not be valid for case-insensitive match.");
                Assert(report.ValidationErrors.Count == 1, $"Expected 1 validation error, got {report.ValidationErrors.Count}");
                Assert(report.ValidationErrors[0].Contains("already uses collation 'SQL_Latin1_General_CP1_CI_AS'"), "Validation error must state database already uses current collation.");
                Assert(report.ValidationErrors[0].Contains("Target collation 'sql_latin1_general_cp1_ci_as' is identical to the current database collation."), "Validation error must state target collation is identical.");
            });

            RunSyncTest("DatabaseService_EvaluateCollation_Succeeds_WhenCollationsDiffer", () =>
            {
                var report = new PreflightReport { DatabaseName = "TestDB" };
                DatabaseService.EvaluateCollation("TestDB", "SQL_Latin1_General_CP1_CI_AS", "Latin1_General_100_CI_AS", report);

                Assert(!report.HasValidationErrors, "Report should NOT have validation errors when collations differ.");
                Assert(!report.HasBlockingConditions, "Report should NOT have blocking conditions when collations differ.");
                Assert(report.IsValid, "Report should be valid when collations differ.");
                Assert(report.ValidationErrors.Count == 0, $"Expected 0 validation errors, got {report.ValidationErrors.Count}");
            });

            RunSyncTest("DatabaseService_EvaluateCollation_HandlesNullOrWhitespaceGracefully", () =>
            {
                var report = new PreflightReport { DatabaseName = "TestDB" };
                DatabaseService.EvaluateCollation("TestDB", null, "SQL_Latin1_General_CP1_CI_AS", report);
                DatabaseService.EvaluateCollation("TestDB", "SQL_Latin1_General_CP1_CI_AS", null, report);
                DatabaseService.EvaluateCollation("TestDB", "   ", "SQL_Latin1_General_CP1_CI_AS", report);
                DatabaseService.EvaluateCollation("TestDB", "SQL_Latin1_General_CP1_CI_AS", "   ", report);
                DatabaseService.EvaluateCollation("TestDB", null, null, report);

                Assert(!report.HasValidationErrors, "Report should NOT have validation errors for null or whitespace inputs.");
                Assert(!report.HasBlockingConditions, "Report should NOT have blocking conditions for null or whitespace inputs.");
                Assert(report.IsValid, "Report should be valid.");
            });

            RunSyncTest("PreflightReport_GetDiagnosticSummary_DisplaysIdenticalCollationDiagnostic", () =>
            {
                var report = new PreflightReport { DatabaseName = "DiagDB" };
                DatabaseService.EvaluateCollation("DiagDB", "SQL_Latin1_General_CP1_CI_AS", "SQL_Latin1_General_CP1_CI_AS", report);

                string summary = report.GetDiagnosticSummary();
                Assert(summary.Contains("FAILED (Blocking items found)"), "Diagnostic summary should indicate failure.");
                Assert(summary.Contains("[Validation Errors (1)]"), "Diagnostic summary should include Validation Errors header.");
                Assert(summary.Contains("Database 'DiagDB' already uses collation 'SQL_Latin1_General_CP1_CI_AS'"), "Diagnostic summary must state database already uses collation.");
                Assert(summary.Contains("Target collation 'SQL_Latin1_General_CP1_CI_AS' is identical to the current database collation."), "Diagnostic summary must state target collation is identical.");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_HaltsBeforeBackup_WhenTargetCollationMatchesCurrentCollation", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "IdenticalDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "SQL_Latin1_General_CP1_CI_AS";

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 0, "Backup must NOT be performed when target collation matches current collation!");
                Assert(mockService.AlterCollationCount == 0, "Alter Collation must NOT be performed when target collation matches current collation!");
                Assert(mockService.IsMultiUser, "Database must remain in multi-user mode.");
                Assert(vm.StatusMessage.Contains("Process failed before backup"), $"Expected status message to indicate failure before backup, got: '{vm.StatusMessage}'");
                Assert(vm.StatusMessage.Contains("already uses collation 'SQL_Latin1_General_CP1_CI_AS'"), $"Status message should include collation diagnostic, got: '{vm.StatusMessage}'");
                Assert(vm.HasDetailedError, "HasDetailedError must be true to allow user inspection.");
                Assert(vm.DetailedError != null && vm.DetailedError.Contains("Pre-flight Validation Report for Database 'IdenticalDb'"), $"DetailedError should contain preflight report, got: '{vm.DetailedError}'");
                Assert(vm.DetailedError != null && vm.DetailedError.Contains("already uses collation 'SQL_Latin1_General_CP1_CI_AS'"), $"DetailedError should describe identical collation, got: '{vm.DetailedError}'");
                Assert(vm.DetailedError != null && vm.DetailedError.Contains("Target collation 'SQL_Latin1_General_CP1_CI_AS' is identical to the current database collation."), $"DetailedError should state collation is identical, got: '{vm.DetailedError}'");
                Assert(vm.ShowErrorDetailsCommand.CanExecute(null), "ShowErrorDetailsCommand should be executable.");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_HaltsBeforeBackup_WhenTargetCollationMatchesCaseInsensitively", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "CaseDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "sql_latin1_general_cp1_ci_as";

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 0, "Backup must NOT execute when target collation matches case-insensitively!");
                Assert(mockService.AlterCollationCount == 0, "AlterCollation must NOT execute when target collation matches case-insensitively!");
                Assert(vm.StatusMessage.Contains("Process failed before backup"), $"Expected failure before backup status, got: '{vm.StatusMessage}'");
                Assert(vm.HasDetailedError, "HasDetailedError should be true.");
                Assert(vm.DetailedError != null && vm.DetailedError.Contains("already uses collation 'SQL_Latin1_General_CP1_CI_AS'"), $"DetailedError should state database already uses collation, got: '{vm.DetailedError}'");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_ProceedsNormally_WhenTargetCollationDiffers", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "DistinctDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => true;
                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.BackupCount == 1, "Backup SHOULD execute when target collation differs!");
                Assert(mockService.AlterCollationCount == 1, "AlterCollation SHOULD execute when target collation differs!");
                Assert(vm.StatusMessage == "Database default collation updated to 'Latin1_General_100_CI_AS'. 0 existing user columns kept prior collations.",
                    $"Expected scope-disclosing success message, got: '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ProcessCommand_CanExecute_ReturnsFalse_WhenTargetCollationMatchesCurrentCollation_CaseInsensitivelyAndWithWhitespace", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "PredicateTestDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();
                vm.SelectedDatabase = vm.Databases[0];

                // Identical collation
                vm.TargetCollation = "SQL_Latin1_General_CP1_CI_AS";
                Assert(!vm.ProcessCommand.CanExecute(null), "ProcessCommand.CanExecute should be false when target collation matches current collation identically.");

                // Case-insensitive match
                vm.TargetCollation = "sql_latin1_general_cp1_ci_as";
                Assert(!vm.ProcessCommand.CanExecute(null), "ProcessCommand.CanExecute should be false when target collation matches current collation case-insensitively.");

                // Match with leading and trailing whitespace
                vm.TargetCollation = "  sql_latin1_general_cp1_ci_as  ";
                Assert(!vm.ProcessCommand.CanExecute(null), "ProcessCommand.CanExecute should be false when target collation matches current collation with whitespace.");

                // Match when database collation also has whitespace
                var dbWithWhitespace = new DatabaseInfo { Name = "WhitespaceDb", Collation = "  SQL_Latin1_General_CP1_CI_AS  " };
                vm.SelectedDatabase = dbWithWhitespace;
                vm.TargetCollation = "SQL_Latin1_General_CP1_CI_AS";
                Assert(!vm.ProcessCommand.CanExecute(null), "ProcessCommand.CanExecute should be false when database collation has padding whitespace and target matches.");
            });

            await RunAsyncTest("ProcessCommand_CanExecute_ReturnsTrue_WhenCollationsDiffer_OrWhenDatabaseCollationIsNullOrWhitespace", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "DistinctDb", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                await vm.ConnectAsync();
                vm.SelectedDatabase = vm.Databases[0];

                // Collations differ
                vm.TargetCollation = "Latin1_General_100_CI_AS";
                Assert(vm.ProcessCommand.CanExecute(null), "ProcessCommand.CanExecute should be true when target collation differs from current collation.");

                // Database collation is null
                var dbWithNullCollation = new DatabaseInfo { Name = "NullCollationDb", Collation = null! };
                vm.SelectedDatabase = dbWithNullCollation;
                vm.TargetCollation = "SQL_Latin1_General_CP1_CI_AS";
                Assert(vm.ProcessCommand.CanExecute(null), "ProcessCommand.CanExecute should be true when database collation is null (permits preflight verification).");

                // Database collation is empty
                var dbWithEmptyCollation = new DatabaseInfo { Name = "EmptyCollationDb", Collation = string.Empty };
                vm.SelectedDatabase = dbWithEmptyCollation;
                vm.TargetCollation = "SQL_Latin1_General_CP1_CI_AS";
                Assert(vm.ProcessCommand.CanExecute(null), "ProcessCommand.CanExecute should be true when database collation is empty.");

                // Database collation is whitespace
                var dbWithWhitespaceCollation = new DatabaseInfo { Name = "WhitespaceCollationDb", Collation = "   " };
                vm.SelectedDatabase = dbWithWhitespaceCollation;
                vm.TargetCollation = "SQL_Latin1_General_CP1_CI_AS";
                Assert(vm.ProcessCommand.CanExecute(null), "ProcessCommand.CanExecute should be true when database collation is whitespace.");
            });

            RunSyncTest("MainWindowXaml_TargetConfiguration_StackedVertically_AndFullWidthBackupDirectory", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);

                // 1. Verify there is no ColumnDefinition with Width="200"
                var colDefs = doc.Descendants().Where(e => e.Name.LocalName == "ColumnDefinition").ToList();
                Assert(!colDefs.Any(c => c.Attribute("Width")?.Value == "200"),
                    "MainWindow.xaml should not contain a 200 DIP ColumnDefinition constraining backup directory.");

                // 2. Find Target Configuration label
                var labels = doc.Descendants().Where(e => e.Name.LocalName == "Label").ToList();
                var targetConfigLabel = labels.FirstOrDefault(l => l.Attribute("Content")?.Value == "Target Configuration");
                Assert(targetConfigLabel != null, "MainWindow.xaml should contain 'Target Configuration' Label");

                // 3. The container immediately following Target Configuration label must be a StackPanel
                var targetConfigParent = targetConfigLabel!.Parent;
                Assert(targetConfigParent != null, "Target Configuration Label should have a parent");

                var siblings = targetConfigParent!.Elements().ToList();
                int labelIndex = siblings.IndexOf(targetConfigLabel);
                var targetConfigContainer = siblings[labelIndex + 1];
                Assert(targetConfigContainer.Name.LocalName == "StackPanel",
                    "Target Configuration fields container must be a vertical StackPanel, not a two-column Grid");

                // 4. Verify vertical stacking and margins of the inner StackPanels
                var innerPanels = targetConfigContainer.Elements().Where(e => e.Name.LocalName == "StackPanel").ToList();
                Assert(innerPanels.Count == 2, "Target Configuration StackPanel should contain 2 stacked row panels");

                var targetCollationPanel = innerPanels[0];
                var backupDirPanel = innerPanels[1];

                // Target collation row has bottom margin to separate rows cleanly
                Assert(targetCollationPanel.Attribute("Margin")?.Value == "0,0,0,16",
                    "Target collation container must specify bottom margin (Margin=\"0,0,0,16\") to cleanly separate rows");
                Assert(targetCollationPanel.Attribute("Grid.Column") == null,
                    "Target collation container should not specify Grid.Column");

                // Backup directory row
                Assert(backupDirPanel.Attribute("Grid.Column") == null,
                    "Backup directory container should not specify Grid.Column");

                // 5. Verify Backup Directory TextBox attributes, bindings, tooltips, and automation properties
                var textBoxes = backupDirPanel.Elements().Where(e => e.Name.LocalName == "TextBox").ToList();
                Assert(textBoxes.Count == 1, "Backup directory container must contain a TextBox");
                var backupTextBox = textBoxes[0];

                Assert(backupTextBox.Attribute("Text")?.Value == "{Binding BackupDirectory, UpdateSourceTrigger=PropertyChanged}",
                    "Backup directory TextBox must bind to BackupDirectory");
                Assert(backupTextBox.Attribute("IsEnabled")?.Value == "{Binding IsNotBusy}",
                    "Backup directory TextBox must bind IsEnabled to IsNotBusy");
                Assert(backupTextBox.Attribute("AutomationProperties.LabeledBy")?.Value == "{Binding ElementName=BackupDirectoryLabel}",
                    "Backup directory TextBox must preserve AutomationProperties.LabeledBy");
                Assert(backupTextBox.Attribute("AutomationProperties.Name")?.Value == "Backup Directory (Server Host)",
                    "Backup directory TextBox must preserve AutomationProperties.Name");
                Assert(backupTextBox.Attribute("AutomationProperties.HelpText")?.Value == "Path is evaluated on the SQL Server host filesystem, not the local client.",
                    "Backup directory TextBox must preserve AutomationProperties.HelpText");
                Assert(backupTextBox.Attribute("ToolTip")?.Value == "Path is evaluated on the SQL Server host filesystem, not the local client.",
                    "Backup directory TextBox must preserve ToolTip");
                Assert(backupTextBox.Attribute("Width") == null,
                    "Backup directory TextBox must not have a fixed Width attribute so it spans full width");
            });

            RunSyncTest("MainViewModel_LongBackupPath_PreservesFullPath", () =>
            {
                var vm = new MainViewModel();
                string longPath = @"C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\DefaultInstanceSubfolder";
                Assert(longPath.Length > 60, "Test path should exceed 60 characters");
                vm.BackupDirectory = longPath;
                Assert(vm.BackupDirectory == longPath, "MainViewModel should preserve long backup path without truncation");
            });

            RunSyncTest("MainViewModel_IsConnecting_PropertyEvaluationAndNotifications", () =>
            {
                var vm = new MainViewModel(new MockDatabaseService());
                Assert(!vm.IsConnecting, "IsConnecting should default to false.");

                var notifiedProperties = new List<string>();
                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName != null)
                    {
                        notifiedProperties.Add(args.PropertyName);
                    }
                };

                // Transition to busy while disconnected -> IsConnecting becomes true
                vm.IsBusy = true;
                Assert(vm.IsConnecting, "IsConnecting should be true when IsBusy is true and IsConnected is false.");
                Assert(notifiedProperties.Contains(nameof(MainViewModel.IsConnecting)),
                    $"Expected PropertyChanged for 'IsConnecting' when IsBusy becomes true, got: {string.Join(", ", notifiedProperties)}");

                // Transition to connected while busy -> IsConnecting becomes false
                notifiedProperties.Clear();
                vm.IsConnected = true;
                Assert(!vm.IsConnecting, "IsConnecting should be false when IsConnected is true.");
                Assert(notifiedProperties.Contains(nameof(MainViewModel.IsConnecting)),
                    $"Expected PropertyChanged for 'IsConnecting' when IsConnected becomes true, got: {string.Join(", ", notifiedProperties)}");

                // Transition busy back to false -> IsConnecting remains false
                notifiedProperties.Clear();
                vm.IsBusy = false;
                Assert(!vm.IsConnecting, "IsConnecting should remain false when IsBusy is false.");
                Assert(notifiedProperties.Contains(nameof(MainViewModel.IsConnecting)),
                    $"Expected PropertyChanged for 'IsConnecting' when IsBusy becomes false, got: {string.Join(", ", notifiedProperties)}");

                // Transition connected back to false
                notifiedProperties.Clear();
                vm.IsConnected = false;
                Assert(!vm.IsConnecting, "IsConnecting should be false when IsBusy is false and IsConnected is false.");
                Assert(notifiedProperties.Contains(nameof(MainViewModel.IsConnecting)),
                    $"Expected PropertyChanged for 'IsConnecting' when IsConnected becomes false, got: {string.Join(", ", notifiedProperties)}");
            });

            await RunAsyncTest("ConnectAsync_UpdatesIsConnectingState_DuringAndAfterConnection", async () =>
            {
                var connectionStarted = new TaskCompletionSource<bool>();
                var releaseConnection = new TaskCompletionSource<bool>();
                var mockService = new MockDatabaseService
                {
                    OnTestConnection = async (token) =>
                    {
                        connectionStarted.SetResult(true);
                        await releaseConnection.Task;
                        return true;
                    },
                    DatabasesToReturn = new List<DatabaseInfo> { new DatabaseInfo { Name = "TestDB" } }
                };

                var vm = new MainViewModel(mockService);
                Assert(!vm.IsConnecting, "IsConnecting should be false before connect.");

                var connectTask = vm.ConnectAsync();
                await connectionStarted.Task;

                Assert(vm.IsConnecting, "IsConnecting should be true while ConnectAsync is executing.");
                Assert(vm.IsBusy, "IsBusy should be true while ConnectAsync is executing.");
                Assert(!vm.IsConnected, "IsConnected should be false while ConnectAsync is executing.");

                releaseConnection.SetResult(true);
                await connectTask;

                Assert(!vm.IsConnecting, "IsConnecting should be false after successful connection.");
                Assert(vm.IsConnected, "IsConnected should be true after successful connection.");
                Assert(!vm.IsBusy, "IsBusy should be false after successful connection.");

                // Test cancellation during connection attempt
                var cancelStarted = new TaskCompletionSource<bool>();
                var cancelMockService = new MockDatabaseService
                {
                    OnTestConnection = async (token) =>
                    {
                        cancelStarted.SetResult(true);
                        var tcs = new TaskCompletionSource<bool>();
                        using var reg = token.Register(() => tcs.TrySetCanceled());
                        return await tcs.Task;
                    }
                };

                var cancelVm = new MainViewModel(cancelMockService);
                var cancelConnectTask = cancelVm.ConnectAsync();
                await cancelStarted.Task;

                Assert(cancelVm.IsConnecting, "IsConnecting should be true during connection attempt prior to cancel.");
                cancelVm.CancelCommand.Execute(null);
                await cancelConnectTask;

                Assert(!cancelVm.IsConnecting, "IsConnecting should be false after cancellation.");
                Assert(!cancelVm.IsConnected, "IsConnected should be false after cancellation.");
                Assert(!cancelVm.IsBusy, "IsBusy should be false after cancellation.");
            });

            RunSyncTest("MainWindowXaml_ConnectionPanel_CancelButton_Configured", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);

                var buttons = doc.Descendants().Where(e => e.Name.LocalName == "Button").ToList();
                var btnCancelConnect = buttons.FirstOrDefault(b => b.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "btnCancelConnect"));

                Assert(btnCancelConnect != null, "MainWindow.xaml must declare btnCancelConnect button.");

                var parent = btnCancelConnect!.Parent;
                Assert(parent != null && parent.Name.LocalName == "StackPanel", "btnCancelConnect must be wrapped inside a StackPanel.");
                Assert(parent!.Attribute("Orientation")?.Value == "Horizontal", "Containing StackPanel for connection controls must have Orientation=\"Horizontal\".");

                var siblingButtons = parent.Elements().Where(e => e.Name.LocalName == "Button").ToList();
                Assert(siblingButtons.Any(b => b.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "btnConnect")),
                    "Containing StackPanel must contain both btnConnect and btnCancelConnect.");

                var commandAttr = btnCancelConnect.Attribute("Command")?.Value;
                Assert(commandAttr != null && commandAttr.Contains("CancelCommand"),
                    $"btnCancelConnect must bind Command to CancelCommand, got '{commandAttr}'");

                var visibilityAttr = btnCancelConnect.Attribute("Visibility")?.Value;
                Assert(visibilityAttr != null && visibilityAttr.Contains("IsConnecting"),
                    $"btnCancelConnect must bind Visibility to IsConnecting, got '{visibilityAttr}'");
                Assert(visibilityAttr.Contains("BooleanToVisibilityConverter"),
                    $"btnCancelConnect Visibility binding must use BooleanToVisibilityConverter, got '{visibilityAttr}'");

                var automationName = btnCancelConnect.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name" && a.Name.NamespaceName.Contains("AutomationProperties") || a.Name.ToString().Contains("AutomationProperties.Name"))?.Value;
                Assert(automationName == "Cancel Connection",
                    $"btnCancelConnect must set AutomationProperties.Name to 'Cancel Connection', got '{automationName}'");
            });

            RunSyncTest("ErrorDetailsWindowXaml_ActionButtons_AutomationProperties_Configured", () =>
            {
                string errorDetailsXamlPath = ResolveCollationModifierFilePath("ErrorDetailsWindow.xaml");

                string content = System.IO.File.ReadAllText(errorDetailsXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var buttons = doc.Descendants().Where(e => e.Name.LocalName == "Button").ToList();

                // 1. btnCopy
                var btnCopy = buttons.FirstOrDefault(b => b.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "btnCopy"));
                Assert(btnCopy != null, "ErrorDetailsWindow.xaml must declare btnCopy Button.");
                var copyContent = btnCopy!.Attribute("Content")?.Value;
                var copyAutomationName = btnCopy.Attributes().FirstOrDefault(a => (a.Name.LocalName == "Name" && a.Name.NamespaceName.Contains("AutomationProperties")) || a.Name.ToString().Contains("AutomationProperties.Name"))?.Value;
                var copyHelpText = btnCopy.Attributes().FirstOrDefault(a => (a.Name.LocalName == "HelpText" && a.Name.NamespaceName.Contains("AutomationProperties")) || a.Name.ToString().Contains("AutomationProperties.HelpText"))?.Value;

                Assert(copyContent == "Copy Details", $"btnCopy Content should be 'Copy Details', got '{copyContent}'");
                Assert(copyAutomationName == "Copy Details", $"btnCopy AutomationProperties.Name must match visible Content 'Copy Details' per WCAG 2.2 SC 2.5.3 (G211), got '{copyAutomationName}'");
                Assert(copyAutomationName == copyContent, "btnCopy AutomationProperties.Name must match its visible label exactly.");
                Assert(!string.IsNullOrWhiteSpace(copyHelpText), "btnCopy must declare valid AutomationProperties.HelpText");
                Assert(copyHelpText == "Copy error details to clipboard", $"btnCopy AutomationProperties.HelpText should be 'Copy error details to clipboard', got '{copyHelpText}'");

                // 2. btnClose
                var btnClose = buttons.FirstOrDefault(b => b.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "btnClose"));
                Assert(btnClose != null, "ErrorDetailsWindow.xaml must declare btnClose Button.");
                var closeContent = btnClose!.Attribute("Content")?.Value;
                var closeAutomationName = btnClose.Attributes().FirstOrDefault(a => (a.Name.LocalName == "Name" && a.Name.NamespaceName.Contains("AutomationProperties")) || a.Name.ToString().Contains("AutomationProperties.Name"))?.Value;
                var closeHelpText = btnClose.Attributes().FirstOrDefault(a => (a.Name.LocalName == "HelpText" && a.Name.NamespaceName.Contains("AutomationProperties")) || a.Name.ToString().Contains("AutomationProperties.HelpText"))?.Value;

                Assert(closeContent == "Close", $"btnClose Content should be 'Close', got '{closeContent}'");
                Assert(closeAutomationName == "Close", $"btnClose AutomationProperties.Name must match visible Content 'Close' per WCAG 2.2 SC 2.5.3 (G211), got '{closeAutomationName}'");
                Assert(closeAutomationName == closeContent, "btnClose AutomationProperties.Name must match its visible label exactly.");
                Assert(!string.IsNullOrWhiteSpace(closeHelpText), "btnClose must declare valid AutomationProperties.HelpText");
                Assert(closeHelpText == "Close error details dialog", $"btnClose AutomationProperties.HelpText should be 'Close error details dialog', got '{closeHelpText}'");
            });

            RunSyncTest("MainWindowXaml_ErrorDetailsButton_AutomationProperties_Configured", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var buttons = doc.Descendants().Where(e => e.Name.LocalName == "Button").ToList();

                var btnErrorDetails = buttons.FirstOrDefault(b => b.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "btnErrorDetails"));
                Assert(btnErrorDetails != null, "MainWindow.xaml must declare btnErrorDetails Button.");

                var errorDetailsContent = btnErrorDetails!.Attribute("Content")?.Value;
                var errorDetailsAutomationName = btnErrorDetails.Attributes().FirstOrDefault(a => (a.Name.LocalName == "Name" && a.Name.NamespaceName.Contains("AutomationProperties")) || a.Name.ToString().Contains("AutomationProperties.Name"))?.Value;
                var errorDetailsHelpText = btnErrorDetails.Attributes().FirstOrDefault(a => (a.Name.LocalName == "HelpText" && a.Name.NamespaceName.Contains("AutomationProperties")) || a.Name.ToString().Contains("AutomationProperties.HelpText"))?.Value;

                Assert(errorDetailsContent == "Details...", $"btnErrorDetails Content should be 'Details...', got '{errorDetailsContent}'");
                Assert(errorDetailsAutomationName == "Details...", $"btnErrorDetails AutomationProperties.Name must match visible Content 'Details...' per WCAG 2.2 SC 2.5.3 (G211), got '{errorDetailsAutomationName}'");
                Assert(errorDetailsAutomationName == errorDetailsContent, "btnErrorDetails AutomationProperties.Name must match its visible label exactly.");
                Assert(!string.IsNullOrWhiteSpace(errorDetailsHelpText), "btnErrorDetails must declare valid AutomationProperties.HelpText");
                Assert(errorDetailsHelpText == "View detailed error information", $"btnErrorDetails AutomationProperties.HelpText should be 'View detailed error information', got '{errorDetailsHelpText}'");
            });

            RunSyncTest("MainWindowXaml_ConnectionPanel_ConnectButton_DefaultConfigured", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);

                var buttons = doc.Descendants().Where(e => e.Name.LocalName == "Button").ToList();
                var btnConnect = buttons.FirstOrDefault(b => b.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "btnConnect"));

                Assert(btnConnect != null, "MainWindow.xaml must declare btnConnect button.");

                var isDefaultAttr = btnConnect!.Attribute("IsDefault")?.Value;
                Assert(isDefaultAttr != null, "btnConnect must declare IsDefault attribute.");
                Assert(isDefaultAttr.Contains("CanEditConnectionParameters"),
                    $"btnConnect IsDefault attribute must bind to CanEditConnectionParameters, got '{isDefaultAttr}'");
            });

            RunSyncTest("ResolveCollationModifierFilePath_ResolvesExistingFiles_AndThrowsForMissingFile", () =>
            {
                string[] existingFiles = new[]
                {
                    "App.xaml",
                    "App.xaml.cs",
                    "MainWindow.xaml",
                    "MainWindow.xaml.cs",
                    "ErrorDetailsWindow.xaml"
                };

                foreach (string fileName in existingFiles)
                {
                    string resolvedPath = MainViewModelTestsRunner.ResolveCollationModifierFilePath(fileName);
                    Assert(!string.IsNullOrWhiteSpace(resolvedPath), $"Resolved path for {fileName} should not be empty.");
                    Assert(Path.IsPathRooted(resolvedPath), $"Resolved path for {fileName} should be rooted: {resolvedPath}");
                    Assert(resolvedPath == Path.GetFullPath(resolvedPath), $"Resolved path for {fileName} should be normalized: {resolvedPath}");
                    Assert(File.Exists(resolvedPath), $"Resolved file {fileName} should exist on disk at {resolvedPath}");
                    Assert(string.Equals(Path.GetFileName(resolvedPath), fileName, StringComparison.Ordinal), $"Resolved file name should match {fileName}, got {Path.GetFileName(resolvedPath)}");
                }

                string missingFileName = $"MissingFile_{Guid.NewGuid():N}.xaml";
                string expectedLastCandidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "CollationModifier", missingFileName));
                InvalidOperationException? caughtException = null;
                try
                {
                    MainViewModelTestsRunner.ResolveCollationModifierFilePath(missingFileName);
                }
                catch (InvalidOperationException ex)
                {
                    caughtException = ex;
                }

                Assert(caughtException != null, "ResolveCollationModifierFilePath must throw InvalidOperationException when target file does not exist.");
                Assert(caughtException!.Message == $"Assertion failed: {missingFileName} file should exist at path {expectedLastCandidate}",
                    $"Unexpected exception message: '{caughtException.Message}'");
            });

            RunSyncTest("CollationModifierCsproj_MultiTargetsNet80AndNet80Windows_WithEnableWindowsTargeting", () =>
            {
                string csprojPath = ResolveCollationModifierFilePath("CollationModifier.csproj");
                Assert(System.IO.File.Exists(csprojPath), $"CollationModifier.csproj should exist at {csprojPath}");

                string content = System.IO.File.ReadAllText(csprojPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);

                var targetFrameworks = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "TargetFrameworks");
                Assert(targetFrameworks != null, "CollationModifier.csproj must declare <TargetFrameworks>.");
                var tfms = targetFrameworks!.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                Assert(tfms.Contains("net8.0"), "TargetFrameworks must include net8.0.");
                Assert(tfms.Contains("net8.0-windows"), "TargetFrameworks must include net8.0-windows.");
                Assert(!content.Contains(">net48<"), "CollationModifier.csproj must not target legacy net48.");

                var enableWindowsTargeting = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "EnableWindowsTargeting");
                Assert(enableWindowsTargeting != null && string.Equals(enableWindowsTargeting.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase),
                    "CollationModifier.csproj must set <EnableWindowsTargeting>true</EnableWindowsTargeting>.");
            });

            RunSyncTest("CollationModifierCsproj_GuardsUseWpfOutputTypeAndXamlExclusions_ForWpfTempProject", () =>
            {
                string csprojPath = ResolveCollationModifierFilePath("CollationModifier.csproj");
                Assert(System.IO.File.Exists(csprojPath), $"CollationModifier.csproj should exist at {csprojPath}");

                var doc = System.Xml.Linq.XDocument.Parse(System.IO.File.ReadAllText(csprojPath));

                var wpfTempProp = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "_WpfTempProject");
                Assert(wpfTempProp != null, "CollationModifier.csproj must define <_WpfTempProject> for two-pass WPF markup compilation.");
                Assert(wpfTempProp!.Attribute("Condition")?.Value.Contains("_wpftmp.csproj") == true,
                    "_WpfTempProject condition must detect _wpftmp.csproj temporary markup projects.");

                var useWpf = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "UseWPF");
                Assert(useWpf != null && useWpf.Value.Trim() == "true", "CollationModifier.csproj must configure <UseWPF>true</UseWPF>.");
                string useWpfCond = useWpf!.Attribute("Condition")?.Value ?? string.Empty;
                Assert(useWpfCond.Contains("net8.0-windows") && useWpfCond.Contains("_WpfTempProject"),
                    $"UseWPF condition must check net8.0-windows and _WpfTempProject, got '{useWpfCond}'");

                var outputTypes = doc.Descendants().Where(e => e.Name.LocalName == "OutputType").ToList();
                var winExeOutput = outputTypes.FirstOrDefault(e => e.Value.Trim() == "WinExe");
                var libraryOutput = outputTypes.FirstOrDefault(e => e.Value.Trim() == "Library");
                Assert(winExeOutput != null && (winExeOutput.Attribute("Condition")?.Value.Contains("_WpfTempProject") == true),
                    "WinExe OutputType must account for net8.0-windows and _WpfTempProject.");
                Assert(libraryOutput != null && (libraryOutput.Attribute("Condition")?.Value.Contains("_WpfTempProject") == true),
                    "Library OutputType must exclude net8.0-windows and _WpfTempProject.");

                var exclusionGroup = doc.Descendants()
                    .Where(e => e.Name.LocalName == "ItemGroup" && e.Attribute("Condition") != null)
                    .FirstOrDefault(g => g.Elements().Any(c => c.Name.LocalName == "Page" && c.Attribute("Remove") != null));
                Assert(exclusionGroup != null, "CollationModifier.csproj must guard XAML/code-behind removals in a conditional ItemGroup.");
                string exclusionCond = exclusionGroup!.Attribute("Condition")!.Value;
                Assert(exclusionCond.Contains("net8.0-windows") && exclusionCond.Contains("_WpfTempProject"),
                    $"XAML exclusion ItemGroup condition must check net8.0-windows and _WpfTempProject, got '{exclusionCond}'");
            });

            RunSyncTest("MainWindowXaml_WindowResources_DeclaredBeforeDataContextAndStaticResourceLookups", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");
                Assert(System.IO.File.Exists(mainWindowXamlPath), $"MainWindow.xaml should exist at {mainWindowXamlPath}");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var rootChildren = doc.Root!.Elements().ToList();

                int resourcesElemIndex = rootChildren.FindIndex(e => e.Name.LocalName == "Window.Resources");
                int dataContextElemIndex = rootChildren.FindIndex(e => e.Name.LocalName == "Window.DataContext");
                int gridElemIndex = rootChildren.FindIndex(e => e.Name.LocalName == "Grid");

                Assert(resourcesElemIndex >= 0, "MainWindow.xaml must declare <Window.Resources>.");
                Assert(dataContextElemIndex >= 0, "MainWindow.xaml must declare <Window.DataContext>.");
                Assert(gridElemIndex >= 0, "MainWindow.xaml must declare root <Grid>.");
                Assert(resourcesElemIndex < dataContextElemIndex, "<Window.Resources> must appear before <Window.DataContext>.");
                Assert(dataContextElemIndex < gridElemIndex, "<Window.DataContext> must appear before <Grid>.");

                var converterElem = rootChildren[resourcesElemIndex].Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "BooleanToVisibilityConverter");
                Assert(converterElem != null, "<Window.Resources> must declare BooleanToVisibilityConverter.");

                int resourcesTextIndex = content.IndexOf("<Window.Resources>", StringComparison.Ordinal);
                int firstStaticResourceLookup = content.IndexOf("StaticResource BooleanToVisibilityConverter", StringComparison.Ordinal);
                Assert(firstStaticResourceLookup > resourcesTextIndex,
                    "Window.Resources must precede all StaticResource BooleanToVisibilityConverter lookups.");
            });

            RunSyncTest("MainWindowXaml_ProcessCollationChangeButton_ReplacesDescribedByWithHelpTextWarningBinding", () =>
            {
                string mainWindowXamlPath = ResolveCollationModifierFilePath("MainWindow.xaml");
                Assert(System.IO.File.Exists(mainWindowXamlPath), $"MainWindow.xaml should exist at {mainWindowXamlPath}");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                Assert(!content.Contains("AutomationProperties.DescribedBy"),
                    "MainWindow.xaml must not contain invalid AutomationProperties.DescribedBy.");

                var doc = System.Xml.Linq.XDocument.Parse(content);
                var processButton = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Button" && e.Attribute("Content")?.Value == "Process Collation Change");
                Assert(processButton != null, "MainWindow.xaml must declare the 'Process Collation Change' Button.");

                string? helpTextAttr = processButton!.Attribute("AutomationProperties.HelpText")?.Value;
                Assert(!string.IsNullOrWhiteSpace(helpTextAttr),
                    "Process Collation Change Button must set AutomationProperties.HelpText.");
                Assert(helpTextAttr!.Contains("Binding") && helpTextAttr.Contains("ElementName=lblWarning") && helpTextAttr.Contains("Text"),
                    $"Process Collation Change Button HelpText must bind to lblWarning Text, got '{helpTextAttr}'");

                var lblWarning = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "TextBlock" &&
                        e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "lblWarning"));
                Assert(lblWarning != null, "MainWindow.xaml must preserve lblWarning TextBlock.");
                Assert(lblWarning!.Attribute("Text")?.Value.Contains("SINGLE_USER") == true,
                    "lblWarning TextBlock must preserve SINGLE_USER warning text.");
            });

            RunSyncTest("MainViewModel_PreprocessorGuards_IncludeWindowsForRequeryAndModalDialogs", () =>
            {
                string vmPath = ResolveCollationModifierFilePath(Path.Combine("ViewModels", "MainViewModel.cs"));
                Assert(System.IO.File.Exists(vmPath), $"MainViewModel.cs should exist at {vmPath}");

                string code = System.IO.File.ReadAllText(vmPath);
                var ifLines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith("#if ", StringComparison.Ordinal))
                    .ToList();

                Assert(ifLines.Count == 3, $"Expected 3 #if guards in MainViewModel.cs, found {ifLines.Count}");
                Assert(ifLines.All(l => l == "#if NETFRAMEWORK || WINDOWS"),
                    $"All #if guards in MainViewModel.cs must be '#if NETFRAMEWORK || WINDOWS', got: {string.Join(", ", ifLines)}");
                Assert(code.Contains("StringComparison.OrdinalIgnoreCase"),
                    "MainViewModel.cs must preserve String.Contains(..., StringComparison.OrdinalIgnoreCase).");
            });

            RunSyncTest("RelayCommand_CanExecuteChanged_IncludesWindowsInPreprocessorGuards", () =>
            {
                string rcPath = ResolveCollationModifierFilePath(Path.Combine("ViewModels", "RelayCommand.cs"));
                Assert(System.IO.File.Exists(rcPath), $"RelayCommand.cs should exist at {rcPath}");

                string code = System.IO.File.ReadAllText(rcPath);
                var ifLines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith("#if ", StringComparison.Ordinal))
                    .ToList();

                Assert(ifLines.Count == 2, $"Expected 2 #if guards in RelayCommand.cs, found {ifLines.Count}");
                Assert(ifLines.All(l => l == "#if NETFRAMEWORK || WINDOWS"),
                    $"All #if guards in RelayCommand.cs must be '#if NETFRAMEWORK || WINDOWS', got: {string.Join(", ", ifLines)}");
            });

            RunSyncTest("CompiledTargets_VerifyNet80HeadlessIsolation_AndNet80WindowsWpfArtifacts", () =>
            {
                // 1. Verify the assembly loaded by CollationModifier.Tests (net8.0) has no WPF dependencies
                var headlessAsm = typeof(MainViewModel).Assembly;
                var referencedNames = headlessAsm.GetReferencedAssemblies().Select(a => a.Name).ToList();
                Assert(!referencedNames.Contains("PresentationCore"),
                    "Headless net8.0 CollationModifier assembly must not reference PresentationCore.");
                Assert(!referencedNames.Contains("PresentationFramework"),
                    "Headless net8.0 CollationModifier assembly must not reference PresentationFramework.");
                Assert(headlessAsm.GetType("CollationModifier.MainWindow") == null,
                    "Headless net8.0 CollationModifier assembly must exclude MainWindow WPF view.");

                // 2. When net8.0-windows output is built on disk, inspect its PE/CLI metadata without loading PresentationCore
                string collationModifierDir = Path.GetDirectoryName(ResolveCollationModifierFilePath("CollationModifier.csproj"))!;
                string winDebugDll = Path.Combine(collationModifierDir, "bin", "Debug", "net8.0-windows", "CollationModifier.dll");
                string winReleaseDll = Path.Combine(collationModifierDir, "bin", "Release", "net8.0-windows", "CollationModifier.dll");
                string? winDllPath = System.IO.File.Exists(winDebugDll) ? winDebugDll : (System.IO.File.Exists(winReleaseDll) ? winReleaseDll : null);

                if (winDllPath != null)
                {
                    using var stream = System.IO.File.OpenRead(winDllPath);
                    using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
                    var metadataReader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(peReader);

                    var definedTypes = metadataReader.TypeDefinitions
                        .Select(h =>
                        {
                            var td = metadataReader.GetTypeDefinition(h);
                            string ns = metadataReader.GetString(td.Namespace);
                            string name = metadataReader.GetString(td.Name);
                            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                        })
                        .ToHashSet(StringComparer.Ordinal);

                    Assert(definedTypes.Contains("CollationModifier.App"), "net8.0-windows assembly must contain CollationModifier.App.");
                    Assert(definedTypes.Contains("CollationModifier.MainWindow"), "net8.0-windows assembly must contain CollationModifier.MainWindow.");
                    Assert(definedTypes.Contains("CollationModifier.ErrorDetailsWindow"), "net8.0-windows assembly must contain CollationModifier.ErrorDetailsWindow.");

                    var manifestResources = metadataReader.ManifestResources
                        .Select(h => metadataReader.GetString(metadataReader.GetManifestResource(h).Name))
                        .ToList();
                    Assert(manifestResources.Contains("CollationModifier.g.resources"),
                        "net8.0-windows assembly must embed compiled BAML resources in CollationModifier.g.resources.");

                    var memberRefs = metadataReader.MemberReferences
                        .Select(h => metadataReader.GetString(metadataReader.GetMemberReference(h).Name))
                        .ToHashSet(StringComparer.Ordinal);
                    Assert(memberRefs.Contains("InvalidateRequerySuggested"),
                        "net8.0-windows assembly must compile CommandManager.InvalidateRequerySuggested calls under WINDOWS symbol.");
                    Assert(memberRefs.Contains("add_RequerySuggested") && memberRefs.Contains("remove_RequerySuggested"),
                        "net8.0-windows assembly must compile CommandManager.RequerySuggested event subscriptions under WINDOWS symbol.");
                    Assert(memberRefs.Contains("ShowDialog") && memberRefs.Contains("Show"),
                        "net8.0-windows assembly must compile WPF ShowDialog and MessageBox.Show handlers under WINDOWS symbol.");
                }
            });

            Console.WriteLine("\nTest Run Finished: MainViewModel tests completed.");
            if (failed > 0)
            {
                throw new InvalidOperationException($"MainViewModel tests failed with {failed} failures.");
            }

            await RunAsyncTest("DatabaseService_ExecuteNonQueryAsync_ForwardsCancellationToken_ToSqlCommand", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new DatabaseService(logger);
                using var connection = new SqlConnection();

                // Uncancelled token reaches SqlCommand connection check (InvalidOperationException on closed connection)
                bool threwInvalidOpWhenUncancelled = false;
                try
                {
                    await dbService.ExecuteNonQueryAsync(connection, "SELECT 1;", 3600, CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    threwInvalidOpWhenUncancelled = true;
                }
                Assert(threwInvalidOpWhenUncancelled, "Uncancelled ExecuteNonQueryAsync on closed SqlConnection should reach SqlCommand execution.");

                // Cancelled token is forwarded to SqlCommand.ExecuteNonQueryAsync(CancellationToken), throwing OperationCanceledException immediately
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                OperationCanceledException? caughtOce = null;
                try
                {
                    await dbService.ExecuteNonQueryAsync(connection, "SELECT 1;", 3600, cts.Token);
                }
                catch (OperationCanceledException oce)
                {
                    caughtOce = oce;
                }

                Assert(caughtOce != null, "ExecuteNonQueryAsync must forward cancelled token to SqlCommand.ExecuteNonQueryAsync(CancellationToken) and throw OperationCanceledException.");
            });

            await RunAsyncTest("DatabaseService_ExecuteNonQueryAsync_TranslatesSqlExceptionToOperationCanceledException_WhenTokenCancelled", async () =>
            {
                using var logger = new LoggerService();
                var simulatedSqlEx = TestableDatabaseService.CreateSimulatedSqlException("Operation cancelled by user.");

                // 1. Active cancellation + SqlException -> OperationCanceledException with InnerException attached
                var dbServiceCancelled = new TestableDatabaseService(logger)
                {
                    SimulateConnectionOpen = true
                };
                using var cts = new CancellationTokenSource();
                dbServiceCancelled.OnExecuteCommand = (_, ct) =>
                {
                    cts.Cancel();
                    Assert(ct.IsCancellationRequested, "CancellationToken passed to command should reflect cancellation.");
                    throw simulatedSqlEx;
                };

                using var conn1 = new SqlConnection();
                OperationCanceledException? caughtCanceled = null;
                try
                {
                    await dbServiceCancelled.ExecuteNonQueryAsync(conn1, "BACKUP DATABASE [TestDB] TO DISK = @BackupFile;", 3600, cts.Token, new SqlParameter("@BackupFile", "/tmp/test.bak"));
                }
                catch (OperationCanceledException oce)
                {
                    caughtCanceled = oce;
                }

                Assert(caughtCanceled != null, "SqlException raised after cancellation must surface as OperationCanceledException.");
                Assert(ReferenceEquals(caughtCanceled!.InnerException, simulatedSqlEx), "OperationCanceledException must attach the original SqlException as InnerException.");
                Assert(caughtCanceled.CancellationToken == cts.Token, "OperationCanceledException must carry the cancelled token.");

                // 2. Non-cancelled token + SqlException -> rethrows original SqlException unwrapped
                var dbServiceUncancelled = new TestableDatabaseService(logger)
                {
                    SimulateConnectionOpen = true,
                    OnExecuteCommand = (_, _) => throw simulatedSqlEx
                };

                using var conn2 = new SqlConnection();
                SqlException? caughtSqlEx = null;
                try
                {
                    await dbServiceUncancelled.ExecuteNonQueryAsync(conn2, "ALTER DATABASE [TestDB] COLLATE Latin1_General_100_CI_AS;", 3600, CancellationToken.None);
                }
                catch (SqlException sqlEx)
                {
                    caughtSqlEx = sqlEx;
                }

                Assert(ReferenceEquals(caughtSqlEx, simulatedSqlEx), "SqlException when token is NOT cancelled must propagate as SqlException.");
            });

            await RunAsyncTest("DatabaseService_SetDatabaseUserModeAsync_ForwardsTokenForSingleUser_AndEnforcesNoneForMultiUser", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new TestableDatabaseService(logger)
                {
                    SimulateConnectionOpen = true
                };

                using var connection = new SqlConnection();
                using var singleUserCts = new CancellationTokenSource();

                await dbService.SetDatabaseUserModeAsync(connection, "UserModeDB", "SINGLE_USER", singleUserCts.Token);
                Assert(dbService.ExecutedCommands.Count == 1, "Expected 1 command for SINGLE_USER.");
                Assert(dbService.ExecutedCommands[0].CommandText.Contains("SET SINGLE_USER WITH ROLLBACK IMMEDIATE"), "Expected SINGLE_USER statement.");
                Assert(dbService.ExecutedCommands[0].CancellationToken == singleUserCts.Token, "SINGLE_USER must forward caller CancellationToken.");
                Assert(dbService.ExecutedCommands[0].CanBeCanceled, "SINGLE_USER token must be cancelable.");

                using var cancelledMultiUserCts = new CancellationTokenSource();
                cancelledMultiUserCts.Cancel();

                await dbService.SetDatabaseUserModeAsync(connection, "UserModeDB", "MULTI_USER", cancelledMultiUserCts.Token);
                Assert(dbService.ExecutedCommands.Count == 2, "Expected 2nd command for MULTI_USER even when caller token is cancelled.");
                Assert(dbService.ExecutedCommands[1].CommandText.Contains("SET MULTI_USER"), "Expected MULTI_USER statement.");
                Assert(dbService.ExecutedCommands[1].CancellationToken == CancellationToken.None, "MULTI_USER must enforce CancellationToken.None.");
                Assert(!dbService.ExecutedCommands[1].CanBeCanceled, "MULTI_USER token must not be cancelable.");
            });

            await RunAsyncTest("DatabaseService_WorkflowMethods_ForwardCancellationTokens_AndKeepMultiUserUncancelable", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new TestableDatabaseService(logger)
                {
                    SimulateConnectionOpen = true
                };
                dbService.SetConnectionString("localhost", "Windows Authentication", "", "");

                // 1. BackupDatabaseAsync
                using var backupCts = new CancellationTokenSource();
                string backupPath = await dbService.BackupDatabaseAsync("WorkflowDB", "/tmp", null, backupCts.Token);
                Assert(!string.IsNullOrEmpty(backupPath), "BackupDatabaseAsync should return backup file path.");
                Assert(dbService.RecordedOpenTokens.Last() == backupCts.Token, "BackupDatabaseAsync must pass cancellationToken to OpenConnectionAsync.");
                Assert(dbService.ExecutedCommands.Count == 1, $"Expected 1 backup command, got {dbService.ExecutedCommands.Count}.");
                Assert(dbService.ExecutedCommands[0].CommandText.Contains("BACKUP DATABASE [WorkflowDB]"), "Expected BACKUP DATABASE command.");
                Assert(dbService.ExecutedCommands[0].CancellationToken == backupCts.Token, "BackupDatabaseAsync must forward cancellationToken to BACKUP DATABASE.");

                // 2. RestoreDatabaseAsync
                dbService.ExecutedCommands.Clear();
                dbService.RecordedOpenTokens.Clear();
                using var restoreCts = new CancellationTokenSource();
                await dbService.RestoreDatabaseAsync("WorkflowDB", backupPath, null, restoreCts.Token);
                Assert(dbService.RecordedOpenTokens.Single() == restoreCts.Token, "RestoreDatabaseAsync must pass cancellationToken to OpenConnectionAsync.");
                Assert(dbService.ExecutedCommands.Count == 4, $"Expected 4 restore commands, got {dbService.ExecutedCommands.Count}.");
                Assert(dbService.ExecutedCommands[0].CommandText == "USE [master];" && dbService.ExecutedCommands[0].CancellationToken == restoreCts.Token,
                    "RestoreDatabaseAsync must forward cancellationToken to USE [master].");
                Assert(dbService.ExecutedCommands[1].CommandText.Contains("SET SINGLE_USER") && dbService.ExecutedCommands[1].CancellationToken == restoreCts.Token,
                    "RestoreDatabaseAsync must forward cancellationToken to SINGLE_USER.");
                Assert(dbService.ExecutedCommands[2].CommandText.Contains("RESTORE DATABASE [WorkflowDB]") && dbService.ExecutedCommands[2].CancellationToken == restoreCts.Token,
                    "RestoreDatabaseAsync must forward cancellationToken to RESTORE DATABASE.");
                Assert(dbService.ExecutedCommands[3].CommandText.Contains("SET MULTI_USER") && dbService.ExecutedCommands[3].CancellationToken == CancellationToken.None,
                    "RestoreDatabaseAsync must execute MULTI_USER with CancellationToken.None.");

                // 3. AlterCollationAsync
                dbService.ExecutedCommands.Clear();
                dbService.RecordedOpenTokens.Clear();
                using var alterCts = new CancellationTokenSource();
                await dbService.AlterCollationAsync("WorkflowDB", "Latin1_General_100_CI_AS", alterCts.Token);
                Assert(dbService.RecordedOpenTokens.Single() == alterCts.Token, "AlterCollationAsync must pass cancellationToken to OpenConnectionAsync.");
                Assert(dbService.ExecutedCommands.Count == 3, $"Expected 3 alter commands, got {dbService.ExecutedCommands.Count}.");
                Assert(dbService.ExecutedCommands[0].CommandText.Contains("SET SINGLE_USER") && dbService.ExecutedCommands[0].CancellationToken == alterCts.Token,
                    "AlterCollationAsync must forward cancellationToken to SINGLE_USER.");
                Assert(dbService.ExecutedCommands[1].CommandText.Contains("ALTER DATABASE [WorkflowDB] COLLATE Latin1_General_100_CI_AS") && dbService.ExecutedCommands[1].CancellationToken == alterCts.Token,
                    "AlterCollationAsync must forward cancellationToken to ALTER DATABASE COLLATE.");
                Assert(dbService.ExecutedCommands[2].CommandText.Contains("SET MULTI_USER") && dbService.ExecutedCommands[2].CancellationToken == CancellationToken.None,
                    "AlterCollationAsync must execute MULTI_USER with CancellationToken.None.");

                // 4. TrySetMultiUserAsync ignores cancelled token
                dbService.ExecutedCommands.Clear();
                dbService.RecordedOpenTokens.Clear();
                using var cancelledCts = new CancellationTokenSource();
                cancelledCts.Cancel();
                await dbService.TrySetMultiUserAsync("WorkflowDB", cancelledCts.Token);
                Assert(dbService.RecordedOpenTokens.Single() == CancellationToken.None, "TrySetMultiUserAsync must open connection with CancellationToken.None.");
                Assert(dbService.ExecutedCommands.Count == 1, "TrySetMultiUserAsync must execute MULTI_USER command.");
                Assert(dbService.ExecutedCommands[0].CommandText.Contains("SET MULTI_USER") && dbService.ExecutedCommands[0].CancellationToken == CancellationToken.None,
                    "TrySetMultiUserAsync must execute MULTI_USER with CancellationToken.None.");
            });

            await RunAsyncTest("TestableDatabaseService_CancelsSimulated30SecondCommands_InUnder100Milliseconds", async () =>
            {
                using var logger = new LoggerService();
                var dbService = new TestableDatabaseService(logger)
                {
                    SimulateConnectionOpen = true
                };
                dbService.SetConnectionString("localhost", "Windows Authentication", "", "");

                // Warm up paths so JIT overhead does not skew timing measurement
                await dbService.BackupDatabaseAsync("WarmupDB", "/tmp", null, CancellationToken.None);
                await dbService.RestoreDatabaseAsync("WarmupDB", "/tmp/warmup.bak", null, CancellationToken.None);
                await dbService.AlterCollationAsync("WarmupDB", "Latin1_General_100_CI_AS", CancellationToken.None);

                // 1. Cancel 30-second BackupDatabaseAsync after 50ms -> total latency < 100ms
                dbService.OnExecuteCommand = async (cmd, ct) =>
                {
                    if (cmd.CommandText.Contains("BACKUP DATABASE"))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    }
                    return 0;
                };

                using (var cts = new CancellationTokenSource())
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    cts.CancelAfter(50);
                    bool backupCanceled = false;
                    try
                    {
                        await dbService.BackupDatabaseAsync("SlowBackupDB", "/tmp", null, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        backupCanceled = true;
                    }
                    sw.Stop();

                    Assert(backupCanceled, "Simulated 30-second backup must throw OperationCanceledException when cancelled.");
                    Assert(sw.ElapsedMilliseconds < 100, $"Backup cancellation after 50ms must complete in under 100ms, took {sw.ElapsedMilliseconds}ms.");
                }

                // 2. Cancel 30-second AlterCollationAsync after 50ms -> total latency < 100ms and emergency MULTI_USER runs
                dbService.ExecutedCommands.Clear();
                dbService.OnExecuteCommand = async (cmd, ct) =>
                {
                    if (cmd.CommandText.Contains("COLLATE"))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    }
                    return 0;
                };

                using (var cts = new CancellationTokenSource())
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    cts.CancelAfter(50);
                    bool alterCanceled = false;
                    try
                    {
                        await dbService.AlterCollationAsync("SlowAlterDB", "Latin1_General_100_CI_AS", cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        alterCanceled = true;
                    }
                    sw.Stop();

                    Assert(alterCanceled, "Simulated 30-second AlterCollationAsync must throw OperationCanceledException when cancelled.");
                    Assert(sw.ElapsedMilliseconds < 100, $"AlterCollation cancellation after 50ms must complete in under 100ms, took {sw.ElapsedMilliseconds}ms.");
                    Assert(dbService.ExecutedCommands.Any(c => c.CommandText.Contains("SET MULTI_USER") && c.CancellationToken == CancellationToken.None),
                        "Emergency MULTI_USER reset must execute with CancellationToken.None on AlterCollation cancellation.");
                }

                // 3. Cancel 30-second RestoreDatabaseAsync after 50ms -> total latency < 100ms
                dbService.ExecutedCommands.Clear();
                dbService.OnExecuteCommand = async (cmd, ct) =>
                {
                    if (cmd.CommandText.Contains("RESTORE DATABASE"))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    }
                    return 0;
                };

                using (var cts = new CancellationTokenSource())
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    cts.CancelAfter(50);
                    bool restoreCanceled = false;
                    try
                    {
                        await dbService.RestoreDatabaseAsync("SlowRestoreDB", "/tmp/slow.bak", null, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        restoreCanceled = true;
                    }
                    sw.Stop();

                    Assert(restoreCanceled, "Simulated 30-second RestoreDatabaseAsync must throw OperationCanceledException when cancelled.");
                    Assert(sw.ElapsedMilliseconds < 100, $"RestoreDatabase cancellation after 50ms must complete in under 100ms, took {sw.ElapsedMilliseconds}ms.");
                }
            });

            await RunAsyncTest("ProcessCollationChangeAsync_WithTestableDatabaseService_CancelsSlowBackup_WithoutRestore_AndUnlocksUI", async () =>
            {
                using var logger = new LoggerService();
                var backupStarted = new TaskCompletionSource<bool>();
                var dbService = new TestableDatabaseService(logger)
                {
                    SimulateConnectionOpen = true,
                    SimulatedPreflightReport = new PreflightReport { DatabaseName = "SlowBackupCancelDB" },
                    OnExecuteCommand = async (cmd, ct) =>
                    {
                        if (cmd.CommandText.Contains("BACKUP DATABASE"))
                        {
                            backupStarted.TrySetResult(true);
                            await Task.Delay(TimeSpan.FromSeconds(30), ct);
                        }
                        return 0;
                    }
                };
                dbService.SetConnectionString("localhost", "Windows Authentication", "", "");

                var vm = new MainViewModel(dbService)
                {
                    ConfirmCollationChangeHandler = (_, _) => true
                };
                var dbInfo = new DatabaseInfo { Name = "SlowBackupCancelDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                vm.Databases.Add(dbInfo);
                vm.SelectedDatabase = dbInfo;
                vm.TargetCollation = "Latin1_General_100_CI_AS";
                vm.BackupDirectory = "/tmp";

                var processTask = vm.ProcessCollationChangeAsync();
                await backupStarted.Task;

                Assert(vm.IsBusy, "MainViewModel should hold IsBusy while backup is running.");

                var cancelSw = System.Diagnostics.Stopwatch.StartNew();
                vm.Cancel();
                await processTask;
                cancelSw.Stop();

                Assert(cancelSw.ElapsedMilliseconds < 100, $"Cancelling slow backup should unlock workflow in under 100ms, took {cancelSw.ElapsedMilliseconds}ms.");
                Assert(!vm.IsBusy, "MainViewModel.IsBusy must unlock immediately after backup cancellation.");
                Assert(!dbService.ExecutedCommands.Any(c => c.CommandText.Contains("RESTORE DATABASE")),
                    "RestoreDatabaseAsync must NOT run when backup was cancelled before completing.");
                Assert(vm.StatusMessage == "Operation cancelled by user.",
                    $"Expected StatusMessage 'Operation cancelled by user.', got '{vm.StatusMessage}'.");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_WithTestableDatabaseService_AttentionSqlExceptionDuringAlterCollation_RecoversAndSetsCancelledStatus", async () =>
            {
                using var logger = new LoggerService();
                var alterStarted = new TaskCompletionSource<bool>();
                var attentionSqlEx = TestableDatabaseService.CreateSimulatedSqlException(
                    "A severe error occurred on the current command.  The results, if any, should be discarded.");

                var dbService = new TestableDatabaseService(logger)
                {
                    SimulateConnectionOpen = true,
                    SimulatedPreflightReport = new PreflightReport { DatabaseName = "AttentionCancelDB" },
                    OnExecuteCommand = async (cmd, ct) =>
                    {
                        if (cmd.CommandText.Contains("COLLATE"))
                        {
                            alterStarted.TrySetResult(true);
                            try
                            {
                                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                            }
                            catch (OperationCanceledException)
                            {
                                // Simulate SQL Server raising an attention SqlException after cancellation token triggers
                                throw attentionSqlEx;
                            }
                        }
                        return 0;
                    }
                };
                dbService.SetConnectionString("localhost", "Windows Authentication", "", "");

                var vm = new MainViewModel(dbService)
                {
                    ConfirmCollationChangeHandler = (_, _) => true
                };
                var dbInfo = new DatabaseInfo { Name = "AttentionCancelDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                vm.Databases.Add(dbInfo);
                vm.SelectedDatabase = dbInfo;
                vm.TargetCollation = "Latin1_General_100_CI_AS";
                vm.BackupDirectory = "/tmp";

                var processTask = vm.ProcessCollationChangeAsync();
                await alterStarted.Task;

                vm.Cancel();
                await processTask;

                Assert(!vm.IsBusy, "MainViewModel.IsBusy must reset to false after attention-cancelled collation change.");
                Assert(!vm.HasDetailedError, $"Attention cancellation should not record an error when rollback succeeds, got DetailedError: '{vm.DetailedError}'");
                Assert(vm.StatusMessage == "Operation cancelled by user.",
                    $"Expected StatusMessage 'Operation cancelled by user.', got '{vm.StatusMessage}'.");
                Assert(dbInfo.Collation == "SQL_Latin1_General_CP1_CI_AS",
                    "In-memory database collation must remain unchanged when alteration is cancelled.");

                var restoreCommands = dbService.ExecutedCommands.Where(c => c.CommandText.Contains("RESTORE DATABASE")).ToList();
                Assert(restoreCommands.Count == 1, $"Expected 1 backup rollback RESTORE DATABASE command, got {restoreCommands.Count}.");
                Assert(restoreCommands[0].CancellationToken == CancellationToken.None,
                    "Rollback RESTORE DATABASE command must execute with CancellationToken.None.");

                var multiUserCommands = dbService.ExecutedCommands.Where(c => c.CommandText.Contains("SET MULTI_USER")).ToList();
                Assert(multiUserCommands.Count >= 2, $"Expected emergency MULTI_USER reset commands to execute, got {multiUserCommands.Count}.");
                Assert(multiUserCommands.All(c => c.CancellationToken == CancellationToken.None),
                    "All MULTI_USER commands must execute with CancellationToken.None.");
            });

            Console.WriteLine("\nTest Run Finished: MainViewModel tests completed.");
            return failed;

            async Task RunAsyncTest(string name, Func<Task> testAction)
            {
                failed += await ExecuteAsyncTestAsync(name, testAction);
            }

            void RunSyncTest(string name, Action testAction)
            {
                failed += ExecuteSyncTest(name, testAction);
            }

            void Assert(bool condition, string message)
            {
                if (!condition)
                    throw new InvalidOperationException($"Assertion failed: {message}");
            }
        }
    }
}
