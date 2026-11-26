using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CollationModifier.Models;
using CollationModifier.Services;

namespace CollationModifier.Tests
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=== Running CollationModifier Unit Tests ===");
            int failed = 0;

            try
            {
                failed += await GlobalExceptionHandlerTests.RunAllTestsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {nameof(GlobalExceptionHandlerTests)}.{nameof(GlobalExceptionHandlerTests.RunAllTestsAsync)} - {ex}");
                failed++;
            }

            try
            {
                int mainVmFailed = await MainViewModelTestsRunner.RunAllTestsAsync();
                if (mainVmFailed == 0)
                {
                    Console.WriteLine("PASS: MainViewModelTestsRunner");
                }
                failed += mainVmFailed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {nameof(MainViewModelTestsRunner)}.{nameof(MainViewModelTestsRunner.RunAllTestsAsync)} - {ex}");
                failed++;
            }

            Console.WriteLine("\n=== Running LoggerService Async Queue Unit Tests ===");
            failed += await RunProgramAsyncTest(nameof(TestHighFrequencyConcurrentLoggingWithoutDataLoss), TestHighFrequencyConcurrentLoggingWithoutDataLoss);
            failed += await RunProgramAsyncTest(nameof(TestFlushOnDispose), TestFlushOnDispose);
            failed += await RunProgramAsyncTest(nameof(TestFileAccessExceptionHandling), TestFileAccessExceptionHandling);
            failed += await RunProgramAsyncTest(nameof(TestEmptyChannelDisposalWithoutError), TestEmptyChannelDisposalWithoutError);
            failed += await RunProgramAsyncTest(nameof(TestDrainAndFlushQueueAsyncDuringStandardOperation), TestDrainAndFlushQueueAsyncDuringStandardOperation);
            failed += await RunProgramAsyncTest(nameof(TestDrainAndFlushQueueAsyncShutdownDraining), TestDrainAndFlushQueueAsyncShutdownDraining);
            failed += RunProgramSyncTest(nameof(TestLogFilePathProperty), TestLogFilePathProperty);

            try
            {
                await TestConstructorDoesNotCreateDirectoryUntilFirstLogEntry();
                Console.WriteLine("PASS: TestConstructorDoesNotCreateDirectoryUntilFirstLogEntry");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestConstructorDoesNotCreateDirectoryUntilFirstLogEntry - {ex}");
                failed++;
            }

            try
            {
                await TestStalledWorkerFlushThenDisposeWaitsOnce();
                Console.WriteLine("PASS: TestStalledWorkerFlushThenDisposeWaitsOnce");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestStalledWorkerFlushThenDisposeWaitsOnce - {ex}");
                failed++;
            }

            try
            {
                TestFlushShortCircuitsWhenWorkerAlreadyComplete();
                Console.WriteLine("PASS: TestFlushShortCircuitsWhenWorkerAlreadyComplete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestFlushShortCircuitsWhenWorkerAlreadyComplete - {ex}");
                failed++;
            }

            Console.WriteLine("\n=== Running DatabaseService MapDatabaseInfo Unit Tests ===");
            failed += RunProgramSyncTest(nameof(TestMapDatabaseInfo_FieldExtraction), TestMapDatabaseInfo_FieldExtraction);
            failed += RunProgramSyncTest(nameof(TestMapDatabaseInfo_NullFieldFallbacks), TestMapDatabaseInfo_NullFieldFallbacks);
            failed += RunProgramSyncTest(nameof(TestMapDatabaseInfo_NullArgumentValidation), TestMapDatabaseInfo_NullArgumentValidation);

            Console.WriteLine("\n=== Running Test Runner Isolation & SynchronizationContext Hardening Tests ===");
            failed += await RunProgramAsyncTest(nameof(TestGlobalExceptionHandlerSuite_IsolatesFirstFailure_AndRunsRemainingSixTests), TestGlobalExceptionHandlerSuite_IsolatesFirstFailure_AndRunsRemainingSixTests);
            failed += await RunProgramAsyncTest(nameof(TestSynchronousSynchronizationContext_CatchesPostedAndAsyncVoidExceptions), TestSynchronousSynchronizationContext_CatchesPostedAndAsyncVoidExceptions);
            failed += await RunProgramAsyncTest(nameof(TestMainViewModelTestsRunner_FormatsSingleFailureWithClassMethodAndStackTrace), TestMainViewModelTestsRunner_FormatsSingleFailureWithClassMethodAndStackTrace);

            if (failed == 0)
            {
                Console.WriteLine("\nALL TESTS PASSED SUCCESSFULLY!");
                return 0;
            }
            else
            {
                Console.WriteLine($"\nTEST SUITE FAILED with {failed} failures.");
                return 1;
            }
        }

        private static async Task<int> RunProgramAsyncTest(string methodName, Func<Task> testAction)
        {
            try
            {
                await testAction();
                Console.WriteLine($"PASS: {methodName}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {nameof(Program)}.{methodName} - {ex}");
                return 1;
            }
        }

        private static int RunProgramSyncTest(string methodName, Action testAction)
        {
            try
            {
                testAction();
                Console.WriteLine($"PASS: {methodName}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {nameof(Program)}.{methodName} - {ex}");
                return 1;
            }
        }

        static async Task TestHighFrequencyConcurrentLoggingWithoutDataLoss()
        {
            string testLogPath = Path.Combine(Path.GetTempPath(), $"logger_test_concurrent_{Guid.NewGuid():N}.txt");

            try
            {
                const int totalMessages = 1000;
                const int concurrentTasks = 10;
                const int messagesPerTask = totalMessages / concurrentTasks;

                Stopwatch stopwatch = Stopwatch.StartNew();

                using (var logger = new LoggerService(testLogPath))
                {
                    Task[] tasks = Enumerable.Range(0, concurrentTasks).Select(taskId => Task.Run(() =>
                    {
                        for (int i = 0; i < messagesPerTask; i++)
                        {
                            if (i % 2 == 0)
                            {
                                logger.LogInfo($"Task {taskId} Info message {i}");
                            }
                            else
                            {
                                logger.LogError($"Task {taskId} Error message {i}", new InvalidOperationException("Test exception"));
                            }
                        }
                    })).ToArray();

                    await Task.WhenAll(tasks);
                    stopwatch.Stop();

                    // All caller threads finished enqueueing quickly.
                    Console.WriteLine($"  [Perf] Enqueued {totalMessages} messages across {concurrentTasks} threads in {stopwatch.ElapsedMilliseconds} ms.");
                    Assert(stopwatch.ElapsedMilliseconds < 5000, "Enqueueing 1000 log entries took too long, calling threads were blocked!");
                } // Dispose called here -> flushes queue to disk

                Assert(File.Exists(testLogPath), "Log file was not created.");

                string[] lines = File.ReadAllLines(testLogPath);
                Console.WriteLine($"  [Verify] Total log lines written to disk: {lines.Length}");

                int infoCount = lines.Count(l => l.Contains("[INFO]"));
                int errorCount = lines.Count(l => l.Contains("[ERROR]"));

                Console.WriteLine($"  [Verify] INFO entries: {infoCount}, ERROR entries: {errorCount}");
                Assert(infoCount + errorCount == totalMessages, $"Expected {totalMessages} log entries on disk, found {infoCount + errorCount}.");
            }
            finally
            {
                if (File.Exists(testLogPath))
                {
                    File.Delete(testLogPath);
                }
            }
        }

        static async Task TestFlushOnDispose()
        {
            string testLogPath = Path.Combine(Path.GetTempPath(), $"logger_test_flush_{Guid.NewGuid():N}.txt");

            try
            {
                const int count = 100;
                using (var logger = new LoggerService(testLogPath))
                {
                    for (int i = 0; i < count; i++)
                    {
                        logger.LogInfo($"Flush test log {i}");
                    }
                } // Dispose flushes queue

                Assert(File.Exists(testLogPath), "Log file was not created.");
                string[] lines = File.ReadAllLines(testLogPath);
                Assert(lines.Length == count, $"Expected {count} lines in flushed log file, found {lines.Length}.");
            }
            finally
            {
                if (File.Exists(testLogPath))
                {
                    File.Delete(testLogPath);
                }
            }
        }

        static async Task TestFileAccessExceptionHandling()
        {
            // Invalid directory path to trigger file write exception
            string testLogPath = Path.Combine("/invalid_nonexistent_dir_12345/subdir", "test.log");

            using (var logger = new LoggerService(testLogPath))
            {
                // Enqueue should not throw despite bad path
                logger.LogInfo("Message to invalid path");
                logger.LogError("Error message to invalid path");

                // Wait briefly for background loop to attempt write and handle exception
                await Task.Delay(200);
            } // Dispose should complete without crashing
        }


        static void TestLogFilePathProperty()
        {
            using (var logger = new LoggerService())
            {
                string path = logger.LogFilePath;
                Assert(!string.IsNullOrWhiteSpace(path), "LogFilePath should not be empty");
                string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                Assert(path.Contains("CollationModifier") || path.Contains(appDataFolder), "LogFilePath should be inside ApplicationData CollationModifier folder");
            }

            string customPath = Path.Combine(Path.GetTempPath(), "custom_log_test.log");
            using (var customLogger = new LoggerService(customPath))
            {
                Assert(customLogger.LogFilePath == customPath, "LogFilePath should match custom log path");
            }
        }

        static async Task TestEmptyChannelDisposalWithoutError()
        {
            string testLogPath = Path.Combine(Path.GetTempPath(), $"logger_test_empty_{Guid.NewGuid():N}.txt");

            try
            {
                using (var logger = new LoggerService(testLogPath))
                {
                    // No messages logged - channel is empty
                } // Dispose should exit cleanly without throwing and close writer resources

                if (File.Exists(testLogPath))
                {
                    string content = File.ReadAllText(testLogPath);
                    Assert(string.IsNullOrEmpty(content), "Expected empty log file for empty channel disposal.");
                }
            }
            finally
            {
                if (File.Exists(testLogPath))
                {
                    File.Delete(testLogPath);
                }
            }
        }

        static async Task TestDrainAndFlushQueueAsyncDuringStandardOperation()
        {
            string testLogPath = Path.Combine(Path.GetTempPath(), $"logger_test_drain_active_{Guid.NewGuid():N}.txt");

            try
            {
                using (var logger = new LoggerService(testLogPath))
                {
                    for (int i = 0; i < 20; i++)
                    {
                        logger.LogInfo($"Active drain test message {i}");
                    }

                    // Wait briefly for background task to process DrainAndFlushQueueAsync during active loop
                    bool flushed = false;
                    for (int retry = 0; retry < 50; retry++)
                    {
                        await Task.Delay(50);
                        if (File.Exists(testLogPath))
                        {
                            try
                            {
                                using (var fs = new FileStream(testLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                using (var reader = new StreamReader(fs))
                                {
                                    int lineCount = 0;
                                    while (reader.ReadLine() != null)
                                    {
                                        lineCount++;
                                    }
                                    if (lineCount == 20)
                                    {
                                        flushed = true;
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                // File might be open by background worker momentarily
                            }
                        }
                    }

                    Assert(flushed, "Expected all 20 messages to be drained and flushed during active operation before disposal.");
                }
            }
            finally
            {
                if (File.Exists(testLogPath))
                {
                    File.Delete(testLogPath);
                }
            }
        }

        static async Task TestDrainAndFlushQueueAsyncShutdownDraining()
        {
            string testLogPath = Path.Combine(Path.GetTempPath(), $"logger_test_drain_shutdown_{Guid.NewGuid():N}.txt");

            try
            {
                const int count = 50;
                using (var logger = new LoggerService(testLogPath))
                {
                    for (int i = 0; i < count; i++)
                    {
                        logger.LogWarning($"Shutdown drain message {i}");
                    }
                    // Immediately exit using block -> triggers Dispose -> finally { DrainAndFlushQueueAsync }
                }

                Assert(File.Exists(testLogPath), "Log file should exist after disposal.");
                string[] lines = File.ReadAllLines(testLogPath);
                Assert(lines.Length == count, $"Expected {count} lines drained on shutdown, found {lines.Length}.");
            }
            finally
            {
                if (File.Exists(testLogPath))
                {
                    File.Delete(testLogPath);
                }
            }
        }


        static async Task TestConstructorDoesNotCreateDirectoryUntilFirstLogEntry()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), $"logger_lazy_dir_{Guid.NewGuid():N}");
            string nestedDir = Path.Combine(baseDir, "nested");
            string testLogPath = Path.Combine(nestedDir, "lazy_log.txt");

            try
            {
                Assert(!Directory.Exists(baseDir), "Base test directory should not exist before constructing LoggerService.");

                using (var logger = new LoggerService(testLogPath))
                {
                    await Task.Delay(100);
                    Assert(!Directory.Exists(baseDir), "Constructing LoggerService must not create the log directory before logging.");
                    Assert(!Directory.Exists(nestedDir), "Constructing LoggerService must not create nested log directory before logging.");
                    Assert(!File.Exists(testLogPath), "Constructing LoggerService must not create the log file before logging.");

                    logger.LogInfo("First log entry triggers lazy directory creation");
                }

                Assert(Directory.Exists(nestedDir), "Background worker should create the log directory when writing the first log entry.");
                Assert(File.Exists(testLogPath), "Log file should exist after writing and disposing.");
                string content = File.ReadAllText(testLogPath);
                Assert(content.Contains("First log entry triggers lazy directory creation"), "Log file should contain the written log entry.");
            }
            finally
            {
                if (Directory.Exists(baseDir))
                {
                    Directory.Delete(baseDir, recursive: true);
                }
            }
        }

        static async Task TestStalledWorkerFlushThenDisposeWaitsOnce()
        {
            string testLogPath = Path.Combine(Path.GetTempPath(), $"logger_stalled_{Guid.NewGuid():N}.txt");
            var workerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseWorker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var logger = new LoggerService(testLogPath, async reader =>
                {
                    workerStarted.TrySetResult(true);
                    await reader.WaitToReadAsync().ConfigureAwait(false);
                    await releaseWorker.Task.ConfigureAwait(false);
                });

                await workerStarted.Task;
                logger.LogInfo("Stalled queue entry");

                var timeout = TimeSpan.FromMilliseconds(200);
                var sw = Stopwatch.StartNew();

                logger.Flush(timeout);
                logger.Dispose();
                logger.Flush(timeout);

                sw.Stop();
                Console.WriteLine($"  [Timing] Flush(200ms) + Dispose() + Flush(200ms) on stalled worker elapsed: {sw.ElapsedMilliseconds} ms");

                Assert(sw.ElapsedMilliseconds >= 150, $"Expected initial Flush to wait near {timeout.TotalMilliseconds}ms, but elapsed was {sw.ElapsedMilliseconds}ms.");
                Assert(sw.ElapsedMilliseconds < 380, $"Expected Flush then Dispose to wait for one timeout instead of two (or 5s), but elapsed was {sw.ElapsedMilliseconds}ms.");
                Assert(logger.IsDisposed, "Logger should be marked disposed after Dispose().");
            }
            finally
            {
                releaseWorker.TrySetResult(true);
                if (File.Exists(testLogPath))
                {
                    File.Delete(testLogPath);
                }
            }
        }

        static void TestFlushShortCircuitsWhenWorkerAlreadyComplete()
        {
            string testLogPath = Path.Combine(Path.GetTempPath(), $"logger_shortcircuit_{Guid.NewGuid():N}.txt");

            try
            {
                using var logger = new LoggerService(testLogPath);
                logger.LogInfo("Entry before initial flush");
                logger.Flush();

                var sw = Stopwatch.StartNew();
                logger.Flush(TimeSpan.FromSeconds(5));
                logger.Dispose();
                sw.Stop();

                Assert(sw.Elapsed.TotalMilliseconds < 25, $"Subsequent Flush/Dispose after completion should short-circuit immediately, took {sw.Elapsed.TotalMilliseconds:F2}ms.");
            }
            finally
            {
                if (File.Exists(testLogPath))
                {
                    File.Delete(testLogPath);
                }
            }
        }

        static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception($"Assertion Failed: {message}");
            }
        }

        static void TestMapDatabaseInfo_FieldExtraction()
        {
            var record = new MockDataRecord("TestDatabase", "Latin1_General_CI_AS", (byte)150, "dbo", 1024L);
            var dbInfo = DatabaseService.MapDatabaseInfo(record);

            Assert(dbInfo != null, "MapDatabaseInfo returned null.");
            Assert(dbInfo.Name == "TestDatabase", $"Expected Name 'TestDatabase', got '{dbInfo.Name}'.");
            Assert(dbInfo.Collation == "Latin1_General_CI_AS", $"Expected Collation 'Latin1_General_CI_AS', got '{dbInfo.Collation}'.");
            Assert(dbInfo.CompatibilityLevel == "150", $"Expected CompatibilityLevel '150', got '{dbInfo.CompatibilityLevel}'.");
            Assert(dbInfo.Owner == "dbo", $"Expected Owner 'dbo', got '{dbInfo.Owner}'.");
            Assert(dbInfo.Size == "1024 MB", $"Expected Size '1024 MB', got '{dbInfo.Size}'.");
        }

        static void TestMapDatabaseInfo_NullFieldFallbacks()
        {
            var record = new MockDataRecord("TestDatabaseNulls", DBNull.Value, (byte)140, DBNull.Value, DBNull.Value);
            var dbInfo = DatabaseService.MapDatabaseInfo(record);

            Assert(dbInfo != null, "MapDatabaseInfo returned null.");
            Assert(dbInfo.Name == "TestDatabaseNulls", $"Expected Name 'TestDatabaseNulls', got '{dbInfo.Name}'.");
            Assert(dbInfo.Collation == "Unknown", $"Expected Collation 'Unknown', got '{dbInfo.Collation}'.");
            Assert(dbInfo.CompatibilityLevel == "140", $"Expected CompatibilityLevel '140', got '{dbInfo.CompatibilityLevel}'.");
            Assert(dbInfo.Owner == "Unknown", $"Expected Owner 'Unknown', got '{dbInfo.Owner}'.");
            Assert(dbInfo.Size == "Unknown", $"Expected Size 'Unknown', got '{dbInfo.Size}'.");
        }

        static void TestMapDatabaseInfo_NullArgumentValidation()
        {
            bool threw = false;
            try
            {
                DatabaseService.MapDatabaseInfo((IDataRecord)null!);
            }
            catch (ArgumentNullException ex)
            {
                threw = true;
                Assert(ex.ParamName == "record", $"Expected ParamName 'record', got '{ex.ParamName}'.");
            }

            Assert(threw, "Expected ArgumentNullException when record is null.");
        }

        static async Task TestGlobalExceptionHandlerSuite_IsolatesFirstFailure_AndRunsRemainingSixTests()
        {
            const string firstMethod = "Test_DispatcherUnhandledException_LogsToLoggerService_AndContinuesWhenUserChoosesYes";
            var originalOut = Console.Out;
            using var captured = new StringWriter();
            int failures;
            try
            {
                Console.SetOut(captured);
                failures = await GlobalExceptionHandlerTests.RunAllTestsAsync(methodName =>
                {
                    if (methodName == firstMethod)
                    {
                        throw new InvalidOperationException("Simulated failure in first GlobalExceptionHandlerTests method");
                    }
                });
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            string log = captured.ToString();
            Assert(failures == 1, $"Expected exactly 1 failure when first test fails, got {failures}.");
            Assert(log.Contains($"[FAIL] GlobalExceptionHandlerTests.{firstMethod} - System.InvalidOperationException: Simulated failure in first GlobalExceptionHandlerTests method"),
                "Expected failure output to include GlobalExceptionHandlerTests.<TestMethod> and exception message.");
            Assert(log.Contains("at CollationModifier.Tests."), "Expected failure output to include stack trace.");

            int passCount = log.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Count(l => l.TrimStart().StartsWith("PASS: ", StringComparison.Ordinal));
            Assert(passCount == 6, $"Expected remaining 6 tests to execute and pass, got {passCount} PASS lines.");
        }

        static async Task TestSynchronousSynchronizationContext_CatchesPostedAndAsyncVoidExceptions()
        {
            var originalOut = Console.Out;
            using var captured = new StringWriter();
            int postedFailures;
            int asyncVoidFailures;
            bool subsequentTestRan = false;

            try
            {
                Console.SetOut(captured);

                // 1. Direct SynchronizationContext.Post unhandled exception
                postedFailures = MainViewModelTestsRunner.ExecuteSyncTest("PostedUnhandledCallbackTest", () =>
                {
                    System.Threading.SynchronizationContext.Current!.Post(_ =>
                    {
                        throw new InvalidOperationException("Unhandled exception from SynchronizationContext.Post");
                    }, null);
                });

                // 2. async void unhandled exception across await boundary
                asyncVoidFailures = await MainViewModelTestsRunner.ExecuteAsyncTestAsync("AsyncVoidUnhandledCrashTest", async () =>
                {
                    TriggerFaultingAsyncVoid();
                    await Task.Delay(20);
                });

                // 3. Verify subsequent test still runs normally without host abort
                int passResult = MainViewModelTestsRunner.ExecuteSyncTest("SubsequentHealthyTest", () =>
                {
                    subsequentTestRan = true;
                });
                Assert(passResult == 0, "Expected subsequent healthy test to return 0 failures.");
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            string log = captured.ToString();
            Assert(postedFailures == 1, $"Expected 1 failure for posted callback exception, got {postedFailures}.");
            Assert(asyncVoidFailures == 1, $"Expected 1 failure for async void exception, got {asyncVoidFailures}.");
            Assert(subsequentTestRan, "Expected subsequent test to run after SynchronizationContext unhandled exceptions.");
            Assert(log.Contains("[FAIL] MainViewModelTests.PostedUnhandledCallbackTest - System.InvalidOperationException: Unhandled exception from SynchronizationContext.Post"),
                "Expected MainViewModelTests.PostedUnhandledCallbackTest failure entry.");
            Assert(log.Contains("[FAIL] MainViewModelTests.AsyncVoidUnhandledCrashTest - System.InvalidOperationException: Unhandled async void crash after yield"),
                "Expected MainViewModelTests.AsyncVoidUnhandledCrashTest failure entry.");
            Assert(log.Contains("TriggerFaultingAsyncVoid"), "Expected original async void stack frame to be preserved.");
        }

        private static async void TriggerFaultingAsyncVoid()
        {
            await Task.Yield();
            throw new InvalidOperationException("Unhandled async void crash after yield");
        }

        static async Task TestMainViewModelTestsRunner_FormatsSingleFailureWithClassMethodAndStackTrace()
        {
            var originalOut = Console.Out;
            using var captured = new StringWriter();
            int failures;
            try
            {
                Console.SetOut(captured);
                failures = await MainViewModelTestsRunner.ExecuteAsyncTestAsync("SampleFailingViewModelTest", async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Assertion failed: Expected Ready\nActual: Busy");
                });
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            string log = captured.ToString();
            Assert(failures == 1, $"Expected integer failure count 1, got {failures}.");
            int failLines = log.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Count(l => l.TrimStart().StartsWith("[FAIL]", StringComparison.Ordinal));
            Assert(failLines == 1, $"Expected exactly 1 [FAIL] line without duplicate wrapper entry, got {failLines}.");
            Assert(log.Contains("[FAIL] MainViewModelTests.SampleFailingViewModelTest - System.InvalidOperationException: Assertion failed: Expected Ready"),
                "Expected ClassName.MethodName failure header.");
            Assert(log.Contains("Actual: Busy"), "Expected multiline failure message preserved.");
            Assert(log.Contains("at CollationModifier.Tests.Program"), "Expected stack trace preserved.");
        }

        private sealed class MockDataRecord : IDataRecord
        {
            private readonly object?[] _values;

            public MockDataRecord(params object?[] values)
            {
                _values = values;
            }

            public bool IsDBNull(int i) => _values[i] == null || _values[i] == DBNull.Value;
            public string GetString(int i) => (string)_values[i]!;
            public byte GetByte(int i) => (byte)_values[i]!;
            public long GetInt64(int i) => (long)_values[i]!;

            public int FieldCount => _values.Length;
            public string GetName(int i) => throw new NotImplementedException();
            public object this[int i] => _values[i]!;
            public object this[string name] => throw new NotImplementedException();
            public bool GetBoolean(int i) => throw new NotImplementedException();
            public char GetChar(int i) => throw new NotImplementedException();
            public Guid GetGuid(int i) => throw new NotImplementedException();
            public short GetInt16(int i) => throw new NotImplementedException();
            public int GetInt32(int i) => throw new NotImplementedException();
            public float GetFloat(int i) => throw new NotImplementedException();
            public double GetDouble(int i) => throw new NotImplementedException();
            public decimal GetDecimal(int i) => throw new NotImplementedException();
            public DateTime GetDateTime(int i) => throw new NotImplementedException();
            public IDataReader GetData(int i) => throw new NotImplementedException();
            public string GetDataTypeName(int i) => throw new NotImplementedException();
            public Type GetFieldType(int i) => throw new NotImplementedException();
            public object GetValue(int i) => _values[i]!;
            public int GetValues(object[] values) => throw new NotImplementedException();
            public int GetOrdinal(string name) => throw new NotImplementedException();
            public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
            public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
        }
    }
}
