/*  
 *  LZX compression helper for Windows 10
 *  Copyright (c) 2019 Christopher Borecki
 * 
 *  MIT Licence
 * 
 * */
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;

namespace LZXCompactLightEngine
{
    public class LZXCompactLightEngine
    {
        private const int treadPoolWaitMs = 200;
        private const int fileSaveTimerMs = (int)30e3; //30 seconds
        private const string dbFileName = "FileDict.db";
        private const string logFileName = "Activity.log";

        private Timer timer;
        private int threadQueueLength;
        private int fileCountProcessed = 0;
        private int fileCountSkipByNoChanges = 0;
        private int fileCountSkippedByExtension = 0;
        private int fileCountSkippedByAttributes = 0;
        private ConcurrentDictionary<int, int> fileDict = new ConcurrentDictionary<int, int>();

        private readonly object lockObject = new object();
        private readonly int maxQueueLength = Environment.ProcessorCount * 16;
        private readonly BinaryFormatter binaryFormatter = new BinaryFormatter();
        private readonly CancellationTokenSource cancelToken = new CancellationTokenSource();
        private readonly string[] skipCompression = new string[] { ".zip", ".gif", ".7z", ".bmp", ".jpeg", ".jpg", ".mov", ".mp3", ".avi", ".cab", ".mpeg" };

        public LogFlags LogFlags { get; set; } = LogFlags.General;

        public LZXCompactLightEngine()
        {
            timer = new Timer(FileSaveTimerCallback, null, fileSaveTimerMs, fileSaveTimerMs);
        }
        public void ResetDb()
        {
            File.Delete(dbFileName);
        }

        public void Cancel()
        {
            Log("Terminating...", 4, LogFlags.General);
            cancelToken.Cancel();
        }
    
        public void Process(string path = "c:\\")
        {
            Log($"Starting new compressing session. LZXCompactLight version: {Assembly.GetEntryAssembly().GetName().Version}", 20);
            Log($"Starting path {path}", 2);

            DateTime startTimeStamp = DateTime.Now;

            try
            {
                DirectoryInfo dirTop = new DirectoryInfo(path);

                LoadFromFile();

                foreach (var fi in dirTop.EnumerateFiles())
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        // wait until all threads complete
                        FinalizeThreadPool();
                        break;
                    }

                    try
                    {
                        ThreadPool.QueueUserWorkItem(a =>
                        {
                            ProcessFile(fi);
                        });

                        Interlocked.Increment(ref threadQueueLength);

                        // Do not let queue length more items than MaxQueueLength
                        while (threadQueueLength > maxQueueLength)
                        {
                            Thread.Sleep(treadPoolWaitMs);
                        }
                    }
                    catch (UnauthorizedAccessException UnAuthTop)
                    {
                        Log(fi, UnAuthTop);
                    }
                }

                foreach (var di in dirTop.EnumerateDirectories("*"))
                {
                    try
                    {
                        foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            if (cancelToken.IsCancellationRequested)
                            {
                                // wait until all threads complete
                                FinalizeThreadPool();
                                break;
                            }

                            try
                            {
                                ThreadPool.QueueUserWorkItem(a =>
                                {
                                    ProcessFile(fi);
                                });

                                Interlocked.Increment(ref threadQueueLength);

                                // Do not let queue length more items than MaxQueueLength
                                while (threadQueueLength > maxQueueLength)
                                {
                                    Thread.Sleep(treadPoolWaitMs);
                                }
                            }
                            catch (UnauthorizedAccessException UnAuthFile)
                            {
                                Log(fi, UnAuthFile);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException UnAuthSubDir)
                    {
                        Log($"UnAuthSubDir: {UnAuthSubDir.Message}");
                    }
                }
            }
            catch (DirectoryNotFoundException DirNotFound)
            {
                Log(DirNotFound.Message);
            }
            catch (UnauthorizedAccessException UnAuthDir)
            {
                Log($"UnAuthDir: {UnAuthDir.Message}");
            }
            catch (PathTooLongException LongPath)
            {
                Log(LongPath.Message);
            }
            catch (Exception ex)
            {
                Log($"Other error: {ex.Message}");
            }
            finally
            {
                // Wait until all threads complete
                FinalizeThreadPool();

                Log("Completed");

                SaveToFile();

                TimeSpan ts = DateTime.Now.Subtract(startTimeStamp);
                int totalFilesVisited = fileCountProcessed + fileCountSkipByNoChanges + fileCountSkippedByAttributes + fileCountSkippedByExtension;

                Log(
                    $"Stats: {Environment.NewLine}" +
                    $"Files skipped by attributes: {fileCountSkippedByAttributes}{Environment.NewLine}" +
                    $"Files skipped by extension: { fileCountSkippedByExtension}{Environment.NewLine}" +
                    $"Files skipped by no change: { fileCountSkipByNoChanges}{Environment.NewLine}" +
                    $"Files processed by compact command line: { fileCountProcessed}{Environment.NewLine}" +
                    $"Total files visited: {totalFilesVisited}{Environment.NewLine}" +
                    $"Files in db: {fileDict?.Count ?? 0}", 2);

                Log(
                    $"Perf stats:{Environment.NewLine}" +
                    $"Time elapsed[hh:mm:ss:ms]: {ts.Hours.ToString("00")}:{ts.Minutes.ToString("00")}:{ts.Seconds.ToString("00")}:{ts.Milliseconds.ToString("00")}{Environment.NewLine}" +
                    $"Files per minute: {((double)totalFilesVisited / (double)ts.TotalMinutes).ToString("0.00")}", 2);
            }
        }

