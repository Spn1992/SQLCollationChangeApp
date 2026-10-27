using System;
using CollationModifier;

namespace CollationModifier.Tests
{
    public static class MainFormTests
    {
        public static void Main()
        {
            Console.WriteLine("Running MainForm error handling & status tooltip unit tests...");

            TestGetSafeErrorMessage();
            TestFormatConnectionError();
            TestFormatLoadDatabasesError();
            TestFormatProcessError_BeforeBackup();
            TestFormatProcessError_WithRestoreSuccess();
            TestFormatProcessError_WithRestoreFailure();
            TestStatusBarLengthConstraints();

            Console.WriteLine("ALL TESTS PASSED SUCCESSFULLY!");
        }

        private static void TestGetSafeErrorMessage()
        {
            var ex1 = new InvalidOperationException("Database connection timeout");
            AssertEqual("Database connection timeout", ErrorFormattingHelper.GetSafeErrorMessage(ex1), "Normal exception");

            var ex2 = new Exception("");
            AssertEqual("Exception", ErrorFormattingHelper.GetSafeErrorMessage(ex2), "Empty message exception");

            var innerEx = new Exception("Disk write error");
            var ex3 = new InvalidOperationException("Backup failed", innerEx);
            AssertEqual("Backup failed (Disk write error)", ErrorFormattingHelper.GetSafeErrorMessage(ex3), "Nested inner exception");

            AssertEqual("An unknown error occurred.", ErrorFormattingHelper.GetSafeErrorMessage(null), "Null exception");
        }

        private static void TestFormatConnectionError()
        {
            var ex = new Exception("Login failed for user 'sa'");
            var (status, toolTip) = ErrorFormattingHelper.FormatConnectionError(ex);

            AssertEqual("Connection error occurred.", status, "Connection status text");
            AssertEqual("Error: Login failed for user 'sa'", toolTip, "Connection tooltip text");
        }

        private static void TestFormatLoadDatabasesError()
        {
            var ex = new Exception("Server communication failure");
            var (status, toolTip) = ErrorFormattingHelper.FormatLoadDatabasesError(ex);

            AssertEqual("Failed to load databases.", status, "Load databases status text");
            AssertEqual("Failed to load databases: Server communication failure", toolTip, "Load databases tooltip text");
        }

        private static void TestFormatProcessError_BeforeBackup()
        {
            var ex = new InvalidOperationException("Insufficient ALTER DATABASE permissions");
            var (status, toolTip, dialog) = ErrorFormattingHelper.FormatProcessError(ex, backupFile: null);

            AssertEqual("Process failed before backup.", status, "Process status before backup");
            AssertEqual("Process failed before backup: Insufficient ALTER DATABASE permissions", toolTip, "Process tooltip before backup");
            AssertEqual("Process failed before backup: Insufficient ALTER DATABASE permissions", dialog, "Process dialog before backup");
        }

        private static void TestFormatProcessError_WithRestoreSuccess()
        {
            var ex = new Exception("Cannot alter collation due to dependent view");
            string backupFile = @"C:\SQLBackups\TestDB.bak";

            var (status, toolTip, dialog) = ErrorFormattingHelper.FormatProcessError(ex, backupFile, restoreAttempted: true, restoreEx: null);

            AssertEqual("Process failed. Database restored successfully.", status, "Process status restore success");
            AssertEqual("Process failed: Cannot alter collation due to dependent view. Database restored successfully.", toolTip, "Process tooltip restore success");
            AssertContains(dialog, "Cannot alter collation due to dependent view", "Dialog contains primary error");
            AssertContains(dialog, "Database was restored successfully from backup", "Dialog contains restore confirmation");
        }

        private static void TestFormatProcessError_WithRestoreFailure()
        {
            var ex = new Exception("Syntax error in T-SQL command");
            var restoreEx = new Exception("Backup file corrupted on disk");
            string backupFile = @"C:\SQLBackups\TestDB.bak";

            var (status, toolTip, dialog) = ErrorFormattingHelper.FormatProcessError(ex, backupFile, restoreEx: restoreEx, restoreAttempted: true);

            AssertEqual("Process failed. Auto-restore failed.", status, "Process status restore failure");
            AssertEqual("Process failed: Syntax error in T-SQL command. CRITICAL: Auto-restore also failed: Backup file corrupted on disk", toolTip, "Process tooltip restore failure");
            AssertContains(dialog, "Syntax error in T-SQL command", "Dialog contains primary error");
            AssertContains(dialog, "CRITICAL: Auto-restore from backup also failed: Backup file corrupted on disk", "Dialog contains critical restore failure details");
        }

        private static void TestStatusBarLengthConstraints()
        {
            var longEx = new Exception("A very long exception message that describes a complex multi-line SQL Server internal error with stack trace details and table lock timeout messages.");
            var restoreEx = new Exception("Another lengthy error description explaining why the automatic database restore operation could not complete.");

            var (statusConn, _) = ErrorFormattingHelper.FormatConnectionError(longEx);
            var (statusLoad, _) = ErrorFormattingHelper.FormatLoadDatabasesError(longEx);
            var (statusNoBack, _, _) = ErrorFormattingHelper.FormatProcessError(longEx, null);
            var (statusRestoreOk, _, _) = ErrorFormattingHelper.FormatProcessError(longEx, "file.bak", restoreAttempted: true);
            var (statusRestoreFail, _, _) = ErrorFormattingHelper.FormatProcessError(longEx, "file.bak", restoreEx: restoreEx, restoreAttempted: true);

            // Assert that all status texts stay under 60 characters to fit on 22px fixed status bar
            AssertTrue(statusConn.Length < 60, $"Connection status length ({statusConn.Length}) < 60");
            AssertTrue(statusLoad.Length < 60, $"Load databases status length ({statusLoad.Length}) < 60");
            AssertTrue(statusNoBack.Length < 60, $"No-backup status length ({statusNoBack.Length}) < 60");
            AssertTrue(statusRestoreOk.Length < 60, $"Restore-OK status length ({statusRestoreOk.Length}) < 60");
            AssertTrue(statusRestoreFail.Length < 60, $"Restore-FAIL status length ({statusRestoreFail.Length}) < 60");
        }

        private static void AssertEqual(string expected, string actual, string scenario)
        {
            if (expected != actual)
            {
                throw new Exception($"[FAILED] {scenario}: Expected '{expected}' but got '{actual}'");
            }
            Console.WriteLine($"[PASS] {scenario}");
        }

        private static void AssertContains(string text, string substring, string scenario)
        {
            if (!text.Contains(substring))
            {
                throw new Exception($"[FAILED] {scenario}: Expected '{text}' to contain '{substring}'");
            }
            Console.WriteLine($"[PASS] {scenario}");
        }

        private static void AssertTrue(bool condition, string scenario)
        {
            if (!condition)
            {
                throw new Exception($"[FAILED] {scenario}: Condition returned false");
            }
            Console.WriteLine($"[PASS] {scenario}");
        }
    }
}
