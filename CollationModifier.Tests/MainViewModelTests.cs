using CollationModifier.Collections;
using CollationModifier.Models;
using CollationModifier.Services;
using CollationModifier.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace CollationModifier.Tests
{
    public class MockDatabaseService : IDatabaseService
    {
        public List<DatabaseInfo> DatabasesToReturn { get; set; } = new();
        public List<string> CollationsToReturn { get; set; } = new();
        public int GetDatabasesCount { get; private set; }
        public int AlterCollationCount { get; private set; }
        public string? LastAlteredDatabase { get; private set; }
        public string? LastTargetCollation { get; private set; }
        public Func<CancellationToken, Task>? OnBackupDatabase { get; set; }
        public Func<CancellationToken, Task>? OnAlterCollation { get; set; }
        public Func<CancellationToken, Task>? OnTestConnection { get; set; }

        public void SetConnectionString(string server, string authType, string username, string password) { }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (OnTestConnection != null)
            {
                await OnTestConnection(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }

        public Task<List<DatabaseInfo>> GetDatabasesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetDatabasesCount++;
            return Task.FromResult(DatabasesToReturn);
        }

        public Task<string?> GetDefaultBackupDirectoryAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>("/tmp/mock_backup_dir");
        }

        public Task<List<string>> GetSupportedCollationsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CollationsToReturn);
        }

        public Task PreflightCheckAsync(string databaseName, string backupDirectory = "", CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, CancellationToken cancellationToken = default)
        {
            if (OnBackupDatabase != null)
            {
                await OnBackupDatabase(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return "/tmp/mock.bak";
        }

        public Task RestoreDatabaseAsync(string databaseName, string backupFile, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
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
        }
    }

    public static class MainViewModelTests
    {
        public static async Task RunAsync()
        {
            Console.WriteLine("=== Running CollationModifier MainViewModel Unit Tests ===");

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

                await vm.LoadDatabasesAsync();

                Assert(collectionChangedCount == 1, $"Expected collectionChangedCount 1, got {collectionChangedCount}");
                Assert(vm.Databases.Count == 3, $"Expected 3 databases in collection, got {vm.Databases.Count}");
                Assert(mockService.GetDatabasesCount == 1, $"Expected GetDatabasesCount 1, got {mockService.GetDatabasesCount}");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_UpdatesCollationInMemory_WithoutCallingLoadDatabasesAsync", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "DB1", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
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

            await RunAsyncTest("ProcessCollationChangeAsync_WhenCancelled_UpdatesStatusToOperationCancelledByUser", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "DB1", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var tcs = new TaskCompletionSource<bool>();
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb },
                    OnBackupDatabase = async (token) =>
                    {
                        tcs.SetResult(true);
                        while (!token.IsCancellationRequested)
                        {
                            await Task.Delay(10, CancellationToken.None);
                        }
                        token.ThrowIfCancellationRequested();
                    }
                };

                var vm = new MainViewModel(mockService)
                {
                    IsConnected = true,
                    SelectedDatabase = initialDb,
                    TargetCollation = "Latin1_General_100_CI_AS_KS_WS",
                    BackupDirectory = "/tmp/backups"
                };

                var processTask = vm.ProcessCollationChangeAsync();
                await tcs.Task;
                Assert(vm.IsBusy, "MainViewModel should be busy during operation");

                vm.CancelCommand.Execute(null);
                await processTask;

                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Expected status 'Operation cancelled by user.', got '{vm.StatusMessage}'");
                Assert(!vm.IsBusy, "MainViewModel should not be busy after cancellation");
            });

            await RunAsyncTest("ConnectAsync_WhenCancelled_UpdatesStatusToOperationCancelledByUser", async () =>
            {
                var tcs = new TaskCompletionSource<bool>();
                var mockService = new MockDatabaseService
                {
                    OnTestConnection = async (token) =>
                    {
                        tcs.SetResult(true);
                        while (!token.IsCancellationRequested)
                        {
                            await Task.Delay(10, CancellationToken.None);
                        }
                        token.ThrowIfCancellationRequested();
                    }
                };

                var vm = new MainViewModel(mockService)
                {
                    ServerName = "localhost"
                };

                var connectTask = vm.ConnectAsync();
                await tcs.Task;
                Assert(vm.IsBusy, "MainViewModel should be busy during connect");

                vm.CancelCommand.Execute(null);
                await connectTask;

                Assert(vm.StatusMessage == "Operation cancelled by user.", $"Expected status 'Operation cancelled by user.', got '{vm.StatusMessage}'");
                Assert(!vm.IsConnected, "MainViewModel should not be connected after cancellation");
                Assert(!vm.IsBusy, "MainViewModel should not be busy after cancellation");
            });

            async Task RunAsyncTest(string name, Func<Task> testAction)
            {
                try
                {
                    await testAction();
                    Console.WriteLine($"  [PASS] {name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
                    throw;
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
                    throw;
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
