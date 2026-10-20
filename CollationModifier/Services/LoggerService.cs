using System;
using System.IO;

namespace CollationModifier.Services
{
    public class LoggerService
    {
        private readonly string _logFilePath;

        public LoggerService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDirectory = Path.Combine(appDataPath, "CollationModifier");
            Directory.CreateDirectory(appDirectory);
            _logFilePath = Path.Combine(appDirectory, $"log_{DateTime.Now:yyyyMMdd}.txt");
        }

        public void LogInfo(string message)
        {
            Log("INFO", message);
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
                File.AppendAllText(_logFilePath, logEntry);
            }
            catch
            {
                // Fallback for logger failure (e.g., debug output)
                System.Diagnostics.Debug.WriteLine($"Failed to log: {level} - {message}");
            }
        }
    }
}
