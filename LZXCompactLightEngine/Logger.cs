using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
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

        public string TimeStamp => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ";

        public void Log(Exception ex)
        {
            Log($"Exception details: {ex.ToString()}", 3);
        }

        public void Log(Exception ex, DirectoryInfo di)
        {
            Log($"Error while processing directory: {di.FullName}, {ex.ToString()}", 3);
        }

        public void Log(SecurityException ex, DirectoryInfo di)
        {
            Log($"Cannot access directory: {di.ToString()}, {ex.ToString()}", 3);
        }

        public void Log(Exception ex, string customMessage)
        {
            Log($"Error message: {customMessage}.{Environment.NewLine}Exception details: {ex.ToString()}", 3);
        }

        public void Log(Exception ex, FileInfo fi)
        {
            Log($"Error during processing: file: {fi.FullName}.{Environment.NewLine}Exception details: {ex.ToString()}", 3);
        }

        public void Log(UnauthorizedAccessException ex, FileInfo fi)
        {
            Log($"Cannot access file: {fi.FullName}", 3);
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
                    sb.Append(TimeStamp);

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
        Debug = 3,
        Trace = 4
    }
}
