using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CollationModifier.Services;

namespace CollationModifier.Tests
{
    public static class GlobalExceptionHandlerTests
    {
        public static async Task RunAllTestsAsync()
        {
            Console.WriteLine("\n=== Running GlobalExceptionHandler & Centralized WPF Exception Pipeline Tests ===");

            await Test_DispatcherUnhandledException_LogsToLoggerService_AndContinuesWhenUserChoosesYes();
            Console.WriteLine("PASS: Test_DispatcherUnhandledException_LogsToLoggerService_AndContinuesWhenUserChoosesYes");

            await Test_DispatcherUnhandledException_LogsToLoggerService_AndInitiatesShutdownWhenUserChoosesNo();
            Console.WriteLine("PASS: Test_DispatcherUnhandledException_LogsToLoggerService_AndInitiatesShutdownWhenUserChoosesNo");

            await Test_FatalExceptions_AreNotSuppressed_AndLoggedAsFatal();
            Console.WriteLine("PASS: Test_FatalExceptions_AreNotSuppressed_AndLoggedAsFatal");

            Test_SqlCredentialsAndPasswords_AreSanitizedInDialog();
            Console.WriteLine("PASS: Test_SqlCredentialsAndPasswords_AreSanitizedInDialog");

            await Test_AppDomainUnhandledException_LogsStackTraceAndFlushesWithin5Seconds();
            Console.WriteLine("PASS: Test_AppDomainUnhandledException_LogsStackTraceAndFlushesWithin5Seconds");

            await Test_UnobservedTaskException_LogsAndMarksObserved();
            Console.WriteLine("PASS: Test_UnobservedTaskException_LogsAndMarksObserved");

            Test_AppXamlCs_ConfigurationAndRegistrationVerification();
            Console.WriteLine("PASS: Test_AppXamlCs_ConfigurationAndRegistrationVerification");
        }

        private static async Task Test_DispatcherUnhandledException_LogsToLoggerService_AndContinuesWhenUserChoosesYes()
        {
            string logPath = Path.Combine(Path.GetTempPath(), $"test_dispatcher_yes_{Guid.NewGuid():N}.txt");
            try
            {
                using var logger = new LoggerService(logPath);
                string testErrorMessage = "Test UI dispatch error with stack trace info";
                Exception ex;
                try
                {
                    throw new InvalidOperationException(testErrorMessage);
                }
                catch (Exception caughtEx)
                {
                    ex = caughtEx;
                }

                bool promptInvoked = false;
                bool handled = GlobalExceptionHandler.HandleDispatcherException(
                    ex,
                    logger,
                    (message, caption) =>
                    {
                        promptInvoked = true;
                        Assert(message.Contains(testErrorMessage), "Prompt should display the error message");
                        Assert(caption == "Application Error - Recovery Option", "Caption should match standard recovery title");
                        return true; // User clicks Yes to continue
                    },
                    shutdownAction: null);

                Assert(promptInvoked, "Interactive prompt handler should have been invoked");
                Assert(handled, "HandleDispatcherException should return true when user elects to continue");

                logger.Flush();
                Assert(File.Exists(logPath), "Log file should exist on disk");

                string[] lines = File.ReadAllLines(logPath);
                Assert(lines.Any(l => l.Contains("[ERROR]") && l.Contains(testErrorMessage)), "Log file must record the UI exception message");
                Assert(lines.Any(l => l.Contains("StackTrace:")), "Log file must record stack trace");
                Assert(lines.Any(l => l.Contains("[INFO]") && l.Contains("User elected to continue")), "Log file must record user continuation decision");
            }
            finally
            {
                if (File.Exists(logPath)) File.Delete(logPath);
            }
        }

