using System;
using System.Globalization;
using System.IO;
using System.Text;
using SsmsToolkit.Presentation.Settings;

namespace SsmsToolkit.Core
{
    /// <summary>
    /// Minimal, thread-safe file logger. Never throws: logging must not be able to break SSMS.
    /// Never pass passwords or connection strings with credentials to this class.
    /// </summary>
    internal static class Logger
    {
        private const int RetentionDays = 14;
        private static readonly object Sync = new object();
        private static bool _cleaned;

        public static string RootDirectory { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SsmsToolkit");

        public static string LogDirectory { get; } = Path.Combine(RootDirectory, "logs");

        public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        public static string CurrentLogFile =>
            Path.Combine(LogDirectory, "ssmstoolkit-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

        public static void Debug(string message) => Write(LogLevel.Debug, message, null);

        public static void Info(string message) => Write(LogLevel.Info, message, null);

        public static void Warn(string message, Exception ex = null) => Write(LogLevel.Warning, message, ex);

        public static void Error(string message, Exception ex = null) => Write(LogLevel.Error, message, ex);

        private static void Write(LogLevel level, string message, Exception ex)
        {
            if (level > MinimumLevel)
            {
                return;
            }

            try
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                  .Append(' ').Append(Label(level))
                  .Append(" [").Append(System.Threading.Thread.CurrentThread.ManagedThreadId).Append("] ")
                  .Append(message);
                if (ex != null)
                {
                    sb.AppendLine().Append(ex);
                }
                sb.AppendLine();

                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(CurrentLogFile, sb.ToString(), Encoding.UTF8);
                    if (!_cleaned)
                    {
                        _cleaned = true;
                        DeleteOldLogs();
                    }
                }
            }
            catch
            {
                // Swallow: a logging failure must never surface in SSMS.
            }
        }

        private static string Label(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error: return "ERROR";
                case LogLevel.Warning: return "WARN ";
                case LogLevel.Debug: return "DEBUG";
                default: return "INFO ";
            }
        }

        private static void DeleteOldLogs()
        {
            foreach (var file in Directory.GetFiles(LogDirectory, "ssmstoolkit-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-RetentionDays))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
