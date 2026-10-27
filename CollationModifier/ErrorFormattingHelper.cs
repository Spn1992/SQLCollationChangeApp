using System;

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

        public static (string StatusText, string ToolTipText) FormatConnectionError(Exception ex)
        {
            string details = GetSafeErrorMessage(ex);
            return ("Connection error occurred.", $"Error: {details}");
        }

        public static (string StatusText, string ToolTipText) FormatLoadDatabasesError(Exception ex)
        {
            string details = GetSafeErrorMessage(ex);
            return ("Failed to load databases.", $"Failed to load databases: {details}");
        }

        public static (string StatusText, string ToolTipText, string DialogMessage) FormatProcessError(Exception ex, string? backupFile, Exception? restoreEx = null, bool restoreAttempted = false)
        {
            string primaryError = GetSafeErrorMessage(ex);

            if (backupFile != null)
            {
                if (restoreAttempted)
                {
                    if (restoreEx != null)
                    {
                        string restoreError = GetSafeErrorMessage(restoreEx);
                        return (
                            "Process failed. Auto-restore failed.",
                            $"Process failed: {primaryError}. CRITICAL: Auto-restore also failed: {restoreError}",
                            $"Process failed: {primaryError}\n\nCRITICAL: Auto-restore from backup also failed: {restoreError}"
                        );
                    }
                    else
                    {
                        return (
                            "Process failed. Database restored successfully.",
                            $"Process failed: {primaryError}. Database restored successfully.",
                            $"Process failed: {primaryError}\n\nDatabase was restored successfully from backup."
                        );
                    }
                }
                else
                {
                    return (
                        "Process failed. Restoring from backup...",
                        $"Process failed: {primaryError}. Attempting to restore from backup...",
                        ""
                    );
                }
            }
            else
            {
                return (
                    "Process failed before backup.",
                    $"Process failed before backup: {primaryError}",
                    $"Process failed before backup: {primaryError}"
                );
            }
        }
    }
}
