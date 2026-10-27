using System;
using System.IO;

namespace MxMessageProcessor
{
    internal static class FileLogger
    {
        private static readonly string LogDirectory =
            Path.Combine(AppContext.BaseDirectory, "Logs");

        private static readonly string LogFile =
            Path.Combine(LogDirectory, "mx_processor.log");

        public static void Log(string message)
        {
            Directory.CreateDirectory(LogDirectory);

            string logEntry =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            File.AppendAllText(LogFile, logEntry + Environment.NewLine);
        }
    }
}