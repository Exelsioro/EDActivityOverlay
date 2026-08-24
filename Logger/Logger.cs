namespace Logger
{
    public static class Logger
    {
        private static string? logFilePath;
        private static StreamWriter? logWriter;
        private static readonly object lockObject = new object();

        static Logger()
        {
            InitializeLogger();
        }

        private static void InitializeLogger()
        {
            try
            {
                // Create logs directory if it doesn't exist
                string logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logsDirectory))
                {
                    Directory.CreateDirectory(logsDirectory);
                }

                // Create unique log file with timestamp for each startup
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string logFileName = $"overlay_log_{timestamp}.txt";
                logFilePath = Path.Combine(logsDirectory, logFileName);

                logWriter = new StreamWriter(logFilePath, append: true);
                logWriter.AutoFlush = true;

                WriteLog("INFO", "Logger initialized successfully");
                WriteLog("INFO", $"Log file: {logFilePath}");
                WriteLog("INFO", "=== ED Activity Overlay Session Started ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }

        private static void WriteLog(string level, string message)
        {
            lock (lockObject)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string logEntry = $"[{timestamp}] [{level}] {message}";

                    Console.WriteLine(logEntry);
                    logWriter?.WriteLine(logEntry);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to write log: {ex.Message}");
                }
            }
        }

        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public static void Warning(string message)
        {
            WriteLog("WARN", message);
        }

        public static void Error(string message)
        {
            WriteLog("ERROR", message);
        }

        public static void Debug(string message)
        {
            WriteLog("DEBUG", message);
        }

        // Log user actions specifically
        public static void LogUserAction(string action, object? parameters = null)
        {
            string paramStr = parameters != null ? $" | Parameters: {parameters}" : "";
            WriteLog("USER_ACTION", $"{action}{paramStr}");
        }

        // Log search attempts with details
        public static void LogSearchAttempt(string origin, string distance, int cargo, string pad)
        {
            WriteLog("SEARCH", $"Trade route search - Origin: {origin}, Distance: {distance}, Cargo: {cargo}, Pad: {pad}");
        }

        // Log critical extraction failures with context
        public static void LogCriticalExtractionFailure(string methodName, string nodeClass, string outerHtml, string reason = "")
        {
            string htmlSnippet = outerHtml?.Length > 200 ? outerHtml.Substring(0, 200) + "..." : outerHtml ?? "<null>";
            string reasonText = !string.IsNullOrEmpty(reason) ? $" | Reason: {reason}" : "";
            WriteLog("CRITICAL_EXTRACTION_FAILURE", $"{methodName} - Class: {nodeClass} | HTML: {htmlSnippet}{reasonText}");
        }

        // Log numeric parsing failures with context
        public static void LogNumericParsingFailure(string methodName, string inputText, string reason = "")
        {
            string reasonText = !string.IsNullOrEmpty(reason) ? $" | Reason: {reason}" : "";
            WriteLog("NUMERIC_PARSING_FAILURE", $"{methodName} - Input: '{inputText}'{reasonText}");
        }

        public static void Close()
        {
            try
            {
                WriteLog("INFO", "=== ED Activity Overlay Session Ended ===");
                WriteLog("INFO", "Logger shutting down");
                logWriter?.Close();
                logWriter?.Dispose();
                logWriter = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing logger: {ex.Message}");
            }
        }
    }
}