        public void Log(FileInfo fi, Exception ex)
        {
            Log($"Error during processing: file: {fi.FullName}, exception message: {ex.Message}");
        }

        public void Log(string str, int newLinePrefix = 1, LogFlags level = LogFlags.General)
        {
            if (!LogFlags.HasFlag(level))
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < newLinePrefix; i++, sb.AppendLine());

            if (!string.IsNullOrEmpty(str))
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

        private void FinalizeThreadPool()
        {
            // Disable file save timer callback
            timer.Change(Timeout.Infinite, Timeout.Infinite);

            // Wait for thread pool to complete
            while (threadQueueLength > 0)
            {
                Thread.Sleep(treadPoolWaitMs);
            }
        }

        private void ProcessFile(FileInfo fi)
        {
            try
            {
                if (skipCompression.Any(c => c == fi.Extension))
                {
                    Interlocked.Increment(ref fileCountSkippedByExtension);
                    return;
                }

                if (fi.Attributes.HasFlag(FileAttributes.System) || fi.Attributes.HasFlag(FileAttributes.Compressed))
                {
                    Interlocked.Increment(ref fileCountSkippedByAttributes);
                    return;
                }

                if (fi.Length > 0)
                {
                    Log("", 4, LogFlags.FileCompacting | LogFlags.FileSkipping);

                    int filePathHash = fi.FullName.GetHashCode();

                    int fileSizeHash;
                    if (fileDict.TryGetValue(filePathHash, out fileSizeHash) && fileSizeHash == fi.Length.GetHashCode())
                    {
                        Log($"Skipping file: ${fi.FullName} because it has been visited already and its size did not change", 1, LogFlags.FileSkipping);
                        Interlocked.Increment(ref fileCountSkipByNoChanges);
                        return;
                    }

                    fileDict[filePathHash] = fi.Length.GetHashCode();

                    Log($"Compressing file {fi.FullName}", 1, LogFlags.FileCompacting);
                    Interlocked.Increment(ref fileCountProcessed);

                    var proc = new Process();
                    proc.StartInfo.FileName = $"compact";
                    proc.StartInfo.Arguments = $"/c /exe:LZX \"{fi.FullName}\"";
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.Start();
                    string outPut = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    proc.Close();

                    Log(outPut, 2, LogFlags.Debug);
                }
            }
            finally
            {
                Interlocked.Decrement(ref threadQueueLength);
            }
        }

        private void SaveToFile()
        {
            try
            {
                Log("Saving file...", 1, LogFlags.Debug);
                lock (lockObject)
                {
                    using (FileStream writerFileStream = new FileStream(dbFileName, FileMode.Create, FileAccess.Write))
                    {
                        binaryFormatter.Serialize(writerFileStream, fileDict);
                        writerFileStream.Close();
                    }
                }

                Log("File saved", 1, LogFlags.Debug);
            }
            catch (Exception ex)
            {
                Log($"Unable to save dic to file, {ex.Message}");
            }
        }

        private void LoadFromFile()
        {
            if (File.Exists(dbFileName))
            {
                try
                {
                    Log("Dictionary file found");

                    using (FileStream readerFileStream = new FileStream(dbFileName, FileMode.Open, FileAccess.Read))
                    {
                        if (readerFileStream.Length > 0)
                        {
                            fileDict = (ConcurrentDictionary<int, int>)this.binaryFormatter.Deserialize(readerFileStream);
                            readerFileStream.Close();
                        }
                    }

                    Log("Loaded from file");
                }
                catch (Exception ex)
                {
                    Log($"Error during loading from file: {ex.Message}" +
                        $"{Environment.NewLine}Terminating.");

                    Environment.Exit(-1);
                }
            }
            else
            {
                Log("DB file not found");
            }
        }

        private void FileSaveTimerCallback(object state)
        {
            Log("Saving dictionary file...", 1, LogFlags.Debug);
            SaveToFile();
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
