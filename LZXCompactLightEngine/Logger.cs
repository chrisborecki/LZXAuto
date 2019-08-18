using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LZXCompactLightEngine
{
    public class Logger
    {
        private const string logFileName = "Activity.log";
        private readonly object lockObject = new object();

        public LogLevel LogLevel { get; set; }

        public Logger(LogLevel logLevel)
        {
            LogLevel = logLevel;
        }

        public void Log(Exception ex, string customMessage)
        {
            if (LogLevel == LogLevel.None)
                return;

            Log($"Error encountered: {customMessage}.");
            Log($"Stack trace: {ex.StackTrace}");
        }

        public void Log(FileInfo fi, Exception ex)
        {
            Log($"Error during processing: file: {fi.FullName}, exception message: {ex.Message}");
        }

        public void Log(string str, int newLinePrefix = 1, LogLevel level = LogLevel.Info, bool showTimeStamp = true)
        {
            if (((int)LogLevel < (int)level))
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < newLinePrefix; i++, sb.AppendLine()) ;

            if (!string.IsNullOrEmpty(str))
            {
                if (showTimeStamp)
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ");

                sb.Append(str);
            }

            string result = sb.ToString();
            Console.WriteLine(result);

            lock (lockObject)
            {
                File.AppendAllText(logFileName, result);
            }
        }
    }


    public enum LogLevel
    {
        None = 0,
        General = 1,
        Info = 2,
        Debug = 4,
        Trace = 8
    }
}
