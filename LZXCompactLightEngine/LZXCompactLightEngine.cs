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
        private ConcurrentDictionary<int, uint> fileDict = new ConcurrentDictionary<int, uint>();

        //private const int fileSaveTimerMs = (int)30e3; //30 seconds
        private const int treadPoolWaitMs = 200;
        private const string dbFileName = "FileDict.db";

        private int fileCountProcessed = 0;
        private int fileCountSkipByNoChanges = 0;
        private int fileCountSkippedByAttributes = 0;
        private int fileCountSkippedByExtension = 0;
        private long diskSpaceSaved = 0;
        private int threadQueueLength;

        private string[] skipFileExtensions;

        private long diskFreeSpace0, diskFreeSpace1, uncompressedFilesTotalSize;

        private readonly CancellationTokenSource cancelToken = new CancellationTokenSource();
        private readonly int maxQueueLength = Environment.ProcessorCount * 16;
        private readonly object lockObject = new object();
        //private readonly Timer timer;

        public Logger Logger { get; set; } = new Logger(LogLevel.General);

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
            //timer = new Timer(FileSaveTimerCallback, null, fileSaveTimerMs, fileSaveTimerMs);
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
                LoadDictFromFile();

                DirectoryInfo dirTop = new DirectoryInfo(path);

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
                        Interlocked.Increment(ref threadQueueLength);

                        ThreadPool.QueueUserWorkItem(a =>
                        {
                            ProcessFile(fi);
                        });

                        // Do not let queue length more items than MaxQueueLength
                        while (threadQueueLength > maxQueueLength)
                        {
                            Thread.Sleep(treadPoolWaitMs);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, fi);
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
                                Interlocked.Increment(ref threadQueueLength);

                                ThreadPool.QueueUserWorkItem(a =>
                                {
                                    ProcessFile(fi);
                                });


                                // Do not let queue length more items than MaxQueueLength
                                while (threadQueueLength > maxQueueLength)
                                {
                                    Thread.Sleep(treadPoolWaitMs);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Log(ex, fi);
                            }
                        }

                        DirectoryRemoveCompressAttr(di);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, di);
                    }
                }

                DirectoryRemoveCompressAttr(dirTop);
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
                    $"Space saved during this session: {diskSpaceSaved.GetMemoryString()}{Environment.NewLine}"
                    , 2, LogLevel.General);

                //if (IsElevated)
                //{
                //}
                //else
                //{
                //}

                Logger.Log(
                    $"Perf stats:{Environment.NewLine}" +
                    $"Time elapsed[hh:mm:ss:ms]: {ts.Hours.ToString("00")}:{ts.Minutes.ToString("00")}:{ts.Seconds.ToString("00")}:{ts.Milliseconds.ToString("00")}{Environment.NewLine}" +
                    $"Compressed files per minute: {((double)fileCountProcessed / (double)ts.TotalMinutes).ToString("0.00")}{Environment.NewLine}" +
                    $"Files per minute: {((double)totalFilesVisited / (double)ts.TotalMinutes).ToString("0.00")}", 2, LogLevel.General, false);
            }
        }

        private void ProcessFile(FileInfo fi)
        {
            try
            {
                Interlocked.Add(ref uncompressedFilesTotalSize, fi.Length); // DriveUtils.GetDiskUncompressedFileSize(fi.Length, fi.FullName));

                if (skipFileExtensions.Any(c => c == fi.Extension))
                {
                    Interlocked.Increment(ref fileCountSkippedByExtension);
                    return;
                }

                if (fi.Attributes.HasFlag(FileAttributes.System))
                {
                    Interlocked.Increment(ref fileCountSkippedByAttributes);
                    return;
                }

                bool useForceCompress = false;
                if(fi.Attributes.HasFlag(FileAttributes.Compressed))
                {
                    File.SetAttributes(fi.FullName, fi.Attributes & ~FileAttributes.Compressed);
                    useForceCompress = true;
                }

                if (fi.Length > 0)
                {
                    Logger.Log("", 4, LogLevel.Debug);

                    int filePathHash = fi.FullName.GetHashCode();
                    uint currentFileSize1 = DriveUtils.GetCompressedFileSize(fi.FullName);

                    if (fileDict.TryGetValue(filePathHash, out uint dictFileSize) && dictFileSize == currentFileSize1)
                    {
                        Logger.Log($"Skipping file: '{fi.FullName}' because it has been visited already and its size ('{fi.Length.GetMemoryString()}') did not change", 1, LogLevel.Debug);
                        Interlocked.Increment(ref fileCountSkipByNoChanges);
                        return;
                    }

                    Logger.Log($"Compressing file {fi.FullName}", 1, LogLevel.Debug);
                    Interlocked.Increment(ref fileCountProcessed);

                    string outPut = CompactCommand($"/c /exe:LZX {(useForceCompress ? "/f" : "")} \"{fi.FullName}\"");

                    uint currentFileSize2 = DriveUtils.GetCompressedFileSize(fi.FullName);
                    fileDict[filePathHash] = currentFileSize2;

                    long fileDiskSize1 = DriveUtils.GetDiskUncompressedFileSize(currentFileSize1, fi.FullName);
                    long fileDiskSize2 = DriveUtils.GetDiskUncompressedFileSize(currentFileSize2, fi.FullName);
                    Interlocked.Add(ref diskSpaceSaved, fileDiskSize1 - fileDiskSize2);

                    Logger.Log(outPut, 2, LogLevel.Debug);
                }
            }
            catch(Exception ex)
            {
                Logger.Log(ex, fi);
            }
            finally
            {
                Interlocked.Decrement(ref threadQueueLength);
            }
        }

        private void DirectoryRemoveCompressAttr(DirectoryInfo dirTop)
        {
            if (dirTop.Attributes.HasFlag(FileAttributes.Compressed))
            {
                Logger.Log($"Removing NTFS compress flag on folder {dirTop.FullName} in favor of LZX compression", 1, LogLevel.General);

                string outPut = CompactCommand($"/u \"{dirTop.FullName}\"");

                Logger.Log(outPut, 2, LogLevel.Debug);
            }
        }

        private string CompactCommand(string arguments)
        {
            var proc = new Process();
            proc.StartInfo.FileName = $"compact";
            proc.StartInfo.Arguments = arguments;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;

            Logger.Log(arguments, 1, LogLevel.Debug, true);

            proc.Start();
            try
            {
                proc.PriorityClass = ProcessPriorityClass.Idle;
            }
            catch (InvalidOperationException)
            {
                Logger.Log("Process Compact exited before setting its pririty. Nothing to worry about.", 3, LogLevel.Debug);
            }

            string outPut = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            proc.Close();

            return outPut;
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
                        Logger.Log("Saving file...", 1, LogLevel.Debug);

                        binaryFormatter.Serialize(writerFileStream, fileDict);

                        Logger.Log($"File saved, dictCount: {fileDict.Count}, fileSize: {writerFileStream.Length}", 1, LogLevel.Debug);

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
                            fileDict = (ConcurrentDictionary<int, uint>)binaryFormatter.Deserialize(readerFileStream);
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

        //private void FileSaveTimerCallback(object state)
        //{
        //    Logger.Log("Saving dictionary file...", 1, LogFlags.Debug);
        //    SaveDictToFile();
        //}

        public void Cancel()
        {
            Logger.Log("Terminating...", 3, LogLevel.General);
            cancelToken.Cancel();
        }

        private void FinalizeThreadPool()
        {
            // Disable file save timer callback
            //timer.Change(Timeout.Infinite, Timeout.Infinite);

            // Wait for thread pool to complete
            while (threadQueueLength > 0)
            {
                Thread.Sleep(treadPoolWaitMs);
            }
        }
    }


}
