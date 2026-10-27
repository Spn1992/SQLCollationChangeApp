using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CollationModifier.Services
{
    public class LoggerService : IDisposable
    {
        private readonly string _logFilePath;
        private readonly Channel<string> _logChannel;
        private readonly Task _processQueueTask;
        private readonly CancellationTokenSource _cts;
        private bool _disposed;

        public LoggerService() : this(null)
        {
        }

        public LoggerService(string? customLogFilePath)
        {
            if (!string.IsNullOrWhiteSpace(customLogFilePath))
            {
                _logFilePath = customLogFilePath;
                try
                {
                    var directory = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create log directory: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var appDirectory = Path.Combine(appDataPath, "CollationModifier");
                    Directory.CreateDirectory(appDirectory);
                    _logFilePath = Path.Combine(appDirectory, $"log_{DateTime.Now:yyyyMMdd}.txt");
                }
                catch (Exception ex)
                {
                    _logFilePath = $"log_{DateTime.Now:yyyyMMdd}.txt";
                    System.Diagnostics.Debug.WriteLine($"Failed to create app log directory: {ex.Message}");
                }
            }

            var options = new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            };
            _logChannel = Channel.CreateUnbounded<string>(options);
            _cts = new CancellationTokenSource();
            _processQueueTask = Task.Run(ProcessQueueAsync);
        }

        public void LogInfo(string message)
        {
            Log("INFO", message);
        }

        public void LogWarning(string message)
        {
            Log("WARN", message);
        }

        public void LogError(string message, Exception? ex = null)
        {
            Log("ERROR", $"{message}{(ex != null ? $" Exception: {ex.Message}\nStackTrace: {ex.StackTrace}" : "")}");
        }

        private void Log(string level, string message)
        {
            try
            {
                var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                if (!_logChannel.Writer.TryWrite(logEntry))
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to log: {level} - {message}");
                }
            }
            catch
            {
                // Fallback for logger failure (e.g., debug output)
                System.Diagnostics.Debug.WriteLine($"Failed to log: {level} - {message}");
            }
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                while (await _logChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (_logChannel.Reader.TryRead(out var logEntry))
                    {
                        WriteLogToFile(logEntry);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logger background worker encountered error: {ex.Message}");
            }
        }

        private void WriteLogToFile(string logEntry)
        {
            try
            {
                File.AppendAllText(_logFilePath, logEntry);
            }
            catch (Exception ex)
            {
                // Fallback for logger failure (e.g., file access error) so worker loop continues running
                System.Diagnostics.Debug.WriteLine($"Failed to write log entry to file: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _logChannel.Writer.TryComplete();

                try
                {
                    _processQueueTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error while flushing logger queue: {ex.Message}");
                }

                _cts.Cancel();
                _cts.Dispose();
            }

            _disposed = true;
        }
    }
}