        private static async Task Test_DispatcherUnhandledException_LogsToLoggerService_AndInitiatesShutdownWhenUserChoosesNo()
        {
            string logPath = Path.Combine(Path.GetTempPath(), $"test_dispatcher_no_{Guid.NewGuid():N}.txt");
            try
            {
                using var logger = new LoggerService(logPath);
                string testErrorMessage = "Non-recoverable UI event failure";
                var ex = new InvalidOperationException(testErrorMessage);

                bool promptInvoked = false;
                bool shutdownCalled = false;

                bool handled = GlobalExceptionHandler.HandleDispatcherException(
                    ex,
                    logger,
                    (message, caption) =>
                    {
                        promptInvoked = true;
                        return false; // User clicks No to close the application
                    },
                    shutdownAction: () =>
                    {
                        shutdownCalled = true;
                    });

                Assert(promptInvoked, "Interactive prompt handler should have been invoked");
                Assert(shutdownCalled, "Shutdown action should have been invoked when user selected No");
                Assert(handled, "HandleDispatcherException should return true when shutdown is handled cleanly");

                Assert(File.Exists(logPath), "Log file should exist on disk");
                string[] lines = File.ReadAllLines(logPath);
                Assert(lines.Any(l => l.Contains("[ERROR]") && l.Contains(testErrorMessage)), "Log file must record the exception");
                Assert(lines.Any(l => l.Contains("[INFO]") && l.Contains("User elected to exit")), "Log file must record user exit decision");
            }
            finally
            {
                if (File.Exists(logPath)) File.Delete(logPath);
            }
        }

        private static async Task Test_FatalExceptions_AreNotSuppressed_AndLoggedAsFatal()
        {
            string logPath = Path.Combine(Path.GetTempPath(), $"test_fatal_oom_{Guid.NewGuid():N}.txt");
            try
            {
                using var logger = new LoggerService(logPath);

                // 1. Direct OutOfMemoryException
                var oom = new OutOfMemoryException("Memory allocation failed");
                bool promptInvoked = false;
                bool handled = GlobalExceptionHandler.HandleDispatcherException(
                    oom,
                    logger,
                    (m, c) => { promptInvoked = true; return true; });

                Assert(!promptInvoked, "Prompt should NOT be shown for fatal OutOfMemoryException");
                Assert(!handled, "Fatal OutOfMemoryException must NOT be suppressed (handled must be false)");

                // 2. AggregateException wrapping OutOfMemoryException
                var aggOom = new AggregateException(new OutOfMemoryException("Nested OOM"));
                bool aggHandled = GlobalExceptionHandler.HandleDispatcherException(
                    aggOom,
                    logger,
                    (m, c) => true);

                Assert(!aggHandled, "Nested OutOfMemoryException in AggregateException must NOT be suppressed");

                logger.Flush();
                string[] lines = File.ReadAllLines(logPath);
                Assert(lines.Any(l => l.Contains("Fatal UI thread exception (OutOfMemoryException)")), "Fatal OOM must be logged as fatal error");
            }
            finally
            {
                if (File.Exists(logPath)) File.Delete(logPath);
            }
        }

        private static void Test_SqlCredentialsAndPasswords_AreSanitizedInDialog()
        {
            string rawMessageWithCredentials =
                "Connection failed: Server=sql.corp.internal;Database=CustomerDb;User ID=admin_sa;Password=SuperSecretP@ssw0rd!;Timeout=30";

            string sanitized = GlobalExceptionHandler.SanitizeExceptionMessage(rawMessageWithCredentials);

            Assert(!sanitized.Contains("SuperSecretP@ssw0rd!"), "Exception message must NOT leak password in plain text");
            Assert(sanitized.Contains("Password=********"), "Password must be masked with asterisks");
            Assert(sanitized.Contains("User ID=********"), "User ID credentials must be masked with asterisks");

            // Also test key: value format
            string rawKeyValue = "Failed authentication: Password: TopSecretValue123, User: prod_user";
            string sanitizedKeyValue = GlobalExceptionHandler.SanitizeExceptionMessage(rawKeyValue);
            Assert(!sanitizedKeyValue.Contains("TopSecretValue123"), "Key-value password must NOT leak in plain text");
            Assert(sanitizedKeyValue.Contains("Password: ********"), "Key-value password must be masked");

            // Verify dialog prompt text generation also sanitizes credentials
            var ex = new Exception(rawMessageWithCredentials);
            using var logger = new LoggerService();
            string capturedPrompt = string.Empty;
            GlobalExceptionHandler.HandleDispatcherException(
                ex,
                logger,
                (msg, cap) =>
                {
                    capturedPrompt = msg;
                    return true;
                });

            Assert(!capturedPrompt.Contains("SuperSecretP@ssw0rd!"), "Dialog prompt text must NOT contain SQL password");
            Assert(capturedPrompt.Contains("Password=********"), "Dialog prompt text must contain masked password");
        }

