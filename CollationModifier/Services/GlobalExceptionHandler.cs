using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CollationModifier.Services
{
    public static class GlobalExceptionHandler
    {
        public static string SanitizeExceptionMessage(string? message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;

            // Mask password/pwd parameter in connection strings
            var sanitized = Regex.Replace(
                message,
                @"(?i)(password|pwd)\s*=\s*['""]?([^;'""]+)['""]?",
                "$1=********");

            // Mask password in key-value format (e.g. Password: ...)
            sanitized = Regex.Replace(
                sanitized,
                @"(?i)(password|pwd)\s*:\s*['""]?([^,\r\n'""]+)['""]?",
                "$1: ********");

            // Mask User ID / UID in connection strings
            sanitized = Regex.Replace(
                sanitized,
                @"(?i)(user\s*id|uid)\s*=\s*['""]?([^;'""]+)['""]?",
                "$1=********");

            return sanitized;
        }

        public static bool IsFatalException(Exception? ex)
        {
            if (ex == null) return false;

            if (ex is OutOfMemoryException
                || ex is StackOverflowException
                || ex is AccessViolationException)
            {
                return true;
            }

            if (ex is AggregateException aggEx)
            {
                foreach (var inner in aggEx.Flatten().InnerExceptions)
                {
                    if (IsFatalException(inner)) return true;
                }
            }

            if (ex.InnerException != null && IsFatalException(ex.InnerException))
            {
                return true;
            }

            return false;
        }

        public static bool HandleDispatcherException(
            Exception ex,
            LoggerService logger,
            Func<string, string, bool>? promptHandler = null,
            Action? shutdownAction = null)
        {
            if (ex == null) return true;

            if (IsFatalException(ex))
            {
                logger.LogError($"Fatal UI thread exception ({ex.GetType().Name}) encountered. The exception will not be suppressed.", ex);
                FlushLogger(logger, TimeSpan.FromSeconds(5));
                return false;
            }

            logger.LogError("Unhandled UI thread exception caught by DispatcherUnhandledException pipeline.", ex);

            string safeMessage = SanitizeExceptionMessage(ex.Message);
            string dialogMessage =
                $"An unexpected application error occurred:\n\n{safeMessage}\n\n" +
                "Would you like to attempt to continue working?\n" +
                "Click 'Yes' to continue your session, or 'No' to close the application.";
            string dialogCaption = "Application Error - Recovery Option";

            bool shouldContinue = promptHandler?.Invoke(dialogMessage, dialogCaption) ?? false;

            if (shouldContinue)
            {
                logger.LogInfo("User elected to continue application session after unhandled UI exception.");
                return true;
            }
            else
            {
                logger.LogInfo("User elected to exit application after unhandled UI exception. Initiating shutdown.");
                FlushLogger(logger, TimeSpan.FromSeconds(5));
                if (shutdownAction != null)
                {
                    shutdownAction.Invoke();
                    return true;
                }
                return false;
            }
        }

        public static void HandleAppDomainException(
            object? exceptionObject,
            bool isTerminating,
            LoggerService logger)
        {
            var ex = exceptionObject as Exception ?? new Exception($"Unhandled AppDomain error: {exceptionObject}");
            logger.LogError($"Unhandled AppDomain exception encountered. IsTerminating={isTerminating}", ex);
            FlushLogger(logger, TimeSpan.FromSeconds(5));
        }

        public static void HandleUnobservedTaskException(
            AggregateException? exception,
            Action? setObservedAction,
            LoggerService logger)
        {
            logger.LogError("Unobserved task exception caught by TaskScheduler pipeline.", exception);
            try
            {
                setObservedAction?.Invoke();
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to mark unobserved task exception as observed.", ex);
            }
        }

        public static void FlushLogger(LoggerService logger, TimeSpan timeout)
        {
            try
            {
                logger.Flush(timeout);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to flush logger: {ex.Message}");
            }
        }
    }
}
