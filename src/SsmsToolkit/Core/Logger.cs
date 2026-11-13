using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SsmsToolkit.Core
{
    /// <summary>
    /// Minimal, thread-safe file logger. Never throws: logging must not be able to break SSMS.
    /// Never pass passwords or connection strings with credentials to this class.
    /// </summary>
    internal static class Logger
    {
        private static readonly object Sync = new object();

        public static string RootDirectory { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SsmsToolkit");

        public static string LogDirectory { get; } = Path.Combine(RootDirectory, "logs");

        public static string CurrentLogFile =>
            Path.Combine(LogDirectory, "ssmstoolkit-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

        public static void Info(string message) => Write("INFO ", message, null);

        public static void Warn(string message, Exception ex = null) => Write("WARN ", message, ex);

        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                  .Append(' ').Append(level)
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
                }
            }
            catch
            {
                // Swallow: a logging failure must never surface in SSMS.
            }
        }
    }
}
