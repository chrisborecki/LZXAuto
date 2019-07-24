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
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace LZXCompactLightEngine
{
    public class LZXCompactLightEngine
    {
        private ConcurrentDictionary<int, int> fileDict = new ConcurrentDictionary<int, int>();

        private const int fileSaveTimerMs = (int)30e3; //30 seconds
        private const int treadPoolWaitMs = 200;
        private const string dbFileName = "FileDict.db";

        private int fileCountProcessed = 0;
        private int fileCountSkipByNoChanges = 0;
        private int fileCountSkippedByAttributes = 0;
        private int fileCountSkippedByExtension = 0;
        private int threadQueueLength;

        private string[] skipFileExtensions;

        private long diskFreeSpace0, diskFreeSpace1, uncompressedFilesTotalSize;

        private readonly CancellationTokenSource cancelToken = new CancellationTokenSource();
        private readonly int maxQueueLength = Environment.ProcessorCount * 16;
        private readonly object lockObject = new object();
        private readonly Timer timer;

        public  Logger Logger { get; set; } = new Logger(LogFlags.General);

        public bool IsElevated
        {
            get
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
        }

        public LZXCompactLightEngine()
        {
            timer = new Timer(FileSaveTimerCallback, null, fileSaveTimerMs, fileSaveTimerMs);
        }

        public void Process(string path, string[] skipFileExtensionsArr)
        {
            skipFileExtensions = skipFileExtensionsArr ?? new string[] { };

            Logger.Log($"Starting new compressing session. LZXCompactLight version: {Assembly.GetEntryAssembly().GetName().Version}", 20);
            Logger.Log($"Running in Administrator mode: {IsElevated}", 2);
            Logger.Log($"Starting path {path}", 2);

            diskFreeSpace0 = DriveUtils.GetDriveFreeSpace(path);

            DateTime startTimeStamp = DateTime.Now;

            try
            {
                DirectoryInfo dirTop = new DirectoryInfo(path);

                LoadDictFromFile();

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
                        Logger.Log(fi, UnAuthTop);
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
                                Logger.Log(fi, UnAuthFile);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException UnAuthSubDir)
                    {
                        Logger.Log($"UnAuthSubDir: {UnAuthSubDir.Message}");
                    }
                }
            }
            catch (DirectoryNotFoundException DirNotFound)
            {
                Logger.Log(DirNotFound.Message);
            }
            catch (UnauthorizedAccessException UnAuthDir)
            {
                Logger.Log($"UnAuthDir: {UnAuthDir.Message}");
            }
            catch (PathTooLongException LongPath)
            {
                Logger.Log(LongPath.Message);
            }
            catch (Exception ex)
            {
                Logger.Log($"Other error: {ex.Message}");
            }
            finally
            {
                // Wait until all threads complete
                FinalizeThreadPool();

                Logger.Log("Completed");

                SaveDictToFile();

                TimeSpan ts = DateTime.Now.Subtract(startTimeStamp);
                int totalFilesVisited = fileCountProcessed + fileCountSkipByNoChanges + fileCountSkippedByAttributes + fileCountSkippedByExtension;

                diskFreeSpace1 = DriveUtils.GetDriveFreeSpace(path);
                long spaceSaved = Math.Max(0, uncompressedFilesTotalSize - DriveUtils.GetDriveUsedSpace(path));
                long spaceSavedThisSession = Math.Max(0, diskFreeSpace1 - diskFreeSpace0);

                StringBuilder statStr = new StringBuilder();

                Logger.Log(
                    $"Stats: {Environment.NewLine}" +
                    $"Files skipped by attributes: {fileCountSkippedByAttributes}{Environment.NewLine}" +
                    $"Files skipped by extension: { fileCountSkippedByExtension}{Environment.NewLine}" +
                    $"Files skipped by no change: { fileCountSkipByNoChanges}{Environment.NewLine}" +
                    $"Files processed by compact command line: { fileCountProcessed}{Environment.NewLine}" +
                    $"Total files visited: {totalFilesVisited}{Environment.NewLine}" +
                    $"Files in db: {fileDict?.Count ?? 0}{Environment.NewLine}" +
                    $"Drive capacity: {DriveUtils.GetMemoryString(DriveUtils.GetDriveCapacity(path))}{Environment.NewLine}" +
                    $"Approx space saved during this session: {DriveUtils.GetMemoryString(spaceSavedThisSession)}{Environment.NewLine}"
                    , 2, LogFlags.General);

                if (IsElevated)
                {
                    Logger.Log(
                        $"Files uncompressed on drive (beta): {DriveUtils.GetMemoryString(uncompressedFilesTotalSize)}{Environment.NewLine}" +
                        $"Drive capacity: {DriveUtils.GetMemoryString(DriveUtils.GetDriveCapacity(path))}{Environment.NewLine}" +
                        $"Approx space saved on drive (beta): {DriveUtils.GetMemoryString(spaceSaved)}{Environment.NewLine}",
                        0, LogFlags.General, false);
                }
                else
                {
                    Logger.Log("Cannot show additional stats because process is not running with Administrator rights.", 0, LogFlags.General, false);
                }

                Logger.Log(
                    $"Perf stats:{Environment.NewLine}" +
                    $"Time elapsed[hh:mm:ss:ms]: {ts.Hours.ToString("00")}:{ts.Minutes.ToString("00")}:{ts.Seconds.ToString("00")}:{ts.Milliseconds.ToString("00")}{Environment.NewLine}" +
                    $"Compressed files per minute: {((double)fileCountProcessed / (double)ts.TotalMinutes).ToString("0.00")}{Environment.NewLine}" +
                    $"Files per minute: {((double)totalFilesVisited / (double)ts.TotalMinutes).ToString("0.00")}", 2, LogFlags.General, false);
            }
        }

        private void ProcessFile(FileInfo fi)
        {
            try
            {
                Interlocked.Add(ref uncompressedFilesTotalSize, DriveUtils.GetDiskUncompressedFileSize(fi.Length, fi.FullName));

                if (skipFileExtensions.Any(c => c == fi.Extension))
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
                    Logger.Log("", 4, LogFlags.FileCompacting | LogFlags.FileSkipping);

                    int filePathHash = fi.FullName.GetHashCode();

                    int fileSizeHash;
                    if (fileDict.TryGetValue(filePathHash, out fileSizeHash) && fileSizeHash == fi.Length.GetHashCode())
                    {
                        Logger.Log($"Skipping file: ${fi.FullName} because it has been visited already and its size did not change", 1, LogFlags.FileSkipping);
                        Interlocked.Increment(ref fileCountSkipByNoChanges);
                        return;
                    }

                    fileDict[filePathHash] = fi.Length.GetHashCode();

                    Logger.Log($"Compressing file {fi.FullName}", 1, LogFlags.FileCompacting);
                    Interlocked.Increment(ref fileCountProcessed);

                    var proc = new Process();
                    proc.StartInfo.FileName = $"compact";
                    proc.StartInfo.Arguments = $"/c /exe:LZX \"{fi.FullName}\"";
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.Start();
                    proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                    string outPut = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    proc.Close();

                    Logger.Log(outPut, 2, LogFlags.Debug);
                }
            }
            finally
            {
                Interlocked.Decrement(ref threadQueueLength);
            }
        }

        public void ResetDb()
        {
            File.Delete(dbFileName);
        }

        private void SaveDictToFile()
        {
            try
            {
                lock (lockObject)
                {
                    BinaryFormatter binaryFormatter = new BinaryFormatter();
                    using (FileStream writerFileStream = new FileStream(dbFileName, FileMode.Create, FileAccess.Write))
                    {
                        Logger.Log("Saving file...", 1, LogFlags.Debug);

                        binaryFormatter.Serialize(writerFileStream, fileDict);

                        Logger.Log($"File saved, dictCount: {fileDict.Count}, fileSize: {writerFileStream.Length}", 1, LogFlags.Debug);

                        writerFileStream.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Unable to save dic to file, {ex.Message}");
            }
        }

        private void LoadDictFromFile()
        {
            if (File.Exists(dbFileName))
            {
                try
                {
                    Logger.Log("Dictionary file found");

                    BinaryFormatter binaryFormatter = new BinaryFormatter();
                    using (FileStream readerFileStream = new FileStream(dbFileName, FileMode.Open, FileAccess.Read))
                    {
                        if (readerFileStream.Length > 0)
                        {
                            fileDict = (ConcurrentDictionary<int, int>)binaryFormatter.Deserialize(readerFileStream);
                            readerFileStream.Close();
                        }
                    }

                    Logger.Log("Loaded from file");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error during loading from file: {ex.Message}" +
                        $"{Environment.NewLine}Terminating.");

                    Environment.Exit(-1);
                }
            }
            else
            {
                Logger.Log("DB file not found");
            }
        }

        private void FileSaveTimerCallback(object state)
        {
            Logger.Log("Saving dictionary file...", 1, LogFlags.Debug);
            SaveDictToFile();
        }

        public void Cancel()
        {
            Logger.Log("Terminating...", 4, LogFlags.General);
            cancelToken.Cancel();
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
    }


}
