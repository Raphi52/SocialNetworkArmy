using System;
using System.IO;

namespace SocialNetworkArmy.Utils
{
    public static class Logger
    {
        private static readonly string LogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Logs");
        private static readonly string LogFile = Path.Combine(LogDir, $"log_{DateTime.Now:yyyy-MM-dd}.log");

        static Logger()
        {
            if (!Directory.Exists(LogDir))
            {
                Directory.CreateDirectory(LogDir);
            }
        }

        public static void LogInfo(string message)
        {
            Log("INFO", message);
        }

        public static void LogWarning(string message)
        {
            Log("WARNING", message);
        }

        public static void LogError(string message)
        {
            Log("ERROR", message);
        }

        private static void Log(string level, string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logEntry = $"[{timestamp}][{level}] {message}\r\n";
            File.AppendAllText(LogFile, logEntry);
            Console.WriteLine(logEntry); // For debug
        }
    }
}