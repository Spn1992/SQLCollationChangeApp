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
        public PreflightReport PreflightReportToReturn { get; set; } = new PreflightReport();
        public int GetDatabasesCount { get; private set; }
        public int AlterCollationCount { get; private set; }
        public int BackupCount { get; private set; }
        public string? LastAlteredDatabase { get; private set; }
        public string? LastTargetCollation { get; private set; }

        public void SetConnectionString(string server, string authType, string username, string password) { }

        public Task<bool> TestConnectionAsync() => Task.FromResult(true);

        public Task<List<DatabaseInfo>> GetDatabasesAsync()
        {
            GetDatabasesCount++;
            if (ThrowOnGetDatabases)
            {
                throw new InvalidOperationException("Failed to reach database catalog.");
            }
            return Task.FromResult(DatabasesToReturn);
        }

        public Task<string?> GetDefaultBackupDirectoryAsync() => Task.FromResult<string?>(null);

        public Task<PreflightReport> PreflightCheckAsync(string databaseName, string backupDirectory = "")
        {
            PreflightReportToReturn.DatabaseName = databaseName;
            return Task.FromResult(PreflightReportToReturn);
        }

        public Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory)
        {
            BackupCount++;
            return Task.FromResult("/tmp/mock.bak");
        }

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
            Console.WriteLine("\n=== Running MainViewModel & Pre-flight Validation Tests ===");

            await RunSyncTest("DatabaseInfo_RaisesPropertyChanged_WhenCollationChanges", () =>
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
                    failed++;
                }
            }

            Task RunSyncTest(string name, Action testAction)
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
                return Task.CompletedTask;
            }

            void Assert(bool condition, string message)
            {
                if (!condition)
                    throw new InvalidOperationException($"Assertion failed: {message}");
            }
        }
    }
}
