using LZXCompactLightEngine;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace LZXCompactLight
{
    class LZXCompactLight
    {
        private const string TaskScheduleName = "LZXCompactLight";
        private const string TaskScheduleTemplateFileName = "LZXCompactLightTask.xml";
        
        private readonly static LZXCompactLightEngine.LZXCompactLightEngine compressorEngine = new LZXCompactLightEngine.LZXCompactLightEngine();

        static void Main(string[] args)
        {
            string thisprocessname = Process.GetCurrentProcess().ProcessName;
            if (Process.GetProcesses().Count(p => p.ProcessName == thisprocessname) > 1)
            {
                compressorEngine.Log("Another instance is already running. Exiting...", 2, LogFlags.General);
                return;
            }

            Console.CancelKeyPress += new ConsoleCancelEventHandler(ConsoleTerminateHandler);

            // Parse help option
            if (args.Length == 0 || args.Contains("/?") || args.Contains("/help"))
            {
                Console.WriteLine($@"
Compress files to NTFS LZX compression with minimal disk write cycles.
                
Syntax: LZXCompactLight [/log:mode] [/resetDb] [/scheduleOn] [/scheduleOff] [/? | /help] [filePath]

Options:

/log: None, General, Stat, FileCompacing, FileSkipping, Debug - log flags (comma separated list).
General and Stat levels are outputed to both log file and the console, other levels to log file only.
Default value: /log:General, Stat

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

Version number: {Assembly.GetEntryAssembly().GetName().Version}
");

                return;
            }

            // Parse resetDb option
            if (args.Contains("/resetDb"))
            {
                compressorEngine.ResetDb();
                return;
            }

            compressorEngine.LogFlags = LogFlags.General | LogFlags.Stat;

            // Parse log level option, like: /q:general,stat,fileskipping
            foreach (string arg in args)
            {
                Regex rx = new Regex(@"/log:(?<mode>[\w,]+)", RegexOptions.IgnoreCase);
                var match = rx.Match(arg);
                if (match.Success)
                {
                    compressorEngine.LogFlags = LogFlags.None;

                    string modeStr = match.Groups?["mode"]?.Value;
                    string[] modeArr = modeStr.Split(',');

                    foreach (string modeVal in modeArr)
                    {
                        LogFlags lm = LogFlags.General;
                        if (!Enum.TryParse<LogFlags>(modeVal, true, out lm))
                        {
                            Console.WriteLine($"Unrecognised log level value: {modeVal}");
                            return;
                        }

                        compressorEngine.LogFlags |= lm;
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

                ReplaceTextInFile(TaskScheduleTemplateFileName, "<Command></Command>", newCommandNode);
                ReplaceTextInFile(TaskScheduleTemplateFileName, "<Arguments></Arguments>", newArgumentsNode);
                ReplaceTextInFile(TaskScheduleTemplateFileName, "<WorkingDirectory></WorkingDirectory>", newWorkingDirectoryNode);

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
                    ReplaceTextInFile(TaskScheduleTemplateFileName, newCommandNode, "<Command></Command>");
                    ReplaceTextInFile(TaskScheduleTemplateFileName, newArgumentsNode, "<Arguments></Arguments>");
                    ReplaceTextInFile(TaskScheduleTemplateFileName, newWorkingDirectoryNode, "<WorkingDirectory></WorkingDirectory>");
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

            compressorEngine.Process(commandLineRequestedPath);
        }

        private static void ConsoleTerminateHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            compressorEngine.Cancel();
        }

        private static void ReplaceTextInFile(string fileName, string sourceText, string replacementText)
        {
            string text = File.ReadAllText(fileName);
            text = text.Replace(sourceText, replacementText);
            File.WriteAllText(fileName, text);
        }
    }
}
