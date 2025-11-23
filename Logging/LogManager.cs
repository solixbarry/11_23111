using System;
using System.IO;

namespace TradingSystem.Logging
{
    /// <summary>
    /// Persistent logging with timestamped files
    /// Logs don't overwrite - each run gets its own file
    /// </summary>
    public class LogManager : IDisposable
    {
        private readonly string _logDirectory;
        private readonly StreamWriter _logWriter;
        private readonly StreamWriter _tradeWriter;
        private readonly string _logFile;
        private readonly string _tradeFile;

        public LogManager(string logDirectory = "logs")
        {
            _logDirectory = logDirectory;

            // Create log directory if it doesn't exist
            Directory.CreateDirectory(_logDirectory);

            // Create timestamped log files
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            _logFile = Path.Combine(_logDirectory, $"trading_{timestamp}.log");
            _tradeFile = Path.Combine(_logDirectory, $"trades_{timestamp}.csv");

            // Initialize log writers with auto-flush
            _logWriter = new StreamWriter(_logFile, append: true) { AutoFlush = true };
            _tradeWriter = new StreamWriter(_tradeFile, append: true) { AutoFlush = true };

            // Write CSV header for trades file
            _tradeWriter.WriteLine("Timestamp,Strategy,Side,Size(BTC),Price(USD),Value(USD),OrderId");

            Log("----------------------------------------------");
            Log($"Log session started: {timestamp}");
            Log($"Log file: {_logFile}");
            Log($"Trade file: {_tradeFile}");
            Log("-----------------------------------------------");
        }

        /// <summary>
        /// Log a message with timestamp
        /// </summary>
        public void Log(string message)
        {
            string timestamped = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {message}";

            // Write to console
            Console.WriteLine(timestamped);

            // Write to file
            _logWriter.WriteLine(timestamped);
        }

        /// <summary>
        /// Log a trade execution
        /// </summary>
        public void LogTrade(string strategy, string side, double sizeBTC, double priceUSD, string orderId)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            double valueUSD = sizeBTC * priceUSD;

            // Write to trades CSV
            _tradeWriter.WriteLine($"{timestamp},{strategy},{side},{sizeBTC:F6},{priceUSD:F2},{valueUSD:F2},{orderId}");

            // Also log to main log
            Log($"TRADE EXECUTED | {strategy} | {side} | {sizeBTC:F6} BTC @ ${priceUSD:F2} = ${valueUSD:F2} | Order: {orderId}");
        }

        /// <summary>
        /// Log strategy signal
        /// </summary>
        public void LogSignal(string strategy, string direction, double confidence, string reason)
        {
            Log($"SIGNAL | {strategy} | {direction} | Confidence: {confidence:F2} | {reason}");
        }

        /// <summary>
        /// Log error
        /// </summary>
        public void LogError(string context, string error)
        {
            Log($"ERROR | {context} | {error}");
        }

        /// <summary>
        /// Log warning
        /// </summary>
        public void LogWarning(string context, string warning)
        {
            Log($"WARNING | {context} | {warning}");
        }

        /// <summary>
        /// Log system status
        /// </summary>
        public void LogStatus(string status)
        {
            Log($"STATUS | {status}");
        }

        /// <summary>
        /// Log separator for readability
        /// </summary>
        public void LogSeparator()
        {
            Log("───────────────────────────────────────────────");
        }

        /// <summary>
        /// List all log files in directory
        /// </summary>
        public static void ListLogFiles(string logDirectory = "logs")
        {
            if (!Directory.Exists(logDirectory))
            {
                Console.WriteLine($"Log directory not found: {logDirectory}");
                return;
            }

            Console.WriteLine($"\nLog files in {logDirectory}:");
            Console.WriteLine("═══════════════════════════════════════════════");

            var logFiles = Directory.GetFiles(logDirectory, "*.log");
            var tradeFiles = Directory.GetFiles(logDirectory, "*.csv");

            Console.WriteLine("\nTrading Logs:");
            foreach (var file in logFiles)
            {
                var info = new FileInfo(file);
                Console.WriteLine($"  {Path.GetFileName(file)} ({info.Length / 1024:F1} KB) - {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }

            Console.WriteLine("\nTrade CSVs:");
            foreach (var file in tradeFiles)
            {
                var info = new FileInfo(file);
                Console.WriteLine($"  {Path.GetFileName(file)} ({info.Length / 1024:F1} KB) - {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }

            Console.WriteLine("═══════════════════════════════════════════════\n");
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            Log("═══════════════════════════════════════════════");
            Log("Log session ended");
            Log("═══════════════════════════════════════════════");

            _logWriter?.Dispose();
            _tradeWriter?.Dispose();
        }
    }

    /// <summary>
    /// Example usage in your main program
    /// </summary>
    public class LogManagerExample
    {
        public static void Example()
        {
            // Initialize logger
            using (var logger = new LogManager())
            {
                // Log normal messages
                logger.Log("System started");
                logger.LogStatus("Connecting to Binance...");
                logger.Log("Connected successfully");

                // Log signals
                logger.LogSignal("OBI", "BUY", 0.75, "Strong bid imbalance detected");

                // Log trades
                logger.LogTrade("OBI", "BUY", 0.012, 84000, "123456789");

                // Log errors
                logger.LogError("OrderExecutor", "Insufficient balance");

                // Log warnings
                logger.LogWarning("RiskManager", "Approaching daily loss limit");

                // Add separators for readability
                logger.LogSeparator();
                logger.Log("Starting trading loop...");

            } // Dispose automatically called here

            // List all log files
            LogManager.ListLogFiles();
        }
    }
}
