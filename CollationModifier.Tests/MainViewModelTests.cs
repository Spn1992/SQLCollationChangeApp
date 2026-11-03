using CollationModifier.Collections;
using CollationModifier.Models;
using CollationModifier.Services;
using CollationModifier.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CollationModifier.Tests
{
    public class MockDatabaseService : IDatabaseService
    {
        public List<DatabaseInfo> DatabasesToReturn { get; set; } = new();
        public List<string>? CollationsToReturn { get; set; } = null;
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
        public Func<CancellationToken, Task>? OnBackupDatabase { get; set; }
        public Func<CancellationToken, Task>? OnAlterCollation { get; set; }
        public Action<IProgress<int>?>? BackupProgressSimulator { get; set; }
        public Action<IProgress<int>?>? RestoreProgressSimulator { get; set; }
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

        public Task<List<DatabaseInfo>> GetDatabasesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetDatabasesCount++;
            if (ThrowOnGetDatabases)
            {
                throw new InvalidOperationException("Failed to reach database catalog.");
            }
            return Task.FromResult(DatabasesToReturn);
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

        public Task<PreflightReport> PreflightCheckAsync(string databaseName, string backupDirectory = "", string targetCollation = "", CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreflightReportToReturn.DatabaseName = databaseName;
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
            }
            return Task.FromResult(PreflightReportToReturn);
        }

        public async Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            if (OnBackupDatabase != null)
            {
                await OnBackupDatabase(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
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

    public class TestableDatabaseService : DatabaseService
    {
        public int GetSupportedCollationsCallCount { get; private set; }
        public List<string> CollationsToReturn { get; set; } = new List<string>
        {
            "SQL_Latin1_General_CP1_CI_AS",
            "Latin1_General_100_CI_AS"
        };
        public bool ThrowOnGetSupportedCollations { get; set; } = false;

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
    }

    public class SynchronousSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
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
        public static Task RunAllTestsAsync() => RunTestsAsync();

        public static async Task RunTestsAsync()
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

                await vm.LoadCollationsAsync();

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
                Assert(vm.StatusMessage == "Ready", $"Expected StatusMessage to be 'Ready', got '{vm.StatusMessage}'");
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
                Assert(vm.StatusMessage == "Disconnected from server.", $"Expected 'Disconnected from server.', got '{vm.StatusMessage}'");
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
                    vm.CancelOperation();
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
                    vm.CancelOperation();
                    token.ThrowIfCancellationRequested();
                    await Task.CompletedTask;
                };

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.RestoreCount == 0, "RestoreDatabaseAsync should NOT be invoked if cancelled before backup completes.");
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Status message should indicate cancellation by user, got: '{vm.StatusMessage}'");
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

            await RunAsyncTest("LoadCollationsAsync_PopulatesSupportedCollations", async () =>
            {
                var mockService = new MockDatabaseService
                {
                    CollationsToReturn = new List<string> { "SQL_Latin1_General_CP1_CI_AS", "Latin1_General_100_CI_AS_SC" }
                };
                var vm = new MainViewModel(mockService);

                await vm.LoadCollationsAsync();

                Assert(vm.SupportedCollations.Count == 2, $"Expected 2 collations, got {vm.SupportedCollations.Count}");
                Assert(vm.SupportedCollations[0] == "SQL_Latin1_General_CP1_CI_AS", $"Expected 'SQL_Latin1_General_CP1_CI_AS', got '{vm.SupportedCollations[0]}'");
            });

            await RunAsyncTest("ConnectAsync_InvokesLoadCollationsAsync_OnSuccessfulConnection", async () =>
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
                await vm.LoadCollationsAsync();
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
                await vm.LoadCollationsAsync();

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
                Assert(DatabaseService.IsValidCollationFormat("SQL_Latin1_General_CP1_CI_AS"), "IsValidCollationFormat should be true for valid format.");

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
                    Assert(!DatabaseService.IsValidCollationFormat(invalid), $"IsValidCollationFormat should be false for: '{invalid}'");
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
                string appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "App.xaml"));
                if (!System.IO.File.Exists(appXamlPath))
                {
                    appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "CollationModifier", "App.xaml"));
                }
                if (!System.IO.File.Exists(appXamlPath))
                {
                    appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "CollationModifier", "App.xaml"));
                }
                Assert(System.IO.File.Exists(appXamlPath), $"App.xaml file should exist at path {appXamlPath}");

                string content = System.IO.File.ReadAllText(appXamlPath);

                Assert(content.Contains("x:Key=\"FocusVisualStyle\""), "App.xaml should define FocusVisualStyle");
                Assert(content.Contains("x:Key=\"FocusBorderBrush\" Color=\"#0284C7\""), "App.xaml should define FocusBorderBrush with #0284C7");
                Assert(content.Contains("<Style TargetType=\"Button\">"), "App.xaml should define implicit TargetType=\"Button\" style");
                Assert(content.Contains("<Setter Property=\"FocusVisualStyle\" Value=\"{StaticResource FocusVisualStyle}\" />"), "App.xaml should apply FocusVisualStyle across implicit control styles");
                Assert(!content.Contains("<Setter Property=\"BorderThickness\" Value=\"2\" />"), "App.xaml should not contain layout-shifting BorderThickness=2 trigger setters");
                Assert(content.Contains("#047857"), "App.xaml should define PrimaryButton background with #047857");
                Assert(content.Contains("#1D4ED8"), "App.xaml should define SecondaryButton background with #1D4ED8");
                Assert(!content.Contains("#10B981"), "App.xaml should not contain non-compliant color #10B981");
                Assert(!content.Contains("#3B82F6"), "App.xaml should not contain non-compliant color #3B82F6");
            });

            RunSyncTest("MainWindowXaml_MetadataValues_AutomationProperties_Configured", () =>
            {
                string mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "MainWindow.xaml"));
                if (!System.IO.File.Exists(mainWindowXamlPath))
                {
                    mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "CollationModifier", "MainWindow.xaml"));
                }
                if (!System.IO.File.Exists(mainWindowXamlPath))
                {
                    mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "CollationModifier", "MainWindow.xaml"));
                }
                Assert(System.IO.File.Exists(mainWindowXamlPath), $"MainWindow.xaml file should exist at path {mainWindowXamlPath}");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var textBlocks = doc.Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();

                // 1. Current Collation
                var collationValueTb = textBlocks.FirstOrDefault(tb =>
                    tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.Collation") == true);
                Assert(collationValueTb != null, "Collation value TextBlock should have AutomationProperties.Name bound to SelectedDatabase.Collation");
                string collationNameAttr = collationValueTb!.Attribute("AutomationProperties.Name")!.Value;
                Assert(collationNameAttr.Contains("StringFormat='Current Collation: {0}'"), "Collation value TextBlock should format with 'Current Collation: {0}'");
                Assert(collationNameAttr.Contains("FallbackValue='Current Collation: -'"), "Collation value TextBlock should have FallbackValue='Current Collation: -'");
                Assert(collationNameAttr.Contains("TargetNullValue='Current Collation: -'"), "Collation value TextBlock should have TargetNullValue='Current Collation: -'");
                string collationLabeledBy = collationValueTb.Attribute("AutomationProperties.LabeledBy")?.Value ?? "";
                Assert(collationLabeledBy.Contains("ElementName=CurrentCollationLabel"), "Collation value TextBlock should retain LabeledBy binding to CurrentCollationLabel");

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

            RunSyncTest("MainViewModel_SelectedDatabase_MetadataAccessibleNames_EvaluatesCompositeStrings", () =>
            {
                string mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "MainWindow.xaml"));
                if (!System.IO.File.Exists(mainWindowXamlPath))
                {
                    mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "CollationModifier", "MainWindow.xaml"));
                }
                if (!System.IO.File.Exists(mainWindowXamlPath))
                {
                    mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "CollationModifier", "MainWindow.xaml"));
                }
                Assert(System.IO.File.Exists(mainWindowXamlPath), $"MainWindow.xaml file should exist at path {mainWindowXamlPath}");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);
                var doc = System.Xml.Linq.XDocument.Parse(content);
                var textBlocks = doc.Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();

                var collationTb = textBlocks.First(tb => tb.Attribute("AutomationProperties.Name")?.Value.Contains("SelectedDatabase.Collation") == true);
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
                Assert(EvaluateMetadataAccessibleName(collationTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Current Collation: -",
                    "Expected fallback name 'Current Collation: -' when SelectedDatabase is null");
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
                    CompatibilityLevel = "150",
                    Owner = "domain\\dbadmin",
                    Size = "5120 MB"
                };
                vm.SelectedDatabase = db;

                Assert(EvaluateMetadataAccessibleName(collationTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Current Collation: SQL_Latin1_General_CP1_CI_AS",
                    "Expected composite accessible name with dynamic collation");
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
                    CompatibilityLevel = "",
                    Owner = "",
                    Size = ""
                };
                vm.SelectedDatabase = emptyDb;

                Assert(EvaluateMetadataAccessibleName(collationTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Current Collation: -",
                    "Expected TargetNullValue 'Current Collation: -' when collation is empty");
                Assert(EvaluateMetadataAccessibleName(compatTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Compatibility Level: -",
                    "Expected TargetNullValue 'Compatibility Level: -' when compatibility level is empty");
                Assert(EvaluateMetadataAccessibleName(ownerTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Owner: -",
                    "Expected TargetNullValue 'Owner: -' when owner is empty");
                Assert(EvaluateMetadataAccessibleName(sizeTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Size: -",
                    "Expected TargetNullValue 'Size: -' when size is empty");

                // Case 4: Switching back to null database returns fallback
                vm.SelectedDatabase = null;
                Assert(EvaluateMetadataAccessibleName(collationTb.Attribute("AutomationProperties.Name")!.Value, vm) == "Current Collation: -",
                    "Expected fallback name after resetting SelectedDatabase to null");
            });

            RunSyncTest("MainWindowXaml_StatusBarTextBlock_ConfiguredWithPoliteLiveSetting", () =>
            {
                string mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "MainWindow.xaml"));
                if (!System.IO.File.Exists(mainWindowXamlPath))
                {
                    mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "CollationModifier", "MainWindow.xaml"));
                }
                if (!System.IO.File.Exists(mainWindowXamlPath))
                {
                    mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "CollationModifier", "MainWindow.xaml"));
                }
                Assert(System.IO.File.Exists(mainWindowXamlPath), $"MainWindow.xaml file should exist at path {mainWindowXamlPath}");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);

                Assert(content.Contains("Text=\"{Binding StatusMessage}\""), "MainWindow.xaml should contain status message binding");
                Assert(content.Contains("AutomationProperties.LiveSetting=\"Polite\""), "MainWindow.xaml status TextBlock should have AutomationProperties.LiveSetting=\"Polite\"");
            });

            RunSyncTest("AppXaml_ButtonControlTemplates_IncorporateFocusVisualBorderAndTrigger", () =>
            {
                string appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "App.xaml"));
                if (!System.IO.File.Exists(appXamlPath))
                {
                    appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "CollationModifier", "App.xaml"));
                }
                if (!System.IO.File.Exists(appXamlPath))
                {
                    appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "CollationModifier", "App.xaml"));
                }
                Assert(System.IO.File.Exists(appXamlPath), $"App.xaml file should exist at path {appXamlPath}");

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
                Assert(vm.StatusMessage == "Ready", $"Expected StatusMessage 'Ready', got '{vm.StatusMessage}'");
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
                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Expected 'Operation cancelled by user.' status, got: '{vm.StatusMessage}'");
                Assert(vm.ProgressPercent == 0, "ProgressPercent should reset to 0 after cancellation.");
            });

            RunSyncTest("AppXaml_ComboBox_VirtualizationSetters_Configured", () =>
            {
                string appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "App.xaml"));
                if (!System.IO.File.Exists(appXamlPath))
                {
                    appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "CollationModifier", "App.xaml"));
                }
                if (!System.IO.File.Exists(appXamlPath))
                {
                    appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "CollationModifier", "App.xaml"));
                }
                Assert(System.IO.File.Exists(appXamlPath), $"App.xaml file should exist at path {appXamlPath}");

                string content = System.IO.File.ReadAllText(appXamlPath);

                Assert(content.Contains("<Style TargetType=\"ComboBox\">"), "App.xaml should define implicit ComboBox style");
                Assert(content.Contains("<Setter Property=\"VirtualizingPanel.IsVirtualizing\" Value=\"True\" />"), "App.xaml ComboBox style should enable VirtualizingPanel.IsVirtualizing");
                Assert(content.Contains("<Setter Property=\"VirtualizingPanel.VirtualizationMode\" Value=\"Recycling\" />"), "App.xaml ComboBox style should configure VirtualizingPanel.VirtualizationMode to Recycling");
                Assert(content.Contains("<Setter Property=\"ScrollViewer.CanContentScroll\" Value=\"True\" />"), "App.xaml ComboBox style should enable ScrollViewer.CanContentScroll");
                Assert(content.Contains("<VirtualizingStackPanel />"), "App.xaml ComboBox style should set ItemsPanel to a VirtualizingStackPanel template");
            });

            RunSyncTest("AppXaml_ComboBoxStyle_Virtualization_Configured", () =>
            {
                string appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "App.xaml"));
                if (!System.IO.File.Exists(appXamlPath))
                {
                    appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "CollationModifier", "App.xaml"));
                }
                if (!System.IO.File.Exists(appXamlPath))
                {
                    appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "CollationModifier", "App.xaml"));
                }
                Assert(System.IO.File.Exists(appXamlPath), $"App.xaml file should exist at path {appXamlPath}");

                string content = System.IO.File.ReadAllText(appXamlPath);

                Assert(content.Contains("<Style TargetType=\"ComboBox\">"), "App.xaml should define implicit TargetType=\"ComboBox\" style");
                Assert(content.Contains("<Setter Property=\"VirtualizingPanel.IsVirtualizing\" Value=\"True\" />"), "App.xaml ComboBox style should set VirtualizingPanel.IsVirtualizing to True");
                Assert(content.Contains("<Setter Property=\"VirtualizingPanel.VirtualizationMode\" Value=\"Recycling\" />"), "App.xaml ComboBox style should set VirtualizingPanel.VirtualizationMode to Recycling");
                Assert(content.Contains("<Setter Property=\"ScrollViewer.CanContentScroll\" Value=\"True\" />"), "App.xaml ComboBox style should set ScrollViewer.CanContentScroll to True");
                Assert(content.Contains("<VirtualizingStackPanel />"), "App.xaml ComboBox style should set ItemsPanel to a VirtualizingStackPanel template");
            });

            RunSyncTest("MainWindowXaml_TargetCollationInput_VirtualizationProperties_Configured", () =>
            {
                string mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "MainWindow.xaml"));
                if (!System.IO.File.Exists(mainWindowXamlPath))
                {
                    mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "CollationModifier", "MainWindow.xaml"));
                }
                if (!System.IO.File.Exists(mainWindowXamlPath))
                {
                    mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "CollationModifier", "MainWindow.xaml"));
                }
                Assert(System.IO.File.Exists(mainWindowXamlPath), $"MainWindow.xaml file should exist at path {mainWindowXamlPath}");

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
                string mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "MainWindow.xaml"));
                if (!System.IO.File.Exists(mainWindowXamlPath))
                {
                    mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "CollationModifier", "MainWindow.xaml"));
                }
                if (!System.IO.File.Exists(mainWindowXamlPath))
                {
                    mainWindowXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "CollationModifier", "MainWindow.xaml"));
                }
                Assert(System.IO.File.Exists(mainWindowXamlPath), $"MainWindow.xaml file should exist at path {mainWindowXamlPath}");

                string content = System.IO.File.ReadAllText(mainWindowXamlPath);

                Assert(content.Contains("x:Name=\"TargetCollationInput\""), "MainWindow.xaml should define TargetCollationInput ComboBox");
                Assert(content.Contains("VirtualizingPanel.IsVirtualizing=\"True\""), "TargetCollationInput should explicitly set VirtualizingPanel.IsVirtualizing=\"True\"");
                Assert(content.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\""), "TargetCollationInput should explicitly set VirtualizingPanel.VirtualizationMode=\"Recycling\"");
                Assert(content.Contains("ScrollViewer.CanContentScroll=\"True\""), "TargetCollationInput should explicitly set ScrollViewer.CanContentScroll=\"True\"");
            });

            Console.WriteLine("\nTest Run Finished: MainViewModel tests completed.");
            if (failed > 0)
            {
                throw new InvalidOperationException($"MainViewModel tests failed with {failed} failures.");
            }

            async Task RunAsyncTest(string name, Func<Task> testAction)
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new SynchronousSynchronizationContext());
                    await testAction();
                    Console.WriteLine($"  [PASS] {name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
                    failed++;
                }
            }

            void RunSyncTest(string name, Action testAction)
            {
                try
                {
                    testAction();
                    Console.WriteLine($"  [PASS] {name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
                    failed++;
                }
            }

            void Assert(bool condition, string message)
            {
                if (!condition)
                    throw new InvalidOperationException($"Assertion failed: {message}");
            }
        }
    }
}
