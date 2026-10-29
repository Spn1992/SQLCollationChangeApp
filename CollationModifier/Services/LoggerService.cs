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

        public string LogFilePath => _logFilePath;

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
            StreamWriter? writer = null;

            try
            {
                while (await _logChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (_logChannel.Reader.TryRead(out var logEntry))
                    {
                        if (writer == null)
                        {
                            writer = CreateStreamWriter();
                        }

                        if (writer != null)
                        {
                            try
                            {
                                await writer.WriteAsync(logEntry).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to write log entry: {ex.Message}");
                                DisposeWriter(ref writer);
                            }
                        }
                    }

                    if (writer != null)
                    {
                        try
                        {
                            await writer.FlushAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to flush log stream: {ex.Message}");
                            DisposeWriter(ref writer);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logger background worker encountered error: {ex.Message}");
            }
            finally
            {
                while (_logChannel.Reader.TryRead(out var logEntry))
                {
                    if (writer == null)
                    {
                        writer = CreateStreamWriter();
                    }

                    if (writer != null)
                    {
                        try
                        {
                            await writer.WriteAsync(logEntry).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to write remaining log entry during cleanup: {ex.Message}");
                            DisposeWriter(ref writer);
                        }
                    }
                }

                if (writer != null)
                {
                    try
                    {
                        await writer.FlushAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error flushing log stream during cleanup: {ex.Message}");
                    }
                    finally
                    {
                        DisposeWriter(ref writer);
                    }
                }
            }
        }

        private StreamWriter? CreateStreamWriter()
        {
            try
            {
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var fileStream = new FileStream(
                    _logFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    bufferSize: 4096,
                    useAsync: true);

                return new StreamWriter(fileStream, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create StreamWriter for log file '{_logFilePath}': {ex.Message}");
                return null;
            }
        }

        private static void DisposeWriter(ref StreamWriter? writer)
        {
            if (writer != null)
            {
                try
                {
                    writer.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing StreamWriter: {ex.Message}");
                }
                finally
                {
                    writer = null;
                }
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
