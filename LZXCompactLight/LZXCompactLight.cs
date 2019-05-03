using LZXCompactLightEngine;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LZXCompactLight
{
    class LZXCompactLight
    {
        private readonly static LZXCompactLightEngine.LZXCompactLightEngine compressorEngine = new LZXCompactLightEngine.LZXCompactLightEngine();
        private const string TaskScheduleTemplateFileName = "LZXCompactLightTask.xml";
        private const string TaskScheduleName = "LZXCompactLight";

        static void Main(string[] args)
        {
            Console.CancelKeyPress += new ConsoleCancelEventHandler(ConsoleTerminateHandler);

            // Parse help option
            if (args.Length == 0 || args.Contains("/?") || args.Contains("/help"))
            {
                Console.WriteLine($@"
Compress files to NTFS LZX compression with minimal disk write cycles.
                
Syntax: LZXCompactLight [/log:mode] [/resetDb] [/scheduleOn] [/scheduleOff] [/? | /help] [filePath]

Options:

/log: None, General, Stat, FileCompacing, FileSkipping, Debug - log level (comma separated list).
General and Stat levels are outputed to the console, other levels to log file only.
Default value: /log:General,Stat

/resetDb - resets db. On next run, all files will be traversed by Compact command.

/scheduleOn - enables Task Scheduler entry to run LZXCompactLight when computer is idle for 10 minutes. Task runs daily.

/scheduleOff - disables Task Scheduler entry

/? or /help - displays this help screen

filePath - root path to start. All subdirectories will be traversed. Default is current drive.

Description:
Windows 10 extended NTFS compression with LZX alghorithm. 
Files compressed with LZX can be opened like any other file, the uncompressing operation is transparent.
Compressing files, however, can be done only by running Compact command line. 
To keep the files compressed, windows Compact command needs to be re-run. This could be easily achieved with Task Scheduler.

There is a catch with SSD drives though.
When Compact command is being run on file already LZX-compressed, it will not try to recompress it.
However, if file is not compressible, Compact will try to recompress it every time, writing temp data to disk.

This is an issue on SSD drives, because of limited write cycles.
LZXCompactLight keeps record of file name and its last seen size. If the file has not changed since last LZXCompactLight run, attempt for recomressing will not be made. 
This saves SSD write cycles and speeds up processing time.
Iterating through files is multithreaded, one file per CPU logical core.

Typical use:
LZXCompactLight /scheduleOn c:\ 
");

                return;
            }

            // Parse resetDb option
            if (args.Contains("/resetDb"))
            {
                compressorEngine.ResetDb();
                return;
            }

            compressorEngine.LogLevel = LogLevel.General | LogLevel.Stat;

            // Parse log level option, like: /q:general,stat,fileskipping
            foreach (string arg in args)
            {
                Regex rx = new Regex(@"/log:(?<mode>[\w,]+)", RegexOptions.IgnoreCase);
                var match = rx.Match(arg);
                if (match.Success)
                {
                    compressorEngine.LogLevel = LogLevel.None;

                    string modeStr = match.Groups?["mode"]?.Value;
                    string[] modeArr = modeStr.Split(',');

                    foreach (string modeVal in modeArr)
                    {
                        LogLevel lm = LogLevel.General;
                        if (!Enum.TryParse<LogLevel>(modeVal, true, out lm))
                        {
                            compressorEngine.Log($"Unrecognised log level value: {modeVal}");
                            return;
                        }

                        compressorEngine.LogLevel |= lm;
                    }
                }
            }

            // Parse path option
            string commandLineRequestedPath = @"c:\";
            foreach (string arg in args)
            {
                Regex rx = new Regex(@"[a-z]:\\", RegexOptions.IgnoreCase);
                if (!string.IsNullOrEmpty(rx.Match(arg)?.Value))
                {
                    commandLineRequestedPath = arg;
                    if (!commandLineRequestedPath.EndsWith("\\"))
                    {
                        commandLineRequestedPath += "\\";
                    }
                }
            }

            // Parse scheduleOn option
            if (args.Contains("/scheduleOn"))
            {
                var currentProcess = Process.GetCurrentProcess();
                string currentProcessPath = currentProcess.MainModule.FileName;

                string requestedPath = commandLineRequestedPath;
                if (string.IsNullOrEmpty(requestedPath))
                    requestedPath = Path.GetPathRoot(currentProcessPath);

                string newCommandNode = $"<Command>\"{currentProcessPath}\"</Command>";
                string newArgumentsNode = $"<Arguments>{requestedPath}</Arguments>";
                string newWorkingDirectoryNode = $"<WorkingDirectory>{Path.GetDirectoryName(currentProcessPath)}</WorkingDirectory>";

                ReplaceText(TaskScheduleTemplateFileName, "<Command></Command>", newCommandNode);
                ReplaceText(TaskScheduleTemplateFileName, "<Arguments></Arguments>", newArgumentsNode);
                ReplaceText(TaskScheduleTemplateFileName, "<WorkingDirectory></WorkingDirectory>", newWorkingDirectoryNode);

                try
                {
                    var proc = new Process();
                    proc.StartInfo.FileName = $"schtasks";
                    proc.StartInfo.Arguments = $"/Create /XML {TaskScheduleTemplateFileName} /tn {TaskScheduleName}";
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.Start();
                    proc.WaitForExit();
                    int exitCode = proc.ExitCode;
                    if (exitCode == 0)
                    {
                        Console.WriteLine("Schedule initialized");
                    }
                    else
                    {
                        Console.WriteLine("Schedule initialization failed");
                    }

                    proc.Close();
                }
                finally
                {
                    ReplaceText(TaskScheduleTemplateFileName, newCommandNode, "<Command></Command>");
                    ReplaceText(TaskScheduleTemplateFileName, newArgumentsNode, "<Arguments></Arguments>");
                    ReplaceText(TaskScheduleTemplateFileName, newWorkingDirectoryNode, "<WorkingDirectory></WorkingDirectory>");
                }

                return;
            }

            // Parse scheduleOff option
            if (args.Contains("/scheduleOff"))
            {
                var proc = new Process();
                proc.StartInfo.FileName = $"schtasks";
                proc.StartInfo.Arguments = $"/Delete /tn {TaskScheduleName} /f";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.Start();
                proc.WaitForExit();
                int exitCode = proc.ExitCode;
                if (exitCode == 0)
                    Console.WriteLine("Schedule deleted");
                else
                    Console.WriteLine("Schedule deletion failed");

                proc.Close();

                return;
            }

            compressorEngine.Log("Starting new session", 20);
            compressorEngine.LoadFromFile();

            DateTime timeStamp = DateTime.Now;
            try
            {
                compressorEngine.Process(commandLineRequestedPath);
            }
            finally
            {
                compressorEngine.SaveToFile();

                TimeSpan ts = DateTime.Now.Subtract(timeStamp);
                int totalFiles = compressorEngine.fileCountProcessed + compressorEngine.fileCountSkipByNoChanges + compressorEngine.fileCountSkippedByAttributes + compressorEngine.fileCountSkippedByExtension;

                compressorEngine.Log(
                    $"Stats: {Environment.NewLine}" +
                    $"Files skipped by attributes: {compressorEngine.fileCountSkippedByAttributes}{Environment.NewLine}" +
                    $"Files skipped by extension: {compressorEngine.fileCountSkippedByExtension}{Environment.NewLine}" +
                    $"Files skipped by no change: { compressorEngine.fileCountSkipByNoChanges}{Environment.NewLine}" +
                    $"Files processed by compact command line: {compressorEngine.fileCountProcessed}{Environment.NewLine}" +
                    $"Total files visited: {totalFiles}{Environment.NewLine}" +
                    $"Files in db: {compressorEngine.FileDictCount}", 2);

                compressorEngine.Log(
                    $"Perf stats:{Environment.NewLine}" +
                    $"Time elapsed[hh:mm:ss:ms]: {ts.Hours.ToString("00")}:{ts.Minutes.ToString("00")}:{ts.Seconds.ToString("00")}:{ts.Milliseconds.ToString("00")}{Environment.NewLine}" +
                    $"Files per minute: {((double)totalFiles / (double)ts.TotalMinutes).ToString("0.00")}", 2);
            }
        }

        protected static void ConsoleTerminateHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            compressorEngine.Cancel();
        }

        private static void ReplaceText(string fileName, string sourceText, string replacementText)
        {
            string text = File.ReadAllText(fileName);
            text = text.Replace(sourceText, replacementText);
            File.WriteAllText(fileName, text);
        }
    }
}
