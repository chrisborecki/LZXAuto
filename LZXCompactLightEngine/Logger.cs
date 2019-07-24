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

        public LogFlags LogFlags { get; set; }

        public Logger(LogFlags logFlags)
        {
            LogFlags = logFlags;
        }

        public void Log(Exception ex, string customMessage)
        {
            Log($"Error encountered: {customMessage}.");

            if (!string.IsNullOrEmpty(customMessage))
            {
                Log(customMessage);
            }

            if (LogFlags.HasFlag(LogFlags.Debug))
            {
                Log($"Stack trace: {ex.StackTrace}");
            }
        }

        public void Log(FileInfo fi, Exception ex)
        {
            Log($"Error during processing: file: {fi.FullName}, exception message: {ex.Message}");
        }

        public void Log(string str, int newLinePrefix = 1, LogFlags level = LogFlags.General, bool showTimeStamp = true)
        {
            if (!LogFlags.HasFlag(level))
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < newLinePrefix; i++, sb.AppendLine()) ;

            if (showTimeStamp && !string.IsNullOrEmpty(str))
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + str);

            string result = sb.ToString();

            if (LogFlags.HasFlag(LogFlags.General) && level.HasFlag(LogFlags.General) ||
                LogFlags.HasFlag(LogFlags.Stat) && level.HasFlag(LogFlags.Stat))
            {
                Console.WriteLine(result);
            }

            lock (lockObject)
            {
                File.AppendAllText(logFileName, result);
            }
        }
    }

    [Flags]
    public enum LogFlags
    {
        None = 0,
        General = 1,
        Stat = 2,
        FileCompacting = 4,
        FileSkipping = 8,
        Debug = 16
    }
}
