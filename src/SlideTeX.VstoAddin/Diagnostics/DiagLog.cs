// SlideTeX Note: Diagnostics logging helper for troubleshooting add-in runtime behavior.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SlideTeX.VstoAddin.Diagnostics
{
    /// <summary>
    /// File-based diagnostics sink designed to be failure-tolerant in production add-in paths.
    /// </summary>
    internal static class DiagLog
    {
        private static readonly object SyncRoot = new object();
        // Enable verbose diagnostics only when explicitly requested by env var.
        private static readonly bool DebugEnabled = ResolveDebugEnabled();
        private static string _logFilePath;

        /// <summary>
        /// Writes an informational log entry.
        /// </summary>
        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        /// <summary>
        /// Writes a warning log entry.
        /// </summary>
        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        /// <summary>
        /// Writes a debug log entry when debug logging is enabled by environment variable.
        /// </summary>
        public static void Debug(string message)
        {
            if (!DebugEnabled)
            {
                return;
            }

            Write("DEBUG", message, null);
        }

        /// <summary>
        /// Writes an error entry including exception details.
        /// </summary>
        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message, ex);
        }

        public static string CurrentLogFilePath
        {
            get { return EnsureLogFilePath(); }
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                var line = BuildLine(level, message, ex);
                var path = EnsureLogFilePath();

                lock (SyncRoot)
                {
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Never throw from diagnostics path.
            }
        }

        private static string BuildLine(string level, string message, Exception ex)
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var threadId = Thread.CurrentThread.ManagedThreadId;
            var pid = Process.GetCurrentProcess().Id;
            var baseMessage = string.Format("[{0}] [{1}] [pid:{2}] [tid:{3}] {4}", now, level, pid, threadId, message);

            if (ex == null)
            {
                return baseMessage;
            }

            var details = string.Format(
                "{0} | ex={1}: {2} | hresult=0x{3:X8} | stack={4}",
                baseMessage,
                ex.GetType().FullName,
                ex.Message,
                ex.HResult,
                ex.StackTrace);

            return details;
        }

        private static string EnsureLogFilePath()
        {
            if (!string.IsNullOrWhiteSpace(_logFilePath))
            {
                return _logFilePath;
            }

            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.GetTempPath();
            }

            var logDir = Path.Combine(root, "SlideTeX", "logs");
            Directory.CreateDirectory(logDir);

            _logFilePath = Path.Combine(logDir, "vsto-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            return _logFilePath;
        }

        private static bool ResolveDebugEnabled()
        {
            var raw = Environment.GetEnvironmentVariable("SLIDETEX_LOG_DEBUG");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            raw = raw.Trim();
            return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "on", StringComparison.OrdinalIgnoreCase);
        }
    }
}


