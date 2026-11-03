using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
                await GlobalExceptionHandlerTests.RunAllTestsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: GlobalExceptionHandlerTests - {ex}");
                failed++;
            }

            try
            {
                await MainViewModelTestsRunner.RunAllTestsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: MainViewModelTestsRunner - {ex}");
                failed++;
            }

            Console.WriteLine("\n=== Running LoggerService Async Queue Unit Tests ===");
            try
            {
                await TestHighFrequencyConcurrentLoggingWithoutDataLoss();
                Console.WriteLine("PASS: TestHighFrequencyConcurrentLoggingWithoutDataLoss");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestHighFrequencyConcurrentLoggingWithoutDataLoss - {ex}");
                failed++;
            }

            try
            {
                await TestFlushOnDispose();
                Console.WriteLine("PASS: TestFlushOnDispose");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestFlushOnDispose - {ex}");
                failed++;
            }

            try
            {
                await TestFileAccessExceptionHandling();
                Console.WriteLine("PASS: TestFileAccessExceptionHandling");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestFileAccessExceptionHandling - {ex}");
                failed++;
            }

            try
            {
                await TestEmptyChannelDisposalWithoutError();
                Console.WriteLine("PASS: TestEmptyChannelDisposalWithoutError");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestEmptyChannelDisposalWithoutError - {ex}");
                failed++;
            }

            try
            {
                await TestDrainAndFlushQueueAsyncDuringStandardOperation();
                Console.WriteLine("PASS: TestDrainAndFlushQueueAsyncDuringStandardOperation");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestDrainAndFlushQueueAsyncDuringStandardOperation - {ex}");
                failed++;
            }

            try
            {
                await TestDrainAndFlushQueueAsyncShutdownDraining();
                Console.WriteLine("PASS: TestDrainAndFlushQueueAsyncShutdownDraining");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestDrainAndFlushQueueAsyncShutdownDraining - {ex}");
                failed++;
            }


            try
            {
                await MainViewModelTestsRunner.RunAllTestsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: MainViewModelTestsRunner - {ex}");
                failed++;
            }

            try
            {
                TestLogFilePathProperty();
                Console.WriteLine("PASS: TestLogFilePathProperty");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestLogFilePathProperty - {ex}");
                failed++;
            }

            try
            {
                await MainViewModelTestsRunner.RunAllTestsAsync();
                Console.WriteLine("PASS: MainViewModelTestsRunner");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: MainViewModelTestsRunner - {ex}");
                failed++;
            }

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


        static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception($"Assertion Failed: {message}");
            }
        }
    }
}
