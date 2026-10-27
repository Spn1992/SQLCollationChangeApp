using CollationModifier.Collections;
using CollationModifier.Models;
using CollationModifier.Services;
using CollationModifier.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;

namespace CollationModifier.Tests
{
    public class MockDatabaseService : IDatabaseService
    {
        public List<DatabaseInfo> DatabasesToReturn { get; set; } = new();
        public int GetDatabasesCount { get; private set; }
        public int AlterCollationCount { get; private set; }
        public string? LastAlteredDatabase { get; private set; }
        public string? LastTargetCollation { get; private set; }

        public void SetConnectionString(string server, string authType, string username, string password) { }

        public Task<bool> TestConnectionAsync() => Task.FromResult(true);

        public Task<List<DatabaseInfo>> GetDatabasesAsync()
        {
            GetDatabasesCount++;
            return Task.FromResult(DatabasesToReturn);
        }

        public Task<string?> GetDefaultBackupDirectoryAsync() => Task.FromResult<string?>("/tmp/default_backup");

        public Task<List<string>> GetSupportedCollationsAsync() => Task.FromResult(new List<string> { "SQL_Latin1_General_CP1_CI_AS", "Latin1_General_100_CI_AS" });

        public Task PreflightCheckAsync(string databaseName, string backupDirectory = "") => Task.CompletedTask;

        public Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory) => Task.FromResult("/tmp/mock.bak");

        public Task RestoreDatabaseAsync(string databaseName, string backupFile) => Task.CompletedTask;

        public Task AlterCollationAsync(string databaseName, string newCollation)
        {
            AlterCollationCount++;
            LastAlteredDatabase = databaseName;
            LastTargetCollation = newCollation;
            return Task.CompletedTask;
        }
    }

    public static class MainViewModelTestsRunner
    {
        public static async Task RunTestsAsync()
        {
            Console.WriteLine("Running CollationModifier Tests...");

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

            await RunAsyncTest("ProcessCollationChangeAsync_WhenUserCancels_DoesNotCallDatabaseService", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "TestDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                vm.ConfirmCollationChangeHandler = (db, col) => false;
                await vm.LoadDatabasesAsync();

                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_CI_AS";

                await vm.ProcessCollationChangeAsync();

                Assert(mockService.AlterCollationCount == 0, "AlterCollationCount should be 0 when cancelled");
                Assert(vm.SelectedDatabase.Collation == "SQL_Latin1_General_CP1_CI_AS", "Collation should not be altered when cancelled");
                Assert(vm.IsBusy == false, "IsBusy should remain false when cancelled");
                Assert(vm.StatusMessage == "Collation change operation cancelled.", $"Expected cancellation status message, got '{vm.StatusMessage}'");
            });

            await RunAsyncTest("ProcessCollationChangeAsync_ConfirmationPrompt_PassesDatabaseNameAndTargetCollation", async () =>
            {
                var initialDb = new DatabaseInfo { Name = "TargetDB", Collation = "SQL_Latin1_General_CP1_CI_AS" };
                var mockService = new MockDatabaseService
                {
                    DatabasesToReturn = new List<DatabaseInfo> { initialDb }
                };

                var vm = new MainViewModel(mockService);
                string? promptedDb = null;
                string? promptedCollation = null;
                vm.ConfirmCollationChangeHandler = (db, col) =>
                {
                    promptedDb = db;
                    promptedCollation = col;
                    return false;
                };

                await vm.LoadDatabasesAsync();
                vm.SelectedDatabase = vm.Databases[0];
                vm.TargetCollation = "Latin1_General_100_BIN";

                await vm.ProcessCollationChangeAsync();

                Assert(promptedDb == "TargetDB", $"Expected prompted database 'TargetDB', got '{promptedDb}'");
                Assert(promptedCollation == "Latin1_General_100_BIN", $"Expected prompted collation 'Latin1_General_100_BIN', got '{promptedCollation}'");
            });

            Console.WriteLine("\nTest Run Finished: All tests passed!");

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