        private static async Task Test_AppDomainUnhandledException_LogsStackTraceAndFlushesWithin5Seconds()
        {
            string logPath = Path.Combine(Path.GetTempPath(), $"test_appdomain_{Guid.NewGuid():N}.txt");
            try
            {
                using var logger = new LoggerService(logPath);
                Exception testEx;
                try
                {
                    throw new ApplicationException("Background thread crashed unexpectedly");
                }
                catch (Exception caught)
                {
                    testEx = caught;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                GlobalExceptionHandler.HandleAppDomainException(testEx, isTerminating: true, logger);
                sw.Stop();

                Assert(sw.ElapsedMilliseconds < 5000, "Flush during AppDomain shutdown must complete within 5 seconds");
                Assert(File.Exists(logPath), "Log file should exist on disk after AppDomain flush");

                string[] lines = File.ReadAllLines(logPath);
                Assert(lines.Any(l => l.Contains("[ERROR]") && l.Contains("Unhandled AppDomain exception encountered. IsTerminating=True")), "AppDomain error must be logged with terminating status");
                Assert(lines.Any(l => l.Contains("Background thread crashed unexpectedly")), "Exception message must be in log file");
                Assert(lines.Any(l => l.Contains("StackTrace:")), "Stack trace must be present in log file");
            }
            finally
            {
                if (File.Exists(logPath)) File.Delete(logPath);
            }
        }

        private static async Task Test_UnobservedTaskException_LogsAndMarksObserved()
        {
            string logPath = Path.Combine(Path.GetTempPath(), $"test_unobserved_task_{Guid.NewGuid():N}.txt");
            try
            {
                using var logger = new LoggerService(logPath);
                var inner = new InvalidOperationException("Task failed asynchronously in background");
                var aggEx = new AggregateException("Task faulted", inner);

                bool observed = false;
                GlobalExceptionHandler.HandleUnobservedTaskException(
                    aggEx,
                    () => { observed = true; },
                    logger);

                Assert(observed, "SetObserved action must be invoked to mark task exception as observed");

                logger.Flush();
                string[] lines = File.ReadAllLines(logPath);
                Assert(lines.Any(l => l.Contains("[ERROR]") && l.Contains("Unobserved task exception caught by TaskScheduler pipeline")), "TaskScheduler unobserved exception must be logged");
                Assert(lines.Any(l => l.Contains("Task failed asynchronously in background")), "Inner task exception must be logged");
            }
            finally
            {
                if (File.Exists(logPath)) File.Delete(logPath);
            }
        }

        private static void Test_AppXamlCs_ConfigurationAndRegistrationVerification()
        {
            string appXamlCsPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CollationModifier", "App.xaml.cs"));
            if (!File.Exists(appXamlCsPath))
            {
                appXamlCsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "CollationModifier", "App.xaml.cs"));
            }
            if (!File.Exists(appXamlCsPath))
            {
                appXamlCsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "CollationModifier", "App.xaml.cs"));
            }

            Assert(File.Exists(appXamlCsPath), $"App.xaml.cs file should exist at path {appXamlCsPath}");

            string code = File.ReadAllText(appXamlCsPath);

            Assert(code.Contains("this.DispatcherUnhandledException += OnDispatcherUnhandledException;"), "App.xaml.cs must register DispatcherUnhandledException");
            Assert(code.Contains("AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;"), "App.xaml.cs must register AppDomain.CurrentDomain.UnhandledException");
            Assert(code.Contains("TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;"), "App.xaml.cs must register TaskScheduler.UnobservedTaskException");
            Assert(code.Contains("GlobalExceptionHandler.HandleDispatcherException"), "App.xaml.cs must invoke dispatcher exception handler");
            Assert(code.Contains("GlobalExceptionHandler.HandleAppDomainException"), "App.xaml.cs must invoke AppDomain exception handler");
            Assert(code.Contains("GlobalExceptionHandler.HandleUnobservedTaskException"), "App.xaml.cs must invoke unobserved task exception handler");
            Assert(code.Contains("e.SetObserved()"), "App.xaml.cs must mark unobserved task exception as observed");
            Assert(code.Contains("MessageBox.Show"), "App.xaml.cs must have default message box prompt for user recovery");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Assertion failed: {message}");
            }
        }
    }
}
