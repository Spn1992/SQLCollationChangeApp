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

            Console.WriteLine("\n=== Running MainForm UI Accessibility & SystemColors Tests ===");

            try
            {
                TestMainFormTableLayoutPanelLayout();
                Console.WriteLine("PASS: TestMainFormTableLayoutPanelLayout");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestMainFormTableLayoutPanelLayout - {ex}");
                failed++;
            }

            try
            {
                TestMainFormSystemColorsAndStyles();
                Console.WriteLine("PASS: TestMainFormSystemColorsAndStyles");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestMainFormSystemColorsAndStyles - {ex}");
                failed++;
            }

            try
            {
                TestMainFormAccessKeys();
                Console.WriteLine("PASS: TestMainFormAccessKeys");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: TestMainFormAccessKeys - {ex}");
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

        static void TestMainFormTableLayoutPanelLayout()
        {
            string designerPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "MainForm.Designer.cs"));
            if (!File.Exists(designerPath))
            {
                // Fallback check relative to execution directory
                designerPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "CollationModifier", "MainForm.Designer.cs"));
            }
            if (!File.Exists(designerPath))
            {
                designerPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "CollationModifier", "MainForm.Designer.cs"));
            }

            Assert(File.Exists(designerPath), $"MainForm.Designer.cs not found at path: {designerPath}");
            string content = File.ReadAllText(designerPath);

            // 1. Check panelAuth is TableLayoutPanel with AutoScroll
            Assert(content.Contains("private System.Windows.Forms.TableLayoutPanel panelAuth;"), "panelAuth should be declared as TableLayoutPanel");
            Assert(content.Contains("this.panelAuth.AutoScroll = true;"), "panelAuth should have AutoScroll = true");
            Assert(content.Contains("this.panelAuth.ColumnCount = 1;"), "panelAuth should have ColumnCount = 1");

            // 2. Check panelDashboardContent is TableLayoutPanel with Fill Docking
            Assert(content.Contains("private System.Windows.Forms.TableLayoutPanel panelDashboardContent;"), "panelDashboardContent should be declared as TableLayoutPanel");
            Assert(content.Contains("this.panelDashboardContent.Dock = System.Windows.Forms.DockStyle.Fill;"), "panelDashboardContent should have Dock = Fill");

            // 3. Check panelMetadata layout with 2-column TableLayoutPanel
            Assert(content.Contains("private System.Windows.Forms.TableLayoutPanel tblMetadataLayout;"), "tblMetadataLayout should be declared as TableLayoutPanel");
            Assert(content.Contains("this.tblMetadataLayout.ColumnCount = 2;"), "tblMetadataLayout should have ColumnCount = 2");

            // 4. Check lblWarning AutoSize and MaximumSize
            Assert(content.Contains("this.lblWarning.AutoSize = true;"), "lblWarning should have AutoSize = true");
            Assert(content.Contains("this.lblWarning.MaximumSize = new System.Drawing.Size(500, 0);"), "lblWarning should have MaximumSize = (500, 0)");

            // 5. Check panelDashboard AutoScroll and Form AutoScaleMode
            Assert(content.Contains("this.panelDashboard.AutoScroll = true;"), "panelDashboard should have AutoScroll = true");
            Assert(content.Contains("this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;"), "MainForm should preserve AutoScaleMode.Font");

            Console.WriteLine("  [Verify] All TableLayoutPanel layout declarations and WCAG scaling attributes verified in MainForm.Designer.cs.");
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

        static void TestMainFormSystemColorsAndStyles()
        {
            string designerPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "MainForm.Designer.cs"));
            if (!File.Exists(designerPath))
            {
                designerPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "CollationModifier", "MainForm.Designer.cs"));
            }
            if (!File.Exists(designerPath))
            {
                designerPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "CollationModifier", "MainForm.Designer.cs"));
            }

            Assert(File.Exists(designerPath), $"MainForm.Designer.cs not found at path: {designerPath}");
            string content = File.ReadAllText(designerPath);

            Assert(!content.Contains("Color.FromArgb"), "MainForm.Designer.cs should contain zero hardcoded Color.FromArgb assignments.");
            Assert(!content.Contains("Color.White"), "MainForm.Designer.cs should contain zero hardcoded Color.White assignments.");
            Assert(content.Contains("this.btnConnect.UseVisualStyleBackColor = true;"), "btnConnect should have UseVisualStyleBackColor = true");
            Assert(content.Contains("this.btnProcess.UseVisualStyleBackColor = true;"), "btnProcess should have UseVisualStyleBackColor = true");
            Assert(content.Contains("this.panelAuth.BackColor = System.Drawing.SystemColors.Window;"), "panelAuth should use SystemColors.Window");
            Assert(content.Contains("this.panelDashboard.BackColor = System.Drawing.SystemColors.Control;"), "panelDashboard should use SystemColors.Control");

            Console.WriteLine("  [Verify] All SystemColors and native visual style properties verified in MainForm.Designer.cs.");
        }

        static void TestMainFormAccessKeys()
        {
            string designerPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "MainForm.Designer.cs"));
            if (!File.Exists(designerPath))
            {
                designerPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "CollationModifier", "MainForm.Designer.cs"));
            }
            if (!File.Exists(designerPath))
            {
                designerPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "CollationModifier", "MainForm.Designer.cs"));
            }

            Assert(File.Exists(designerPath), $"MainForm.Designer.cs not found at path: {designerPath}");
            string content = File.ReadAllText(designerPath);

            Assert(content.Contains("this.lblServerName.Text = \"&Server Name\";"), "lblServerName should have shortcut &Server Name");
            Assert(content.Contains("this.lblAuthType.Text = \"&Authentication Type\";"), "lblAuthType should have shortcut &Authentication Type");
            Assert(content.Contains("this.lblUsername.Text = \"&Username\";"), "lblUsername should have shortcut &Username");
            Assert(content.Contains("this.lblPassword.Text = \"Pass&word\";"), "lblPassword should have shortcut Pass&word");
            Assert(content.Contains("this.btnConnect.Text = \"&Connect\";"), "btnConnect should have shortcut &Connect");
            Assert(content.Contains("this.lblSelectDb.Text = \"Select &Database\";"), "lblSelectDb should have shortcut Select &Database");
            Assert(content.Contains("this.lblTargetCollation.Text = \"&New Collation Name\";"), "lblTargetCollation should have shortcut &New Collation Name");
            Assert(content.Contains("this.lblBackupDir.Text = \"&Backup Directory\";"), "lblBackupDir should have shortcut &Backup Directory");
            Assert(content.Contains("this.btnProcess.Text = \"&Process Collation Change\";"), "btnProcess should have shortcut &Process Collation Change");

            Console.WriteLine("  [Verify] All WinForms access keys (&) verified in MainForm.Designer.cs.");
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
