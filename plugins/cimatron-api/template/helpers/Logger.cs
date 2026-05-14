using System;
using System.Diagnostics;
using System.IO;

namespace ApiName.Helpers
{
    public static class Logger
    {
        private static readonly object LogFileLock = new object();

        public static bool LogExternal { get; set; } = true;

        public static string LogFilePath =>
            Path.Combine(GetDownloadsDirectory(), "ApiName.log.txt");

        public static void SetExternalLogging(bool enabled) => LogExternal = enabled;

        public static void LogInfo(string message) => WriteLog("INFO", message);
        public static void LogWarning(string message) => WriteLog("WARNING", message);
        public static void LogError(string message) => WriteLog("ERROR", message);

        public static void LogException(Exception ex, string contextMessage = "")
        {
            var message = string.IsNullOrWhiteSpace(contextMessage)
                ? $"{ex.Message}{Environment.NewLine}{ex.StackTrace}"
                : $"{contextMessage} - {ex.Message}{Environment.NewLine}{ex.StackTrace}";

            WriteLog("EXCEPTION", message);
        }

        private static void WriteLog(string level, string message)
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {level}: {message}";
            Debug.WriteLine(logEntry);

            if (!LogExternal) return;

            try
            {
                var logDirectory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                lock (LogFileLock)
                {
                    File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
                }
            }
            catch (Exception writeException)
            {
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] LOGGER ERROR: Failed to write log file - {writeException.Message}");
            }
        }

        private static string GetDownloadsDirectory()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "Downloads");
        }
    }
}
