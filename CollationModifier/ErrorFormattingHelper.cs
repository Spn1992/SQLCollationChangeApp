using System;
using System.Text;

namespace CollationModifier
{
    public static class ErrorFormattingHelper
    {
        public static string GetSafeErrorMessage(Exception? ex)
        {
            if (ex == null) return "An unknown error occurred.";

            string message = ex.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = ex.GetType().Name;
            }

            if (ex.InnerException != null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
            {
                message += $" ({ex.InnerException.Message})";
            }

            return message;
        }

        public static string GetDetailedErrorText(Exception? ex)
        {
            if (ex == null) return "An unknown error occurred.";

            var sb = new StringBuilder();
            sb.AppendLine($"Error Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");

            if (ex is AggregateException aggEx)
            {
                var innerExceptions = aggEx.Flatten().InnerExceptions;
                if (innerExceptions.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Inner Exceptions:");
                    int index = 1;
                    foreach (var inner in innerExceptions)
                    {
                        sb.AppendLine($"  [{index++}] {inner.GetType().FullName}: {inner.Message}");
                        if (!string.IsNullOrWhiteSpace(inner.StackTrace))
                        {
                            sb.AppendLine($"      Stack Trace:\n{inner.StackTrace}");
                        }
                    }
                }
            }
            else if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine("Inner Exception:");
                sb.AppendLine($"  {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                if (!string.IsNullOrWhiteSpace(ex.InnerException.StackTrace))
                {
                    sb.AppendLine($"  Stack Trace:\n{ex.InnerException.StackTrace}");
                }
            }

            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                sb.AppendLine();
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(ex.StackTrace);
            }

            return sb.ToString().TrimEnd();
        }

        public static (string StatusText, string ToolTipText, string DetailedError) FormatConnectionError(Exception ex)
        {
            string summary = GetSafeErrorMessage(ex);
            string detailed = GetDetailedErrorText(ex);
            return ("Connection error occurred.", $"Error: {summary}", detailed);
        }

        public static (string StatusText, string ToolTipText, string DetailedError) FormatLoadDatabasesError(Exception ex)
        {
            string summary = GetSafeErrorMessage(ex);
            string detailed = GetDetailedErrorText(ex);
            return ("Failed to load databases.", $"Failed to load databases: {summary}", detailed);
        }

        public static bool IsSingleUserLockState(Exception? ex)
        {
            if (ex == null) return false;
            if (ex is AggregateException aggEx)
            {
                foreach (var inner in aggEx.Flatten().InnerExceptions)
                {
                    if (IsSingleUserLockState(inner)) return true;
                }
            }
            string msg = ex.Message ?? string.Empty;
            return msg.IndexOf("SINGLE_USER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("reset failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("emergency MULTI_USER reset", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static (string StatusText, string ToolTipText, string DetailedError) FormatProcessError(Exception ex, string? backupFile, Exception? restoreEx = null, bool restoreAttempted = false)
        {
            string primarySummary = GetSafeErrorMessage(ex);
            string detailedPrimary = GetDetailedErrorText(ex);
            bool isSingleUserLocked = IsSingleUserLockState(ex) || IsSingleUserLockState(restoreEx);

            if (backupFile != null)
            {
                if (restoreAttempted)
                {
                    if (restoreEx != null)
                    {
                        string restoreSummary = GetSafeErrorMessage(restoreEx);
                        string detailedRestore = GetDetailedErrorText(restoreEx);
                        string lockWarning = isSingleUserLocked ? " WARNING: Database remains locked in SINGLE_USER mode." : "";
                        string status = $"Process failed: {primarySummary}. CRITICAL: Auto-restore also failed: {restoreSummary}.{lockWarning}";
                        string detailed = $"Process failed: {primarySummary}\n\n=== Primary Error Details ===\n{detailedPrimary}\n\n=== CRITICAL: Auto-restore failed ({backupFile}) ===\n{detailedRestore}\n\n{lockWarning.Trim()}";
                        return (status, status, detailed.Trim());
                    }
                    else
                    {
                        if (isSingleUserLocked)
                        {
                            string status = $"Process failed: {primarySummary}. WARNING: Database remains locked in SINGLE_USER mode.";
                            string detailed = $"Process failed: {primarySummary}\n\n=== Primary Error Details ===\n{detailedPrimary}\n\nWARNING: Database remains locked in SINGLE_USER mode.";
                            return (status, status, detailed);
                        }
                        else
                        {
                            string status = $"Process failed: {primarySummary}. Database restored to MULTI_USER mode successfully.";
                            string detailed = $"Process failed: {primarySummary}\n\n=== Primary Error Details ===\n{detailedPrimary}\n\nDatabase was restored to MULTI_USER mode successfully from backup: {backupFile}";
                            return (status, status, detailed);
                        }
                    }
                }
                else
                {
                    string status = $"Process failed: {primarySummary}. Attempting to restore from backup...";
                    string detailed = $"Process failed: {primarySummary}\n\n=== Primary Error Details ===\n{detailedPrimary}\n\nAttempting to restore from backup: {backupFile}...";
                    return (status, status, detailed);
                }
            }
            else
            {
                string lockWarning = isSingleUserLocked ? " WARNING: Database remains locked in SINGLE_USER mode." : "";
                string status = $"Process failed before backup: {primarySummary}.{lockWarning}";
                string detailed = $"Process failed before backup: {primarySummary}\n\n=== Error Details ===\n{detailedPrimary}\n\n{lockWarning.Trim()}";
                return (status, status, detailed.Trim());
            }
        }
    }
}
