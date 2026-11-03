using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CollationModifier.Services;

namespace CollationModifier
{
    public partial class App : Application
    {
        private readonly LoggerService _logger;
        private static bool _handlersRegistered;
        private static readonly object _syncLock = new object();

        public LoggerService Logger => _logger;

        public static Func<string, string, bool>? InteractiveRecoveryPromptHandler { get; set; }
        public static Action? ShutdownHandler { get; set; }

        public App() : this(new LoggerService())
        {
        }

        public App(LoggerService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            RegisterExceptionHandlers();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            RegisterExceptionHandlers();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _logger?.Flush();
                _logger?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during application exit cleanup: {ex.Message}");
            }
            base.OnExit(e);
        }

        public void RegisterExceptionHandlers()
        {
            lock (_syncLock)
            {
                if (_handlersRegistered) return;

                this.DispatcherUnhandledException += OnDispatcherUnhandledException;
                AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

                _handlersRegistered = true;
            }
        }

        public void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = GlobalExceptionHandler.HandleDispatcherException(
                e.Exception,
                _logger,
                InteractiveRecoveryPromptHandler ?? DefaultPromptHandler,
                ShutdownHandler ?? (() => Shutdown()));
        }

        public void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            GlobalExceptionHandler.HandleAppDomainException(e.ExceptionObject, e.IsTerminating, _logger);
        }

        public void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            GlobalExceptionHandler.HandleUnobservedTaskException(e.Exception, () => e.SetObserved(), _logger);
        }

        private static bool DefaultPromptHandler(string message, string caption)
        {
            var result = MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }
    }
}
